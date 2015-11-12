// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace EventHubProcessor
{
    using System.Threading.Tasks;
    using IoTProcessorManagement.Common;
    using Microsoft.ServiceBus.Messaging;

    public class RouteToActorWorkItem : IWorkItem
    {
        private const string QueueNameFormat = "{0}";

        public RouteToActorWorkItem()
        {
        }

        public string PublisherName { get; set; }

        public string EventHubName { get; set; }

        public string ServiceBusNS { get; set; }

        public byte[] Body { get; set; }

        // TODO: ignore serializable
        public string QueueName
        {
            get
            {
                return string.Format(
                    QueueNameFormat,
                    this.PublisherName);
            }
        }

        public static async Task<RouteToActorWorkItem> CreateAsync(EventData ev, string publisherName, string eventHubName, string serviceBusNS)
        {
            RouteToActorWorkItem wi = new RouteToActorWorkItem()
            {
                Body = await ev.GetBodyStream().ToBytes(),
                PublisherName = publisherName,
                EventHubName = eventHubName,
                ServiceBusNS = serviceBusNS
            };

            return wi;
        }
    }
}