// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace StorageActor
{
    using System;
    using System.Fabric.Description;
    using System.Threading.Tasks;
    using IoTActor.Common;
    using Microsoft.ServiceFabric.Actors;
    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Table;

    [ActorGarbageCollection(IdleTimeoutInSeconds = 60, ScanIntervalInSeconds = 10)]
    public class StorageActor : StatefulActor<StorageActorState>, IIoTActor
    {
        private const int MaxEntriesPerRound = 100;
        private const string PowerBIActorServiceName = "fabric:/IoTApplication/PowerBIActor";
        private const string PowerBIActorId = "{0}-{1}-{2}";
        private IActorTimer dequeueTimer = null;
        private string tableName = string.Empty;
        private string connectionString = string.Empty;
        private IIoTActor powerBIActor = null;

        public Task Post(string DeviceId, string EventHubName, string ServiceBusNS, byte[] Body)
        {
            IoTActorWorkItem workItem = new IoTActorWorkItem();
            workItem.DeviceId = DeviceId;
            workItem.EventHubName = EventHubName;
            workItem.ServiceBusNS = ServiceBusNS;
            workItem.Body = Body;

            this.State.Queue.Enqueue(workItem);
                
            return this.ForwardToPowerBIActor(DeviceId, EventHubName, ServiceBusNS, Body);
        }

        protected override Task OnActivateAsync()
        {
            if (this.State == null)
            {
                this.State = new StorageActorState();
            }


            this.SetConfig();
            this.ActorService.ServiceInitializationParameters.CodePackageActivationContext.ConfigurationPackageModifiedEvent += this.ConfigChanged;


            // register a call back timer, that perfoms the actual send to PowerBI
            // has to iterate in less than IdleTimeout 
            this.dequeueTimer = this.RegisterTimer(
                this.SaveToStorage,
                false,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(10));

            return base.OnActivateAsync();
        }

        protected override async Task OnDeactivateAsync()
        {
            this.UnregisterTimer(this.dequeueTimer); // remove the actor timer
            await this.SaveToStorage(true); // make sure that no remaining pending records 
            await base.OnDeactivateAsync();
        }
        
        private Task SaveToStorage(object IsFinal)
        {
            if (0 == this.State.Queue.Count)
            {
                return Task.FromResult(true);
            }
            
            bool bFinal = (bool) IsFinal; // as in actor instance is about to get deactivated. 
            int nCurrent = 0;
            
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(this.connectionString);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference(this.tableName);
            table.CreateIfNotExists();

            TableBatchOperation batchOperation = new TableBatchOperation();

            while ((nCurrent <= MaxEntriesPerRound || bFinal) && (0 != this.State.Queue.Count))
            {
                batchOperation.InsertOrReplace(this.State.Queue.Dequeue().ToDynamicTableEntity());
                nCurrent++;
            }

            return table.ExecuteBatchAsync(batchOperation);
        }
        
        private IIoTActor CreatePowerBIActor(string DeviceId, string EventHubName, string ServiceBusNS)
        {
            ActorId actorId = new ActorId(string.Format(PowerBIActorId, DeviceId, EventHubName, ServiceBusNS));
            return ActorProxy.Create<IIoTActor>(actorId, new Uri(PowerBIActorServiceName));
        }

        private Task ForwardToPowerBIActor(string DeviceId, string EventHubName, string ServiceBusNS, byte[] Body)
        {
            if (null == this.powerBIActor)
            {
                this.powerBIActor = this.CreatePowerBIActor(DeviceId, EventHubName, ServiceBusNS);
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
                this.ActorService.ServiceInitializationParameters.CodePackageActivationContext.GetConfigurationPackageObject("Config").Settings;
            ConfigurationSection configSection = settingsFile.Sections["Storage"];

            this.tableName = configSection.Parameters["TableName"].Value;
            this.connectionString = configSection.Parameters["ConnectionString"].Value;
        }
    }
}