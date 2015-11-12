// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace EventHubProcessor
{
    using System;
    using System.Threading.Tasks;
    using IoTActor.Common;
    using IoTProcessorManagement.Common;
    using Microsoft.ServiceFabric.Actors;

    public class RoutetoActorWorkItemHandler : IWorkItemHandler<RouteToActorWorkItem>
    {
        // Each handler is assigned to a queue (and queue is assigned to device). 
        private const string deviceActorServiceName = "fabric:/IoTApplication/DeviceActor";
        private IIoTActor deviceActor = null;
        private object actorLock = new object();

        public async Task<RouteToActorWorkItem> HandleWorkItem(RouteToActorWorkItem workItem)
        {
            IIoTActor DeviceActor = this.getActor(workItem);
            await DeviceActor.Post(workItem.PublisherName, workItem.EventHubName, workItem.ServiceBusNS, workItem.Body);


            return null; // if a workItem is returned, it signals the work manager to re-enqueu
        }

        private IIoTActor getActor(RouteToActorWorkItem wi)
        {
            if (this.deviceActor != null)
            {
                return this.deviceActor;
            }

            lock (this.actorLock)
            {
                if (this.deviceActor != null)
                {
                    return this.deviceActor;
                }

                ActorId id = new ActorId(wi.QueueName);
                this.deviceActor = ActorProxy.Create<IIoTActor>(id, new Uri(deviceActorServiceName));
                return this.deviceActor;
            }
        }
    }
}