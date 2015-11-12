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
        private static int s_MaxEntriesPerRound = 100;
        private static string s_PowerBIActorServiceName = "fabric:/IoTApplication/PowerBIActor";
        private static string s_PowerBIActorId = "{0}-{1}-{2}";
        private IActorTimer m_DequeueTimer = null;
        private string m_TableName = string.Empty;
        private string m_ConnectionString = string.Empty;
        private IIoTActor m_PowerBIActor = null;

        public async Task Post(string DeviceId, string EventHubName, string ServiceBusNS, byte[] Body)
        {
            Task TaskForward = this.ForwardToPowerBIActor(DeviceId, EventHubName, ServiceBusNS, Body);

            Task taskAdd = Task.Run(
                () =>
                {
                    IoTActorWorkItem Wi = new IoTActorWorkItem();
                    Wi.DeviceId = DeviceId;
                    Wi.EventHubName = EventHubName;
                    Wi.ServiceBusNS = ServiceBusNS;
                    Wi.Body = Body;

                    this.State.Queue.Enqueue(Wi);
                }
                );

            await Task.WhenAll(TaskForward, taskAdd);
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
            this.m_DequeueTimer = this.RegisterTimer(
                this.SaveToStorage,
                false,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(10));

            return base.OnActivateAsync();
        }

        protected override async Task OnDeactivateAsync()
        {
            this.UnregisterTimer(this.m_DequeueTimer); // remove the actor timer
            await this.SaveToStorage(true); // make sure that no remaining pending records 
            await base.OnDeactivateAsync();
        }

        #region Save Logic

        private async Task SaveToStorage(object IsFinal)
        {
            if (0 == this.State.Queue.Count)
            {
                return;
            }


            bool bFinal = (bool) IsFinal; // as in actor instance is about to get deactivated. 
            int nCurrent = 0;


            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(this.m_ConnectionString);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference(this.m_TableName);
            table.CreateIfNotExists();

            TableBatchOperation batchOperation = new TableBatchOperation();

            while ((nCurrent <= s_MaxEntriesPerRound || bFinal) && (0 != this.State.Queue.Count))
            {
                batchOperation.InsertOrReplace(this.State.Queue.Dequeue().ToDynamicTableEntity());
                nCurrent++;
            }

            await table.ExecuteBatchAsync(batchOperation);
        }

        #endregion

        #region Send To PowerBI Actor

        private IIoTActor CreatePowerBIActor(string DeviceId, string EventHubName, string ServiceBusNS)
        {
            ActorId actorId = new ActorId(string.Format(s_PowerBIActorId, DeviceId, EventHubName, ServiceBusNS));
            return ActorProxy.Create<IIoTActor>(actorId, new Uri(s_PowerBIActorServiceName));
        }

        private async Task ForwardToPowerBIActor(string DeviceId, string EventHubName, string ServiceBusNS, byte[] Body)
        {
            if (null == this.m_PowerBIActor)
            {
                this.m_PowerBIActor = this.CreatePowerBIActor(DeviceId, EventHubName, ServiceBusNS);
            }

            await this.m_PowerBIActor.Post(DeviceId, EventHubName, ServiceBusNS, Body);
        }

        #endregion

        #region Config Management

        private void ConfigChanged(object sender, System.Fabric.PackageModifiedEventArgs<System.Fabric.ConfigurationPackage> e)
        {
            this.SetConfig();
        }

        private void SetConfig()
        {
            ConfigurationSettings settingsFile =
                this.ActorService.ServiceInitializationParameters.CodePackageActivationContext.GetConfigurationPackageObject("Config").Settings;
            ConfigurationSection configSection = settingsFile.Sections["Storage"];

            this.m_TableName = configSection.Parameters["TableName"].Value;
            this.m_ConnectionString = configSection.Parameters["ConnectionString"].Value;
        }

        #endregion
    }
}