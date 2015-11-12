// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    using System.Threading.Tasks;
    using Microsoft.ServiceBus.Messaging;

    /// <summary>
    /// represents the handler that will accept event data
    /// as they are pumped out from an event hub partition.
    /// </summary>
    public interface IEventDataHandler
    {
        Task HandleEventData(string ServiceBusNamespace, string EventHub, string ConsumerGroupName, EventData ed);
    }
}