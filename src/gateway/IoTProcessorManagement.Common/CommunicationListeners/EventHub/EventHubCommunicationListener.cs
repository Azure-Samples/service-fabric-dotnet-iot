// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Fabric.Query;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus.Messaging;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;

    public class EventHubCommunicationListener : ICommunicationListener
    {
        public readonly string EventHubName;
        public readonly string EventHubConnectionString;
        public readonly IEventDataHandler Handler;
        public readonly string EventHubConsumerGroupName;
        public readonly IReliableDictionary<string, string> StateDictionary;
        public readonly IReliableStateManager StateManager;
        public readonly string EventHubPartitionId;
        private MessagingFactory m_MessagingFactory;
        private EventHubClient m_EventHubClient;
        private EventHubConsumerGroup m_ConsumerGroup;
        private EventProcessorFactory m_EventProcessorFactory;
        private string m_Namespace;
        private ServiceInitializationParameters m_InitParams;
        private ITraceWriter m_TraceWriter;

        public EventHubCommunicationListenerMode ListenerMode { get; }

        public EventHubCommunicationListener(
          ITraceWriter TraceWriter,
          IReliableStateManager stateManager,
          IReliableDictionary<string, string> stateDictionary,
          ServiceInitializationParameters serviceParameters,
          string eventHubName,
          string eventHubConnectionString,
          string eventHubConsumerGroupName,
          IEventDataHandler handler,
          EventHubCommunicationListenerMode Mode,
          string eventHubPartitionId)
        {
            this.ListenerMode = Mode;
            if (this.ListenerMode == EventHubCommunicationListenerMode.Single && string.IsNullOrEmpty(eventHubPartitionId))
            {
                throw new InvalidOperationException("Event hub communication listener in single mode requires a partition id");
            }


            this.m_TraceWriter = TraceWriter;

            this.m_InitParams = serviceParameters;
            this.EventHubName = eventHubName;
            this.EventHubConnectionString = eventHubConnectionString;
            this.Handler = handler;
            this.EventHubConsumerGroupName = eventHubConsumerGroupName;
            this.StateManager = stateManager;
            this.StateDictionary = stateDictionary;
            this.ListenerMode = Mode;


            this.m_TraceWriter.TraceMessage(
                string.Format(
                    "Event Hub Listener created for {0} on {1} group:{2} mode:{3}",
                    this.EventHubName,
                    this.Namespace,
                    this.EventHubConsumerGroupName,
                    this.ListenerMode.ToString()));
        }


        public EventHubCommunicationListener(
            ITraceWriter TraceWriter,
            IReliableStateManager stateManager,
            IReliableDictionary<string, string> stateDictionary,
            ServiceInitializationParameters serviceParameters,
            string eventHubName,
            string eventHubConnectionString,
            string eventHubConsumerGroupName,
            IEventDataHandler handler) : this(TraceWriter,
                stateManager,
                stateDictionary,
                serviceParameters,
                eventHubName,
                eventHubConnectionString,
                eventHubConsumerGroupName,
                handler,
                EventHubCommunicationListenerMode.SafeDistribute,
                string.Empty)
        {
        }

        public EventHubCommunicationListener()
        {
        }
        
        private string Namespace
        {
            get
            {
                if (string.IsNullOrEmpty(this.m_Namespace))
                {
                    string[] elements = this.EventHubConnectionString.Split(';');

                    foreach (string elem in elements)
                    {
                        if (elem.ToLowerInvariant().StartsWith("endpoint="))
                        {
                            this.m_Namespace = new Uri(elem.Split('=')[1]).Host;
                        }
                    }
                }
                return this.m_Namespace;
            }
        }

        public void Abort()
        {
            if (null != this.m_MessagingFactory && !this.m_MessagingFactory.IsClosed)
            {
                this.m_MessagingFactory.Close();
            }
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            if (null != this.m_MessagingFactory && !this.m_MessagingFactory.IsClosed)
            {
                await this.m_MessagingFactory.CloseAsync();
            }

            this.m_TraceWriter.TraceMessage(string.Format("Event Hub Listener for {0} on {1} closed", this.EventHubName, this.Namespace));
        }

        public async Task<string> OpenAsync(CancellationToken cancellationToken)
        {
            this.m_MessagingFactory = MessagingFactory.CreateFromConnectionString(this.EventHubConnectionString);
            this.m_EventHubClient = this.m_MessagingFactory.CreateEventHubClient(this.EventHubName);
            this.m_ConsumerGroup = !string.IsNullOrEmpty(this.EventHubConsumerGroupName)
                ? this.m_EventHubClient.GetConsumerGroup(this.EventHubConsumerGroupName)
                : this.m_EventHubClient.GetDefaultConsumerGroup();


            // slice the pie according to distribution
            // this partition can get one or more assigned Event Hub Partition ids
            string[] EventHubPartitionIds = this.m_EventHubClient.GetRuntimeInformation().PartitionIds;
            string[] assignedPartitionsIds = await this.ResolveEventHubPartitions(EventHubPartitionIds);

            this.m_EventProcessorFactory = new EventProcessorFactory(this.Handler, this.EventHubName, this.Namespace, this.EventHubConsumerGroupName);
            CheckPointManager checkPointManager = new CheckPointManager();


            this.m_TraceWriter.TraceMessage(
                string.Format(
                    "Event Hub Listener for {0} on {1} using mode:{2} handling:{3}/{4} event hub partitions",
                    this.EventHubName,
                    this.Namespace,
                    this.ListenerMode,
                    assignedPartitionsIds.Count(),
                    EventHubPartitionIds.Count()));


            foreach (string pid in assignedPartitionsIds)
            {
                StateManagerLease lease =
                    await
                        StateManagerLease.GetOrCreateAsync(
                            this.StateManager,
                            this.StateDictionary,
                            this.m_Namespace,
                            this.EventHubConsumerGroupName,
                            this.EventHubName,
                            pid);


                await this.m_ConsumerGroup.RegisterProcessorFactoryAsync(
                    lease,
                    checkPointManager,
                    this.m_EventProcessorFactory);
            }


            return string.Concat(this.EventHubName, " @ ", this.Namespace);
        }
        
        private async Task<string[]> getOrderedServicePartitionIds()
        {
            FabricClient fabricClient = new FabricClient();
            ServicePartitionList PartitionList = await fabricClient.QueryManager.GetPartitionListAsync(this.m_InitParams.ServiceName);

            List<string> partitions = new List<string>();

            foreach (Partition p in PartitionList)
            {
                partitions.Add(p.PartitionInformation.Id.ToString());
            }


            return partitions.OrderBy(s => s).ToArray();
        }

        private string[] DisributeOverServicePartitions(string[] orderEventHubPartition, string[] orderServicePartitionIds)
        {
            // service partitions are greater or equal
            // in this case each service partition gets an event hub partitions
            // the reminder partitions will just not gonna work on anything. 
            if (orderServicePartitionIds.Length >= orderEventHubPartition.Length)
            {
                int servicePartitionRank = Array.IndexOf(orderServicePartitionIds, this.m_InitParams.PartitionId.ToString());

                return new string[] {orderEventHubPartition[servicePartitionRank]};
            }
            else
            {
                // service partitions are less than event hub partitins, distribute.. 
                // service partitions can be odd or even. 

                int reminder = orderEventHubPartition.Length%orderServicePartitionIds.Length;
                int HubPartitionsPerServicePartitions = orderEventHubPartition.Length/orderServicePartitionIds.Length;
                int servicePartitionRank = Array.IndexOf(orderServicePartitionIds, this.m_InitParams.PartitionId.ToString());

                List<string> assignedIds = new List<string>();
                for (int i = 0; i < HubPartitionsPerServicePartitions; i++)
                {
                    assignedIds.Add(orderEventHubPartition[(servicePartitionRank*HubPartitionsPerServicePartitions) + i]);
                }

                // last service partition gets the reminder
                if (servicePartitionRank == (orderServicePartitionIds.Length - 1))
                {
                    for (int i = reminder; i > 0; i--)
                    {
                        assignedIds.Add(orderEventHubPartition[orderEventHubPartition.Length - i]);
                    }
                }

                return assignedIds.ToArray();
            }
        }

        private async Task<string[]> ResolveEventHubPartitions(string[] PartitionIds)
        {
            string[] OrderIds = PartitionIds.OrderBy((s) => s).ToArray();


            switch (this.ListenerMode)
            {
                case EventHubCommunicationListenerMode.Single:
                {
                    if (!OrderIds.Contains(this.EventHubPartitionId))
                    {
                        throw new InvalidOperationException(string.Format("Event hub Partition {0} is not found", this.EventHubPartitionId));
                    }

                    return new string[] {this.EventHubPartitionId};
                }
                case EventHubCommunicationListenerMode.OneToOne:
                {
                    string[] servicePartitions = await this.getOrderedServicePartitionIds();
                    if (servicePartitions.Length != OrderIds.Length)
                    {
                        throw new InvalidOperationException("Event Hub listener is in 1:1 mode yet servie partitions are not equal to event hub partitions");
                    }

                    int servicePartitionRank = Array.IndexOf(servicePartitions, this.m_InitParams.PartitionId.ToString());

                    return new string[] {OrderIds[servicePartitionRank]};
                }
                case EventHubCommunicationListenerMode.Distribute:
                {
                    string[] servicePartitions = await this.getOrderedServicePartitionIds();
                    return this.DisributeOverServicePartitions(OrderIds, servicePartitions);
                }
                case EventHubCommunicationListenerMode.SafeDistribute:
                {
                    string[] servicePartitions = await this.getOrderedServicePartitionIds();
                    // we can work with service partitions < or = Event Hub partitions 
                    // anything else is an error case

                    if (servicePartitions.Length > OrderIds.Length)
                    {
                        throw new InvalidOperationException(
                            "Event Hub listener is in fairDistribute mode yet servie partitions greater than event hub partitions");
                    }

                    return this.DisributeOverServicePartitions(OrderIds, servicePartitions);
                }
                default:
                {
                    throw new InvalidOperationException(string.Format("can not resolve event hub partition for {0}", this.ListenerMode.ToString()));
                }
            }


            throw new InvalidOperationException("could not resolve event hub partitions");
        }
        
      
    }
}