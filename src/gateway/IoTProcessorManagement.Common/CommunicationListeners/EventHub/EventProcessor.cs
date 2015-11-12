// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus.Messaging;

    /// <summary>
    /// vanila implementation of IEventProcessor (refer to Event Hub SDK).
    /// the only difference, is we presiste lease (the cursor) with ever event data. 
    /// </summary>
    internal class EventProcessor : IEventProcessor
    {
        private IEventDataHandler m_Handler;
        private string m_EventHubName;
        private string m_ServiceBusNamespace;
        private string m_CounsumerGroupName;

        public EventProcessor(IEventDataHandler Handler, string EventHubName, string ServiceBusNamespace, string ConsumerGroupName)
        {
            this.m_Handler = Handler;
            this.m_EventHubName = EventHubName;
            this.m_ServiceBusNamespace = ServiceBusNamespace;
            this.m_CounsumerGroupName = ConsumerGroupName;
        }

        public Task CloseAsync(PartitionContext context, CloseReason reason)
        {
            // no op
            return Task.FromResult(0);
        }

        public Task OpenAsync(PartitionContext context)
        {
            // no op
            return Task.FromResult(0);
        }

        public async Task ProcessEventsAsync(PartitionContext context, IEnumerable<EventData> messages)
        {
            // Todo: Improve performance by batching items 
            foreach (EventData ev in messages)
            {
                await this.m_Handler.HandleEventData(this.m_ServiceBusNamespace, this.m_EventHubName, this.m_CounsumerGroupName, ev);
                await context.CheckpointAsync();
            }
        }
    }
}