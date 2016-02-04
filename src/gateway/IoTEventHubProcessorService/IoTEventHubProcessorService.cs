// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

//#define _WAIT_FOR_DEBUGGER // if defined will cause the replica to wait for your debugger to attach.
//#define _VS_DEPLOY // if defined will force the replica to use statically defined processor (instead of init data) - check GetAssignedProcessorAsync() method
namespace EventHubProcessor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using IoTProcessorManagement.Clients;
    using IoTProcessorManagement.Common;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;

    public class IoTEventHubProcessorService : StatefulService
    {
        private const string DefDictionary = "defs"; // this dictionary<string, string> will be used to save assigned processor definition
        private const string AssignedProcessorEntryName = "AssginedProcessor"; // this is where we save assigned processor as json strong
        private EventHubListenerDataHandler eventHubListenerHandler = null; // every message on Event hub will be handled by this guy. 
        private Processor assignedProcessor; // each service will have an assigned Processor which is a list of event hubs to pump data out of
        private CompositeCommunicationListener compositeListener; // one composite listener to rule them all

        public IoTEventHubProcessorService()
        {
            this.ErrorMessage = String.Empty;
            this.IsInErrorState = false;
            this.TraceWriter = new TraceWriter(this);
            this.compositeListener = new CompositeCommunicationListener(this.TraceWriter);
        }

        public string ErrorMessage { get; private set; }

        /// <summary>
        /// we maintain a seprate error flag because while worker might be in
        /// working state, processing buffered items, listener might be in error state.
        /// </summary>
        public bool IsInErrorState { get; private set; }

        public TraceWriter TraceWriter { get; private set; } // used to allow components to use Event Source/ServiceMessage

        private WorkManager<RoutetoActorWorkItemHandler, RouteToActorWorkItem> WorkManager { get; set; }

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new[]
            {
                new ServiceReplicaListener(
                    parameteres => new OwinCommunicationListener(new ProcessorServiceOwinListenerSpec(this), parameteres), "webapi"), 

                new ServiceReplicaListener(
                    parameters => this.compositeListener, "eventhubs")
            };
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            this.TraceWriter.EnablePrefix = true;


#if _WAIT_FOR_DEBUGGER
            while (!Debugger.IsAttached)
                await Task.Delay(5000);
#endif

            // Create a work manager that is expected to receive a work item of type RouteToActor
            // The work manager will queue them (according to RouteToActor.Queue Name property). Then for each work 
            //item de-queued it will use RouteToActorWorkItemHandler type to process the work item
            this.WorkManager = new WorkManager<RoutetoActorWorkItemHandler, RouteToActorWorkItem>(this.StateManager, this.TraceWriter);

            /*
            Work Manager supports 2 modes
            Buffered: 
                - the original mode, items are buffered in reliable queues (one per device) then routed to actors.
                - the advantages of this mode, if you have smaller # of devices, the system will attempt to avoid the turn based concurrancy of the actor
                  by fanning out execution, events are picked up from event hub faster than they are delivered to actors. this mode is good for large
                  # of devices each is a high freq message sender. 
              
            Buffered os faster on multi core CPUs , the max # of worker is 2 X CPU ore.

            None Buffered Mode: 
                - newly introcuced mode, events are not buffered and routed directly to actors. this is a better mode if you expect large # of devices.
            
            
            - You can extend the code to support switching at runtime (example: dealing with variable device #)
            
            */
            this.WorkManager.BufferedMode = false; 


            // work manager will creates a new (RoutetoActorWorkItemHandler) 
            // per queue. our work item handling is basically forwarding event hub message to the Actor. 
            // since each Actor will have it is own queue (and a handler). each handler
            // can cache a reference to the actor proxy instead of caching them at a higher level
            this.WorkManager.WorkItemHandlerMode = WorkItemHandlerMode.PerQueue;

            // maximum # of worker loops (loops that de-queue from reliable queue)
            this.WorkManager.MaxNumOfWorkers =  WorkManager<RoutetoActorWorkItemHandler, RouteToActorWorkItem>.s_Max_Num_OfWorker;

            this.WorkManager.YieldQueueAfter = 50; // worker will attempt to process
            // 50 work item per queue before dropping it 
            // and move to the next. 

            // if a queue stays empty more than .. it will be removed
            this.WorkManager.RemoveEmptyQueueAfter = TimeSpan.FromSeconds(10);

            // start it
            await this.WorkManager.StartAsync();

            // this wire up Event hub listeners that uses EventHubListenerDataHandler to 
            // post to WorkManager which then (create or get existing queue) then en-queue.
            this.eventHubListenerHandler = new EventHubListenerDataHandler(this.WorkManager);

            // this ensures that an event hub listener is created
            // per every assigned event hub
            await this.RefreshListenersAsync();

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(2000);
            }


            this.TraceWriter.EnablePrefix = false;


            this.TraceWriter.TraceMessage("Replica is existing, stopping the work manager");

            try
            {
                await this.ClearEventHubListeners();
                await this.WorkManager.StopAsync();
            }
            catch (AggregateException aex)
            {
                AggregateException ae = aex.Flatten();
                this.TraceWriter.TraceMessage(
                    string.Format(
                        "as the replica unload (run async canceled) the followng errors occured E:{0} StackTrace:{1}",
                        aex.GetCombinedExceptionMessage(),
                        aex.GetCombinedExceptionStackTrace()));
            }
        }

        /// <summary>
        /// event hub listeners does not support pause and resume so we just remove and recreate them.
        /// </summary>
        /// <returns></returns>
        public async Task Pause()
        {
            if (this.WorkManager.WorkManagerStatus != WorkManagerStatus.Working)
            {
                throw new InvalidOperationException("can not pause service if its not in working state");
            }

            await this.ClearEventHubListeners();
            await this.WorkManager.PauseAsync();
        }

        public async Task Resume()
        {
            if (this.WorkManager.WorkManagerStatus != WorkManagerStatus.Paused)
            {
                throw new InvalidOperationException("can not resume service if its not in paused state");
            }

            await this.WorkManager.ResumeAsync();
            await this.RefreshListenersAsync();
        }

        public async Task Stop()
        {
            if (this.WorkManager.WorkManagerStatus == WorkManagerStatus.Working
                || this.WorkManager.WorkManagerStatus == WorkManagerStatus.Paused
                || this.WorkManager.WorkManagerStatus == WorkManagerStatus.Draining
                )
            {
                await this.ClearEventHubListeners();
                await this.WorkManager.StopAsync(); 
            }
            else
            {
                throw new InvalidOperationException("can not stop service if its not in working or paused state");
            }
        }

        public async Task DrainAndStop()
        {
            if (this.WorkManager.WorkManagerStatus == WorkManagerStatus.Working
                || this.WorkManager.WorkManagerStatus == WorkManagerStatus.Paused)
            {
                await this.ClearEventHubListeners();
                await this.WorkManager.DrainAndStopAsync();
            }
            else
            {
                throw new InvalidOperationException("can not drain stop service if its not in working or paused state");
            }
        }

        public Task<int> GetNumberOfActiveQueuesAsync()
        {
            return Task.FromResult(this.WorkManager.NumberOfActiveQueues);
        }

        public Task<int> GetTotalPostedLastMinuteAsync()
        {
            return Task.FromResult(this.WorkManager.TotalPostedLastMinute);
        }

        public Task<int> GetTotalProcessedLastMinuteAsync()
        {
            return Task.FromResult(this.WorkManager.TotalProcessedLastMinute);
        }

        public Task<int> GetTotalPostedLastHourAsync()
        {
            return Task.FromResult(this.WorkManager.TotalPostedLastHour);
        }

        public Task<int> GetTotalProcessedLastHourAsync()
        {
            return Task.FromResult(this.WorkManager.TotalProcessedLastHour);
        }

        public Task<float> GetAveragePostedPerMinLastHourAsync()
        {
            return Task.FromResult(this.WorkManager.AveragePostedPerMinLastHour);
        }

        public Task<float> GetAverageProcessedPerMinLastHourAsync()
        {
            return Task.FromResult(this.WorkManager.AverageProcessedPerMinLastHour);
        }

        public Task<string> GetStatusStringAsync()
        {
            // instead of maintaining a new enum for processor service status
            // we are using the work manager status since this service is not 
            // doing anything other than posting and managing work items. 
            return Task.FromResult(this.WorkManager.WorkManagerStatus.ToString());
        }

        public Task<long> GetNumOfBufferedItemsAsync()
        {
            return Task.FromResult(this.WorkManager.NumberOfBufferedWorkItems);
        }
        
        // updates the current assigned processor (the # of event hubs)
        public async Task SetAssignedProcessorAsync(Processor newProcessor)
        {
            // save the processor (replacing whatever we had)
            this.assignedProcessor = await this.SaveProcessorToState(newProcessor);

            // if we are in working mode, refresh listeners
            if (this.WorkManager.WorkManagerStatus == WorkManagerStatus.Working)
            {
                await this.RefreshListenersAsync();
            }
        }
        
        private Task ClearEventHubListeners()
        {
            // we clear the event hub as we are not sure if the # of partitions has changed
            return this.compositeListener.ClearAll();
        }

        private async Task RefreshListenersAsync()
        {
            this.IsInErrorState = false;
            this.ErrorMessage = string.Empty;

            Processor processor = await this.GetAssignedProcessorAsync();
            this.TraceWriter.TraceMessage(string.Format("Begin Refresh Listeners, creating {0} event hub listeners", processor.Hubs.Count));

            // since we don't keep track of event hub config, connections
            // partitions count etc. we will assume that they have *all* changed 
            // so we will remove all active Event Hub listeners and add the new ones. 
            await this.ClearEventHubListeners();

            // Event hub communication listner uses a dictionary<string, string> for check points. 
            // Event Hub Event listener (using Event Processor Approach) implementation uses IReliableState to store
            // checkpoint https://msdn.microsoft.com/en-us/library/microsoft.servicebus.messaging.lease.aspx
            // the leases uses consistent naming, hence even if we removed a listener and added it will pick
            // up from where it lift exactly (in terms of Event Hub Sequence #)
            IReliableDictionary<string, string> LeaseStateDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<string, string>>(DefDictionary);

            this.TraceWriter.TraceMessage(string.Format("Event Hub Leases are saved in reliable dictionary named {0}", DefDictionary));

            foreach (EventHubDefinition hub in processor.Hubs)
            {
                string ListenerName = this.HubDefToListenerName(hub);
                bool BadListener = false;
                string sErrorMessage = string.Empty;

                try
                {
                    EventHubCommunicationListener eventHubListener = new EventHubCommunicationListener(
                        this.TraceWriter,
                        this.StateManager,
                        // state manager used by eh listener for check pointing
                        LeaseStateDictionary,
                        this.ServiceInitializationParameters,
                        // which dictionary will it use to save state it uses IReliableDictionary<string, string>
                        hub.EventHubName,
                        // which event hub will it pump messages out of
                        hub.ConnectionString,
                        // Service Bus connection string
                        hub.ConsumerGroupName,
                        // eh consumer group ("" => will use default consumer group).
                        this.eventHubListenerHandler,
                        // object that implements (IEventDataHandler) to be called by the listener when messages are recieved.
                        EventHubCommunicationListenerMode.Distribute,
                        // refer to EventHubCommunicationListenerMode 
                        string.Empty // no particular event hub partition is assigned to this replica, it will be auto assigned
                        );

                    await this.compositeListener.AddListenerAsync(ListenerName, eventHubListener);
                }

                catch (AggregateException aex)
                {
                    BadListener = true;

                    AggregateException ae = aex.Flatten();
                    sErrorMessage =
                        string.Format(
                            "Event Hub Listener for Connection String:{0} Hub:{1} CG:{2} generated an error, other listeners will keep on running and replica will enter error state. E:{3} StackTrace:{4}",
                            hub.ConnectionString,
                            hub.EventHubName,
                            hub.ConsumerGroupName,
                            ae.GetCombinedExceptionMessage(),
                            ae.GetCombinedExceptionStackTrace());
                }
                catch (Exception e)
                {
                    BadListener = true;
                    sErrorMessage =
                        string.Format(
                            "Event Hub Listener for Connection String:{0} Hub:{1} CG:{2} generated an error, other listeners will keep on running and replica will enter error state. E:{3} StackTrace:{4}",
                            hub.ConnectionString,
                            hub.EventHubName,
                            hub.ConsumerGroupName,
                            e.Message,
                            e.StackTrace);
                }
                finally
                {
                    if (BadListener)
                    {
                        try
                        {
                            await this.compositeListener.RemoveListenerAsync(ListenerName);
                        }
                        catch
                        {
                            /* no op*/
                        }
                        this.TraceWriter.TraceMessage(sErrorMessage);
                        this.IsInErrorState = true;
                        this.ErrorMessage = string.Concat(this.ErrorMessage, "\n", sErrorMessage);
                    }
                }
            }

            this.TraceWriter.TraceMessage("End Refresh Listeners");
        }

        
        private async Task<Processor> GetAssignedProcessorFromState()
        {
            Processor processor = null;
            IReliableDictionary<string, string> dict = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, string>>(DefDictionary);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ConditionalResult<string> cResult = await dict.TryGetValueAsync(tx, AssignedProcessorEntryName);
                if (cResult.HasValue)
                {
                    processor = Processor.FromJsonString(cResult.Value);
                }
                await tx.CommitAsync();
            }
            return processor;
        }

        private async Task<Processor> SaveProcessorToState(Processor processor)
        {
            IReliableDictionary<string, string> dict = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, string>>(DefDictionary);

            string sValue = processor.AsJsonString();
            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                await dict.AddOrUpdateAsync(
                    tx,
                    AssignedProcessorEntryName,
                    sValue,
                    (k, v) => { return sValue; });

                await tx.CommitAsync();
            }
            return processor;
        }

        private string HubDefToListenerName(EventHubDefinition HubDef)
        {
            return string.Concat(HubDef.EventHubName, "-", HubDef.ConnectionString, "-", HubDef.ConsumerGroupName);
        }


        public async Task<Processor> GetAssignedProcessorAsync()
        {
            // do we have it?
            if (null != this.assignedProcessor)
            {
                return this.assignedProcessor;
            }

            //is it in state
            Processor processor = await this.GetAssignedProcessorFromState();
            if (processor != null)
            {
                this.assignedProcessor = processor;
                return this.assignedProcessor;
            }

            // must be a new instance (or if we are debugging in VS.NET then use a manually created one)
#if _VS_DEPLOY
    // in this mode we load a mock up Processor and use it.
    // this mode is used only during single processor (set as a startup)
    // project 
            TraceWriter.TraceMessage("Processor is running in VS.NET Deploy Mode");

            var processor1 = new Processor()
            {
                Name = "One"
            };
            processor1.Hubs.Add(new EventHubDefinition()
            {
                ConnectionString = "// Service Bus Connection String //",
                EventHubName = "eh01",
                ConsumerGroupName = ""
            });

            
            return processor1;
#else
            if (null != this.ServiceInitializationParameters.InitializationData)
            {
                Processor initProcessor = Processor.FromBytes(this.ServiceInitializationParameters.InitializationData);
                Trace.WriteLine(
                    string.Format(
                        string.Format(
                            "Replica {0} Of Application {1} Got Processor {2}",
                            this.ServiceInitializationParameters.ReplicaId,
                            this.ServiceInitializationParameters.CodePackageActivationContext.ApplicationName,
                            initProcessor.Name)));

                this.assignedProcessor = await this.SaveProcessorToState(initProcessor); // this sets m_assignedprocessor

                return this.assignedProcessor;
            }

#endif
            throw new InvalidOperationException("Failed to load assigned processor from saved state and initialization data");
        }
        
    }
}