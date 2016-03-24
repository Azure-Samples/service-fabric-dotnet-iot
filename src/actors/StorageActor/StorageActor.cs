// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace StorageActor
{
    using System;
    using System.Collections.Generic;
    using System.Fabric.Description;
    using System.Threading.Tasks;
    using IoTActor.Common;
    using Microsoft.ServiceFabric.Actors;
    using Microsoft.ServiceFabric.Actors.Client;
    using Microsoft.ServiceFabric.Actors.Runtime;
    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Table;

    [StatePersistence(StatePersistence.Persisted)]
    public class StorageActor : Actor, IIoTActor
    {
        private const int MaxEntriesPerRound = 100;
        private const string PowerBIActorServiceName = "fabric:/IoTApplication/PowerBIActor";
        private const string PowerBIActorId = "{0}-{1}-{2}";
        private IActorTimer dequeueTimer = null;
        private string tableName = string.Empty;
        private string connectionString = string.Empty;
        private IIoTActor powerBIActor = null;

        public async Task Post(string DeviceId, string EventHubName, string ServiceBusNS, byte[] Body)
        {
            IoTActorWorkItem workItem = new IoTActorWorkItem();
            workItem.DeviceId = DeviceId;
            workItem.EventHubName = EventHubName;
            workItem.ServiceBusNS = ServiceBusNS;
            workItem.Body = Body;

            Queue<IoTActorWorkItem> queue = await this.StateManager.GetStateAsync<Queue<IoTActorWorkItem>>("queue");

            queue.Enqueue(workItem);
                
            await this.ForwardToPowerBIActor(DeviceId, EventHubName, ServiceBusNS, Body);

            await this.StateManager.SetStateAsync("queue", queue);
        }

        protected override async Task OnActivateAsync()
        {
            await this.StateManager.TryAddStateAsync("queue", new Queue<IoTActorWorkItem>());
            
            this.SetConfig();
            this.ActorService.Context.CodePackageActivationContext.ConfigurationPackageModifiedEvent += this.ConfigChanged;
            
            // register a call back timer, that perfoms the actual send to PowerBI
            // has to iterate in less than IdleTimeout 
            this.dequeueTimer = this.RegisterTimer(
                this.SaveToStorage,
                false,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(10));

            await base.OnActivateAsync();
        }

        protected override async Task OnDeactivateAsync()
        {
            this.UnregisterTimer(this.dequeueTimer); // remove the actor timer
            await this.SaveToStorage(true); // make sure that no remaining pending records 
            await base.OnDeactivateAsync();
        }
        
        private async Task SaveToStorage(object IsFinal)
        {
            Queue<IoTActorWorkItem> queue = await this.StateManager.GetStateAsync<Queue<IoTActorWorkItem>>("queue");

            if (0 == queue.Count)
            {
                return;
            }
            
            bool bFinal = (bool) IsFinal; // as in actor instance is about to get deactivated. 
            int nCurrent = 0;
            
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(this.connectionString);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference(this.tableName);
            table.CreateIfNotExists();

            TableBatchOperation batchOperation = new TableBatchOperation();

            while ((nCurrent <= MaxEntriesPerRound || bFinal) && (0 != queue.Count))
            {
                batchOperation.InsertOrReplace(queue.Dequeue().ToDynamicTableEntity());
                nCurrent++;
            }

            await table.ExecuteBatchAsync(batchOperation);

            await this.StateManager.SetStateAsync("queue", queue);
        }
        
        private IIoTActor GetPowerBIActorProxy(string DeviceId, string EventHubName, string ServiceBusNS)
        {
            ActorId actorId = new ActorId(string.Format(PowerBIActorId, DeviceId, EventHubName, ServiceBusNS));
            return ActorProxy.Create<IIoTActor>(actorId, new Uri(PowerBIActorServiceName));
        }

        private Task ForwardToPowerBIActor(string DeviceId, string EventHubName, string ServiceBusNS, byte[] Body)
        {
            if (null == this.powerBIActor)
            {
                this.powerBIActor = this.GetPowerBIActorProxy(DeviceId, EventHubName, ServiceBusNS);
            }

            return this.powerBIActor.Post(DeviceId, EventHubName, ServiceBusNS, Body);
        }
        
        private void ConfigChanged(object sender, System.Fabric.PackageModifiedEventArgs<System.Fabric.ConfigurationPackage> e)
        {
            this.SetConfig();
        }

        private void SetConfig()
        {
            ConfigurationSettings settingsFile =
                this.ActorService.Context.CodePackageActivationContext.GetConfigurationPackageObject("Config").Settings;

            ConfigurationSection configSection = settingsFile.Sections["Storage"];

            this.tableName = configSection.Parameters["TableName"].Value;
            this.connectionString = configSection.Parameters["ConnectionString"].Value;
        }
    }
}