// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;

    public partial class WorkManager<Handler, Wi> where Handler : IWorkItemHandler<Wi>, new()
        where Wi : IWorkItem
    {
        /// <summary>
        /// part of Work Manager that handles actual dequeue process
        /// </summary>
        /// <typeparam name="H"></typeparam>
        /// <typeparam name="W"></typeparam>
        private class WorkExecuter<H, W> where H : IWorkItemHandler<W>, new()
            where W : IWorkItem
        {
            internal readonly string workerExecuterId = Guid.NewGuid().ToString();
            private WorkManager<Handler, Wi> workManager;
            private Task workTask;
            private bool keepWorking = true;
            private bool pause = false;

            public WorkExecuter(WorkManager<Handler, Wi> workManager)
            {
                this.workManager = workManager;
            }

            public void Start()
            {
                this.workTask = Task.Run(() => this.workloop());
            }

            public void Pause()
            {
                this.pause = true;
            }

            public void Resume()
            {
                this.pause = false;
            }

            public void Stop()
            {
                this.workManager.m_TraceWriter.TraceMessage(string.Format("Worker {0} signled to stop", this.workerExecuterId));
                this.pause = false;
                this.keepWorking = false;
                this.workTask.Wait();

                this.workManager.m_TraceWriter.TraceMessage(string.Format("Worker {0} stopped", this.workerExecuterId));
            }

            private void workloop()
            {
                try
                {
                    this.workLoopAsync().Wait();
                }
                catch (AggregateException aex)
                {
                    AggregateException ae = aex.Flatten();
                    this.workManager.m_TraceWriter.TraceMessage(
                        string.Format(
                            "Executer encountered a fatel error and will exit Error:{0} StackTrace{1}",
                            ae.GetCombinedExceptionMessage(),
                            ae.GetCombinedExceptionStackTrace()));
                    throw;
                }
            }

            private void leaveQ(string qName, bool bRemoveFromEmptySuspects)
            {
                long LastCheckedVal;
                this.workManager.m_QueueManager.LeaveQueueAsync(qName);
                if (bRemoveFromEmptySuspects)
                {
                    this.workManager.m_QueueManager.m_SuspectedEmptyQueues.TryRemove(qName, out LastCheckedVal);
                }
            }

            private async Task FinilizeQueueWork(int NumOfDeqeuedMsgs, string qName, IReliableQueue<Wi> q)
            {
                long LastCheckedVal;

                if (null == q)
                {
                    // this executer tried to get q and failed. 
                    // typically this happens when # of executers > q
                    // check for decrease workers 
                    this.workManager.m_DeferedTaskExec.AddWork(this.workManager.TryDecreaseExecuters);
                    return;
                }

                bool bMoreMessages = NumOfDeqeuedMsgs > this.workManager.YieldQueueAfter;
                bool bEmptyQ = true;

                using (ITransaction tx = this.workManager.StateManager.CreateTransaction())
                {
                    bEmptyQ = !(await q.GetCountAsync(tx) > 0);
                }

                if (bMoreMessages || !bEmptyQ) // did we find messages in the queue
                {
                    this.workManager.m_TraceWriter.TraceMessage(string.Format("queue:{0} pushed back to queues, queue still have more work", qName));

                    this.leaveQ(qName, true);
                }
                else
                {
                    long now = DateTime.UtcNow.Ticks;
                    // was queue previously empty? 
                    bool bCheckedBefore = this.workManager.m_QueueManager.m_SuspectedEmptyQueues.TryGetValue(qName, out LastCheckedVal);

                    // Q was in suspected empty queue and has expired
                    if (bCheckedBefore && ((now - LastCheckedVal) > this.workManager.m_RemoveEmptyQueueAfterTicks))
                    {
                        this.workManager.m_TraceWriter.TraceMessage(string.Format("queue:{0} confirmed empty, and will be removed", qName));

                        // remove it from suspected queue 
                        this.workManager.m_QueueManager.m_SuspectedEmptyQueues.TryRemove(qName, out LastCheckedVal);

                        // remove from the queue list
                        await this.workManager.m_QueueManager.RemoveQueueAsync(qName);

                        // remove asscioated handler
                        this.workManager.m_DeferedTaskExec.AddWork(() => this.workManager.RemoveHandlerForQueue(qName));

                        // modify executers to reflect the current state
                        this.workManager.m_DeferedTaskExec.AddWork(this.workManager.TryDecreaseExecuters);
                    }
                    else
                    {
                        this.workManager.m_TraceWriter.TraceMessage(
                            string.Format("queue:{0} pushed back to queues and flagged as an empty queue suspect ", qName));
                        // the queue was not a suspect before, or has not expired 
                        this.workManager.m_QueueManager.m_SuspectedEmptyQueues.AddOrUpdate(qName, now, (k, v) => { return v; });
                        this.leaveQ(qName, false);
                    }
                }
            }

            private async Task workLoopAsync()
            {
                int nLongDequeueWaitTimeMs = 20*1000;
                int nShortDequeueWaitTimeMs = 2*1000;
                int nNoQueueWaitTimeMS = 5*1000;
                int nPauseCheckMs = 5*1000;

                while (this.keepWorking)
                {
                    // pause check
                    while (this.pause)
                    {
                        await Task.Delay(nPauseCheckMs);
                    }


                    // take the queue
                    KeyValuePair<string, IReliableQueue<Wi>> kvp = this.workManager.m_QueueManager.TakeQueueAsync();

                    if (null == kvp.Value) // no queue to work on. 
                    {
                        // this will only happen if executers # are > than queues
                        // usually a situation that should resolve it self.
                        // well by the following logic 
                        this.workManager.m_TraceWriter.TraceMessage(
                            string.Format("Executer {0} found no q and will sleep for {1}", this.workerExecuterId, nNoQueueWaitTimeMS));

                        await this.FinilizeQueueWork(0, null, null); // check removal 
                        await Task.Delay(nNoQueueWaitTimeMS); // sleep as there is no point of retrying right away.

                        continue;
                    }

                    // got Q
                    IReliableQueue<Wi> q = kvp.Value;
                    string qName = kvp.Key;
                    int nCurrentMessage = 0;

                    try
                    {
                        while (this.keepWorking & !this.pause)
                        {
                            nCurrentMessage++;

                            // processed the # of messages?
                            if (nCurrentMessage > this.workManager.YieldQueueAfter)
                            {
                                break; //-> to finally
                            }

                            // as long as we have other queues. we need to have a short wait time
                            int ActualTimeOut = this.workManager.m_QueueManager.Count > this.workManager.m_Executers.Count
                                ? nShortDequeueWaitTimeMs
                                : nLongDequeueWaitTimeMs;


                            using (ITransaction tx = this.workManager.StateManager.CreateTransaction())
                            {
                                ConditionalValue<Wi> cResults = await q.TryDequeueAsync(
                                    tx,
                                    TimeSpan.FromMilliseconds(ActualTimeOut),
                                    CancellationToken.None);
                                if (cResults.HasValue)
                                {
                                    Handler handler = this.workManager.GetHandlerForQueue(qName);
                                    Wi wi = await handler.HandleWorkItem(cResults.Value);

                                    if (null != wi) // do we have an enqueue request? (handler failed to process the request)
                                    {
                                        await q.EnqueueAsync(tx, wi);
                                    }
                                    else
                                    {
                                        this.workManager.DecreaseBufferedWorkItems();
                                    }

                                    await tx.CommitAsync();
                                }
                                else
                                {
                                    break; // -> to finally
                                }
                            }
                        }
                    }
                    catch (TimeoutException to)
                    {
                        /* Queue is locked for enqueues */
                        this.workManager.m_TraceWriter.TraceMessage(
                            string.Format("Executer after {0}: {1}", nLongDequeueWaitTimeMs, to.Message));
                        break; //-> to finally
                    }
                    catch (AggregateException aex)
                    {
                        AggregateException ae = aex.Flatten();
                        this.workManager.m_TraceWriter.TraceMessage(
                            string.Format(
                                "Executer encountered fatel error and will exit E:{0} StackTrace:{1}",
                                ae.GetCombinedExceptionMessage(),
                                ae.GetCombinedExceptionStackTrace()));

                        throw;
                    }
                    catch (Exception E)
                    {
                        this.workManager.m_TraceWriter.TraceMessage(
                            string.Format("Executer encountered fatel error and will exit E:{0} StackTrace:{1}", E.Message, E.StackTrace));

                        throw;
                    }
                    finally
                    {
                        await this.FinilizeQueueWork(nCurrentMessage, qName, q);
                    }
                }

                this.workManager.m_TraceWriter.TraceMessage(string.Format("Worker {0} exited loop", this.workerExecuterId));
            }
        }
    }
}