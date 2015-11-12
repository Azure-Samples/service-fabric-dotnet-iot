// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;

    /// <summary>
    /// CQRS implementation that uses multiple IReliableQueue<WorkItem> as storage.
    /// Work Manager performs:
    /// 0- Posting to work manager
    /// 1- Fan out based on Workitem.QueueName
    /// 2- Concurrent Execution on Queues. 
    /// 3- Fair execution, each queue gets a slice (# of dequeue before yeilding)
    /// 4- Does not allow executers to compete on one queue. 1:1 queue to executer 
    /// at any point of time. 
    /// 5- Supports Handler Management
    /// 6- Queue graceful removal 
    /// </summary>
    /// <typeparam name="Handler"></typeparam>
    /// <typeparam name="Wi"></typeparam>
    public partial class WorkManager<Handler, Wi> where Handler : IWorkItemHandler<Wi>, new()
        where Wi : IWorkItem

    {
        // defaults
        public static readonly string s_Queue_Names_Dictionary = "_QueueNames_"; // the dictionary that will hold a list (per entry) of each queue created. 
        public static readonly uint s_Default_Yield_Queue_After = 10; // to ensure fairness, each queue will be de-queued 10 times before moving to the next.
        public static readonly uint s_Max_Num_OfWorker = (uint) Environment.ProcessorCount*2;
        // default is one task per processor, note: this does not mean affinity or what so ever.

        public static readonly uint s_MaxNumOfBufferedWorkItems = 10*1000;
        public static readonly uint s_defaultMaxNumOfWorkers = 2;
        private ITraceWriter m_TraceWriter = null; // used to write trace events. 
        private long m_NumOfBufferedWorkItems = 0; // current num of buffered items
        private QueueManager<Handler, Wi> m_QueueManager = null; // queue manager (saves queues names in state).
        private ConcurrentDictionary<string, Handler> m_handlers = new ConcurrentDictionary<string, Handler>(); // list of handlers
        private ConcurrentDictionary<string, WorkExecuter<Handler, Wi>> m_Executers = new ConcurrentDictionary<string, WorkExecuter<Handler, Wi>>();
        // worker tasks.

        private DeferredTaskExecuter m_DeferedTaskExec = new DeferredTaskExecuter(); // supports limited background processing. 
        private Clicker<WorkManagerClick, int> m_MinuteClicker = new Clicker<WorkManagerClick, int>(TimeSpan.FromMinutes(1));
        private Clicker<WorkManagerClick, int> m_HourClicker = new Clicker<WorkManagerClick, int>(TimeSpan.FromHours(1));

        public WorkManager(IReliableStateManager StateManager, ITraceWriter TraceWriter)
        {
            if (null == StateManager)
            {
                throw new ArgumentNullException("StateManager");
            }

            // wire up the minute clicker. 
            this.m_MinuteClicker.OnTrim = (head) => this.OnMinuteClickerTrim(head);

            this.StateManager = StateManager;

            this.m_TraceWriter = TraceWriter;


            this.m_TraceWriter.TraceMessage("a new work manager was created for this replica");
        }

        public async Task PostWorkItemAsync(Wi workItem)
        {
            if (this.WorkManagerStatus != WorkManagerStatus.Working)
            {
                throw new InvalidOperationException("Work Manager is not working state");
            }

            if (this.m_NumOfBufferedWorkItems >= this.m_MaxNumOfBufferedWorkItems)
            {
                throw new InvalidOperationException(string.Format("Work Manger is at maximum buffered work items:{0}", this.m_NumOfBufferedWorkItems));
            }


            try
            {
                // Which Q
                IReliableQueue<Wi> targetQueue = await this.m_QueueManager.GetOrAddQueueAsync(workItem.QueueName);

                // enqueue
                using (ITransaction tx = this.StateManager.CreateTransaction())
                {
                    await targetQueue.EnqueueAsync(tx, workItem, TimeSpan.FromSeconds(10), CancellationToken.None);
                    await tx.CommitAsync();
                }
                this.IncreaseBufferedWorkItems();
                this.m_DeferedTaskExec.AddWork(this.TryIncreaseExecuters);
            }
            catch (AggregateException aex)
            {
                AggregateException ae = aex.Flatten();

                this.m_TraceWriter.TraceMessage(
                    string.Format(
                        "Post to work manager failed, caller should retry E:{0} StackTrace:{1}",
                        ae.GetCombinedExceptionMessage(),
                        ae.GetCombinedExceptionStackTrace()));
                throw;
            }
        }

        #region Specs 

        private WorkItemHandlerMode m_WorkItemHandlerMode = WorkItemHandlerMode.Singlton; // defines how handlers are and mapped to queues
        // starting # of max workers
        private uint m_MaxNumOfWorkers = s_defaultMaxNumOfWorkers;
        // Maximum allowed work item in *all queues*
        private uint m_MaxNumOfBufferedWorkItems = s_MaxNumOfBufferedWorkItems;
        // each worker will pump and yield after
        private uint m_Yield_Queue_After = s_Default_Yield_Queue_After;
        // remove queue if stayeed empty for? 
        private long m_RemoveEmptyQueueAfterTicks = TimeSpan.FromMinutes(1).Ticks;

        #endregion

        #region Execusion Management 

        /// <summary>
        /// attempts to increase executers. 
        /// </summary>
        /// <returns></returns>
        private Task TryIncreaseExecuters()
        {
            int currentNumOfExecuters = this.m_Executers.Count;
            int qNumber = this.m_QueueManager.Count;


            //we maxed out ?
            // if we add, will it be more than queues?
            if (currentNumOfExecuters == this.m_MaxNumOfWorkers || (currentNumOfExecuters + 1) > qNumber)
            {
                return Task.FromResult(0);
            }

            // we are good add
            WorkExecuter<Handler, Wi> newExecuter = new WorkExecuter<Handler, Wi>(this);
            newExecuter.Start();
            this.m_Executers.AddOrUpdate(newExecuter.m_WorkerExecuterId, newExecuter, (k, v) => { return newExecuter; });


            return Task.FromResult(0);
        }

        private Task TryDecreaseExecuters()
        {
            int currentNumOfExecuters = this.m_Executers.Count;
            int qNumber = this.m_QueueManager.Count;
            if (0 == currentNumOfExecuters || currentNumOfExecuters <= qNumber) // not much we can do here
            {
                Trace.WriteLine(string.Format("a remove request denied E#:{0} Q#:{1}", this.m_Executers.Count, qNumber));
                return Task.FromResult(0);
            }

            // first 
            KeyValuePair<string, WorkExecuter<Handler, Wi>> kvpOtherExecuter = this.m_Executers.First(kvp => true);
            if (kvpOtherExecuter.Value != null)
            {
                WorkExecuter<Handler, Wi> OtherExecuter;
                bool bSucess = this.m_Executers.TryRemove(kvpOtherExecuter.Key, out OtherExecuter);
                if (bSucess)
                {
                    OtherExecuter.Stop();
                    OtherExecuter = null;
                }
            }
            return Task.FromResult(0);
        }

        private Handler GetHandlerForQueue(string qName)
        {
            switch (this.m_WorkItemHandlerMode)
            {
                case WorkItemHandlerMode.Singlton:
                {
                    return this.m_handlers.GetOrAdd("0", (key) => { return new Handler(); });
                }
                case WorkItemHandlerMode.PerWorkItem:
                {
                    return new Handler();
                }
                case WorkItemHandlerMode.PerQueue:
                {
                    return this.m_handlers.GetOrAdd(qName, (key) => { return new Handler(); });
                }
            }
            return default(Handler);
        }

        private Task RemoveHandlerForQueue(string qName)
        {
            return Task.Run(
                () =>
                {
                    Handler handler;
                    this.m_handlers.TryRemove(qName, out handler);

                    // todo: Future phases, check if handler 
                    // implements IDisposable and dispose it if so.
                });
        }

        #endregion

        #region Telemetry Management

        private async Task LoadNumOfBufferedItems()
        {
            long buffered = 0;


            foreach (string qName in this.m_QueueManager.QueueNames)
            {
                IReliableQueue<Wi> q = await this.m_QueueManager.GetOrAddQueueAsync(qName);
                buffered += await q.GetCountAsync();
            }
            this.m_NumOfBufferedWorkItems = buffered;
        }

        private void IncreaseBufferedWorkItems()
        {
            Interlocked.Increment(ref this.m_NumOfBufferedWorkItems);
            this.m_MinuteClicker.Click(new WorkManagerClick() {ClickType = WorkerManagerClickType.Posted});
        }

        private void DecreaseBufferedWorkItems()
        {
            Interlocked.Decrement(ref this.m_NumOfBufferedWorkItems);
            this.m_MinuteClicker.Click(new WorkManagerClick() {ClickType = WorkerManagerClickType.Processed});
        }

        private void OnMinuteClickerTrim(WorkManagerClick head)
        {
            // roll up totals into mins 
            int totalAdd = 0;
            int totalProcessed = 0;

            WorkManagerClick curr = head;
            while (curr != null)
            {
                if (curr.ClickType == WorkerManagerClickType.Posted)
                {
                    totalAdd++;
                }
                else
                {
                    totalProcessed++;
                }

                curr = (WorkManagerClick) curr.Next;
            }

            // harvest 
            this.m_HourClicker.Click(new WorkManagerClick() {ClickType = WorkerManagerClickType.Posted, Value = totalAdd});
            this.m_HourClicker.Click(new WorkManagerClick() {ClickType = WorkerManagerClickType.Processed, Value = totalProcessed});
        }

        #endregion

        #region Specs

        public uint YieldQueueAfter
        {
            get { return this.m_Yield_Queue_After; }
            set
            {
                if (0 == value)
                {
                    return;
                }

                this.m_Yield_Queue_After = value;
            }
        }

        public uint MaxNumOfWorkers
        {
            get { return this.m_MaxNumOfWorkers; }
            set
            {
                if (0 == value)
                {
                    return;
                }

                if (value > s_Max_Num_OfWorker)
                {
                    this.m_MaxNumOfWorkers = s_Max_Num_OfWorker;
                }

                this.m_MaxNumOfWorkers = value;
            }
        }

        public uint MaxNumOfBufferedWorkItems
        {
            get { return this.m_MaxNumOfBufferedWorkItems; }
            set
            {
                if (0 == value)
                {
                    return;
                }

                if (value > s_MaxNumOfBufferedWorkItems)
                {
                    this.m_MaxNumOfBufferedWorkItems = s_MaxNumOfBufferedWorkItems;
                }

                this.m_MaxNumOfBufferedWorkItems = value;
            }
        }


        public IReliableStateManager StateManager { get; } = null;

        public WorkItemHandlerMode WorkItemHandlerMode
        {
            get { return this.m_WorkItemHandlerMode; }
            set
            {
                if (this.WorkManagerStatus == WorkManagerStatus.Working)
                {
                    throw new InvalidOperationException("can not change work item handler mode while working is running");
                }

                if (value != this.m_WorkItemHandlerMode)
                {
                    this.m_handlers.Clear();
                }

                this.m_TraceWriter.TraceMessage(
                    string.Format("Work manager is changing work item handling mode from {0} to {1}", this.m_WorkItemHandlerMode.ToString(), value.ToString()));
                this.m_WorkItemHandlerMode = value;
            }
        }

        public WorkManagerStatus WorkManagerStatus { get; private set; } = WorkManagerStatus.New;

        public TimeSpan RemoveEmptyQueueAfter
        {
            get { return TimeSpan.FromTicks(this.m_RemoveEmptyQueueAfterTicks); }
            set { this.m_RemoveEmptyQueueAfterTicks = value.Ticks; }
        }

        #endregion

        #region runtime Telemetry

        public int NumberOfActiveQueues
        {
            get { return this.m_QueueManager.QueueNames.Count(); }
        }

        public int TotalPostedLastMinute
        {
            get
            {
                return this.m_MinuteClicker.Do(
                    head =>
                    {
                        int count = 0;
                        WorkManagerClick curr = head;
                        while (curr != null)
                        {
                            if (curr.ClickType == WorkerManagerClickType.Posted)
                            {
                                count++;
                            }

                            curr = (WorkManagerClick) curr.Next;
                        }
                        return count;
                    });
            }
        }

        public int TotalProcessedLastMinute
        {
            get
            {
                return this.m_MinuteClicker.Do(
                    head =>
                    {
                        int count = 0;
                        WorkManagerClick curr = head;
                        while (curr != null)
                        {
                            if (curr.ClickType == WorkerManagerClickType.Processed)
                            {
                                count++;
                            }

                            curr = (WorkManagerClick) curr.Next;
                        }
                        return count;
                    });
            }
        }

        public int TotalPostedLastHour
        {
            get
            {
                return this.m_HourClicker.Do(
                    head =>
                    {
                        int count = 0;
                        WorkManagerClick curr = head;
                        while (curr != null)
                        {
                            if (curr.ClickType == WorkerManagerClickType.Posted)
                            {
                                count += curr.Value;
                            }

                            curr = (WorkManagerClick) curr.Next;
                        }
                        return count;
                    });
            }
        }

        public int TotalProcessedLastHour
        {
            get
            {
                return this.m_HourClicker.Do(
                    head =>
                    {
                        int count = 0;
                        WorkManagerClick curr = head;
                        while (curr != null)
                        {
                            if (curr.ClickType == WorkerManagerClickType.Processed)
                            {
                                count += curr.Value;
                            }

                            curr = (WorkManagerClick) curr.Next;
                        }
                        return count;
                    });
            }
        }

        public float AveragePostedPerMinLastHour
        {
            get
            {
                return this.m_HourClicker.Do(
                    head =>
                    {
                        int sum = 0;
                        int count = 0;
                        WorkManagerClick curr = head;
                        while (curr != null)
                        {
                            if (curr.ClickType == WorkerManagerClickType.Posted)
                            {
                                sum += (int) curr.Value;
                                count++;
                            }
                            curr = (WorkManagerClick) curr.Next;
                        }
                        return count == 0 ? 0 : sum/count;
                    });
            }
        }

        public float AverageProcessedPerMinLastHour
        {
            get
            {
                return this.m_HourClicker.Do(
                    head =>
                    {
                        int sum = 0;
                        int count = 0;
                        WorkManagerClick curr = head;
                        while (curr != null)
                        {
                            if (curr.ClickType == WorkerManagerClickType.Processed)
                            {
                                sum += (int) curr.Value;
                                count++;
                            }
                            curr = (WorkManagerClick) curr.Next;
                        }
                        return count == 0 ? 0 : sum/count;
                    });
            }
        }


        public long NumberOfBufferedWorkItems
        {
            get { return this.m_NumOfBufferedWorkItems; }
        }

        #endregion

        #region Control 

        public async Task StartAsync()
        {
            if (this.WorkManagerStatus != WorkManagerStatus.New)
            {
                throw new InvalidOperationException("can not start a non-new work manager");
            }

            this.m_QueueManager = await QueueManager<Handler, Wi>.CreateAsync(this);

            await this.LoadNumOfBufferedItems();

            // create initial executers one per q (keeping in max value). 
            long nTargetExecuters = Math.Min(this.m_QueueManager.Count, this.m_MaxNumOfWorkers);
            for (int i = 1; i <= nTargetExecuters; i++)
            {
                this.m_DeferedTaskExec.AddWork(this.TryIncreaseExecuters);
            }


            this.WorkManagerStatus = WorkManagerStatus.Working;
        }

        public Task PauseAsync()
        {
            return Task.Run(
                () =>
                {
                    this.WorkManagerStatus = WorkManagerStatus.Paused;
                    foreach (WorkExecuter<Handler, Wi> e in this.m_Executers.Values)
                    {
                        e.Pause();
                    }
                });
        }

        public Task ResumeAsync()
        {
            return Task.Run(
                () =>
                {
                    this.WorkManagerStatus = WorkManagerStatus.Working;
                    foreach (WorkExecuter<Handler, Wi> e in this.m_Executers.Values)
                    {
                        e.Resume();
                    }
                });
        }

        public Task DrainAndStopAsync()
        {
            return Task.Run(
                async () =>
                {
                    if (this.WorkManagerStatus == WorkManagerStatus.Paused)
                    {
                        await this.ResumeAsync();
                    }

                    this.WorkManagerStatus = WorkManagerStatus.Draining;


                    while (this.m_NumOfBufferedWorkItems > 0)
                    {
                        await Task.Delay(5*1000);
                    }

                    this.WorkManagerStatus = WorkManagerStatus.Stopped;
                });
        }

        public Task StopAsync()
        {
            return Task.Run(
                () =>
                {
                    this.WorkManagerStatus = WorkManagerStatus.Stopped;
                    foreach (WorkExecuter<Handler, Wi> e in this.m_Executers.Values)
                    {
                        e.Stop();
                    }

                    this.m_Executers.Clear();
                });
        }

        #endregion
    }
}