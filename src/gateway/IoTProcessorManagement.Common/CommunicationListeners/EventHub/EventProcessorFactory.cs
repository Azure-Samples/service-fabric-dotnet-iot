// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    using Microsoft.ServiceBus.Messaging;

    /// <summary>
    /// vanila implementation of IEventProcessorFactory (refer to Event Hub Sdk).
    /// </summary>
    internal class EventProcessorFactory : IEventProcessorFactory
    {
        private IEventDataHandler m_Handler;
        private string m_EventHubName;
        private string m_ServiceBusNamespace;
        private string m_CounsumerGroupName;

        public EventProcessorFactory(IEventDataHandler Handler, string EventHubName, string ServiceBusNamespace, string ConsumerGroupName)
        {
            this.m_Handler = Handler;
            this.m_EventHubName = EventHubName;
            this.m_ServiceBusNamespace = ServiceBusNamespace;
            this.m_CounsumerGroupName = ConsumerGroupName;
        }

        public IEventProcessor CreateEventProcessor(PartitionContext context)
        {
            return new EventProcessor(this.m_Handler, this.m_EventHubName, this.m_ServiceBusNamespace, this.m_CounsumerGroupName);
        }
    }
}