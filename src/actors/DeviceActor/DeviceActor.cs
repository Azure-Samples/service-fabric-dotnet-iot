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
    using Microsoft.ServiceFabric.Actors.Client;
    using Microsoft.ServiceFabric.Actors.Runtime;
    using Newtonsoft.Json.Linq;

    [StatePersistence(StatePersistence.None)]
    public class DeviceActor : Actor, IIoTActor
    {
        private const string FloorActorService = "fabric:/IoTApplication/FloorActor";
        private const string FloorActorIdFormat = "{0}-{1}-{2}";
        private const string StorageActorService = "fabric:/IoTApplication/StorageActor";
        private const string StorageActorIdFormat = "{0}-{1}-{2}";
        private IIoTActor floorActor = null;
        private IIoTActor storageActor = null;

        /// <summary>
        /// Currently device actor does not maintain state
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="eventHubName"></param>
        /// <param name="serviceBusNS"></param>
        /// <param name="body"></param>
        /// <returns></returns>
        public async Task Post(string deviceId, string eventHubName, string serviceBusNS, byte[] body)
        {
            Task TaskFloorForward = this.ForwardToFloorActorAsync(deviceId, eventHubName, serviceBusNS, body);
            Task TaskStorageForward = this.ForwardToStorageActorAsync(deviceId, eventHubName, serviceBusNS, body);

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

        private IIoTActor GetFloorActorProxy(string DeviceId, string FloorId, string EventHubName, string ServiceBusNS)
        {
            ActorId actorId = new ActorId(string.Format(FloorActorIdFormat, FloorId, EventHubName, ServiceBusNS));
            return ActorProxy.Create<IIoTActor>(actorId, new Uri(FloorActorService));
        }

        private IIoTActor GetStorageActorProxy(string DeviceId, string EventHubName, string ServiceBusNS)
        {
            ActorId actorId = new ActorId(string.Format(StorageActorIdFormat, DeviceId, EventHubName, ServiceBusNS));
            return ActorProxy.Create<IIoTActor>(actorId, new Uri(StorageActorService));
        }

        private Task ForwardToFloorActorAsync(string DeviceId, string EventHubName, string ServiceBusNS, byte[] Body)
        {
            if (null == this.floorActor)
            {
                JObject j = JObject.Parse(Encoding.UTF8.GetString(Body));
                string FloorId = j["FloorId"].Value<string>();

                this.floorActor = this.GetFloorActorProxy(DeviceId, FloorId, EventHubName, ServiceBusNS);
            }

            return this.floorActor.Post(DeviceId, EventHubName, ServiceBusNS, Body);
        }

        private Task ForwardToStorageActorAsync(string DeviceId, string EventHubName, string ServiceBusNS, byte[] Body)
        {
            if (null == this.storageActor)
            {
                this.storageActor = this.GetStorageActorProxy(DeviceId, EventHubName, ServiceBusNS);
            }

            return this.storageActor.Post(DeviceId, EventHubName, ServiceBusNS, Body);
        }
    }
}