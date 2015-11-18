// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace DeviceActor
{
    using System;
    using System.Text;
    using System.Threading.Tasks;
    using IoTActor.Common;
    using Microsoft.ServiceFabric.Actors;
    using Newtonsoft.Json.Linq;

    public class DeviceActor : StatefulActor<DeviceActorState>, IIoTActor
    {
        private static string floorActorService = "fabric:/IoTApplication/FloorActor";
        private static string floorActorIdFormat = "{0}-{1}-{2}";
        private static string storageActorService = "fabric:/IoTApplication/StorageActor";
        private static string storageActorIdFormat = "{0}-{1}-{2}";
        private IIoTActor floorActor = null;
        private IIoTActor storageActor = null;

        public async Task Post(string deviceId, string eventHubName, string serviceBusNS, byte[] body)
        {
            Task TaskFloorForward = this.ForwardToNextAggregator(deviceId, eventHubName, serviceBusNS, body);
            Task TaskStorageForward = this.ForwardToStorageActor(deviceId, eventHubName, serviceBusNS, body);

            /*
            While we are waiting for the next actor in chain the device actor can do CEP to identify
            if a an action is needed on the device. if so it can aquire a channel to the device it self 
            and send the command. 
            */

            await Task.WhenAll(TaskFloorForward, TaskStorageForward);
        }

        protected override Task OnActivateAsync()
        {
            return Task.FromResult(true);
        }

        private IIoTActor CreateFloorActor(string DeviceId, string FloorId, string EventHubName, string ServiceBusNS)
        {
            ActorId actorId = new ActorId(string.Format(floorActorIdFormat, FloorId, EventHubName, ServiceBusNS));
            return ActorProxy.Create<IIoTActor>(actorId, new Uri(floorActorService));
        }

        private IIoTActor CreateStorageActor(string DeviceId, string EventHubName, string ServiceBusNS)
        {
            ActorId actorId = new ActorId(string.Format(storageActorIdFormat, DeviceId, EventHubName, ServiceBusNS));
            return ActorProxy.Create<IIoTActor>(actorId, new Uri(storageActorService));
        }

        private Task ForwardToNextAggregator(string DeviceId, string EventHubName, string ServiceBusNS, byte[] Body)
        {
            if (null == this.floorActor)
            {
                JObject j = JObject.Parse(Encoding.UTF8.GetString(Body));
                string FloorId = j["FloorId"].Value<string>();

                this.floorActor = this.CreateFloorActor(DeviceId, FloorId, EventHubName, ServiceBusNS);
            }
            return this.floorActor.Post(DeviceId, EventHubName, ServiceBusNS, Body);
        }

        private Task ForwardToStorageActor(string DeviceId, string EventHubName, string ServiceBusNS, byte[] Body)
        {
            if (null == this.storageActor)
            {
                this.storageActor = this.CreateStorageActor(DeviceId, EventHubName, ServiceBusNS);
            }

            return this.storageActor.Post(DeviceId, EventHubName, ServiceBusNS, Body);
        }
    }
}