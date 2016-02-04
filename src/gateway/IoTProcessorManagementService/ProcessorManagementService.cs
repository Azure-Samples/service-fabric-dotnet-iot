// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    using System;
    using System.Collections.Generic;
    using System.Fabric.Description;
    using System.Threading;
    using System.Threading.Tasks;
    using IoTProcessorManagement.Clients;
    using IoTProcessorManagement.Common;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;
    using System.Fabric;
    public class ProcessorManagementService : StatefulService
    {
        public const string OperationQueueName = "Operations";
        public const string ProcessorDefinitionStateDictionaryName = "Processors";
        public const int MaxProcessorOpeartionRetry = 5;

        public IReliableDictionary<string, Processor> ProcessorStateStore { get; private set; }

        public IReliableQueue<ProcessorOperation> ProcessorOperationsQueue { get; private set; }

        public ProcessorManagementServiceConfig Config { get; private set; }

        public ProcessorOperationHandlerFactory ProcessorOperationFactory { get; private set; }

        public ProcessorServiceCommunicationClientFactory ProcessorServiceCommunicationClientFactory { get; private set; }
           

        public ProcessorManagementService()
        {
            this.ProcessorServiceCommunicationClientFactory = new ProcessorServiceCommunicationClientFactory(
                ServicePartitionResolver.GetDefault(),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(3));
        }

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {

            var settingsConfigPackage = FabricRuntime.GetActivationContext().GetConfigurationPackageObject("Config");
            var publishingAddressHostName = settingsConfigPackage.Settings.Sections["ProcessorDefaults"].Parameters["PublishingAddressHostName"].Value;

            var actualPublishingHostName = string.IsNullOrEmpty(publishingAddressHostName) ? FabricRuntime.GetNodeContext().IPAddressOrFQDN : publishingAddressHostName;
            return new[]
            {
                new ServiceReplicaListener(
                    parameters =>  new OwinCommunicationListener(new ProcessorManagementServiceOwinListenerSpec(this), parameters, actualPublishingHostName))
            };
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            this.SetProcessorAppInstanceDefaults();

            // subscribe to configuration changes
            this.ServiceInitializationParameters.CodePackageActivationContext.ConfigurationPackageModifiedEvent +=
                this.CodePackageActivationContext_ConfigurationPackageModifiedEvent;

            this.ProcessorStateStore = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, Processor>>(ProcessorDefinitionStateDictionaryName);
            this.ProcessorOperationsQueue = await this.StateManager.GetOrAddAsync<IReliableQueue<ProcessorOperation>>(OperationQueueName);

            this.ProcessorOperationFactory = new ProcessorOperationHandlerFactory();

            ProcessorOperation processorOperation = null;

            // pump and execute ProcessorPperation from the queue
            while (!cancellationToken.IsCancellationRequested)
            {
                using (ITransaction tx = this.StateManager.CreateTransaction())
                {
                    try
                    {
                        ConditionalResult<ProcessorOperation> result = await this.ProcessorOperationsQueue.TryDequeueAsync(
                            tx,
                            TimeSpan.FromMilliseconds(1000),
                            cancellationToken);

                        if (result.HasValue)
                        {
                            processorOperation = result.Value;
                            ProcessorOperationHandlerBase handler = this.ProcessorOperationFactory.CreateHandler(this, processorOperation);
                            await handler.RunOperation(tx);
                            await tx.CommitAsync();
                        }
                    }
                    catch (TimeoutException toe)
                    {
                        ServiceEventSource.Current.Message(
                            string.Format("Controller service encountered timeout in a work operations de-queue process {0} and will try again", toe.StackTrace));
                    }
                    catch (AggregateException aex)
                    {
                        AggregateException ae = aex.Flatten();

                        string sError = string.Empty;
                        if (null == processorOperation)
                        {
                            sError =
                                string.Format(
                                    "Event Processor Management Service encountered an error processing Processor-Operation {0} {1} and will terminate replica",
                                    ae.GetCombinedExceptionMessage(),
                                    ae.GetCombinedExceptionStackTrace());
                        }
                        else
                        {
                            sError =
                                string.Format(
                                    "Event Processor Management Service encountered an error processing Processor-opeartion {0} against {1} Error {2} stack trace {3} and will terminate replica",
                                    processorOperation.OperationType.ToString(),
                                    processorOperation.ProcessorName,
                                    ae.GetCombinedExceptionMessage(),
                                    ae.GetCombinedExceptionStackTrace());
                        }


                        ServiceEventSource.Current.ServiceMessage(this, sError);
                        throw;
                    }
                }
            }
        }

        #region Configuration Management 

        private void CodePackageActivationContext_ConfigurationPackageModifiedEvent(
            object sender, System.Fabric.PackageModifiedEventArgs<System.Fabric.ConfigurationPackage> e)
        {
            this.SetProcessorAppInstanceDefaults();
        }

        private void SetProcessorAppInstanceDefaults()
        {
            /// <summary>
            /// loads default processor app type default name and version
            /// from and configuration and saves them for later use 

            ConfigurationSettings settingsFile =
                this.ServiceInitializationParameters.CodePackageActivationContext.GetConfigurationPackageObject("Config").Settings;

            ConfigurationSection ProcessorServiceDefaults = settingsFile.Sections["ProcessorDefaults"];

            ProcessorManagementServiceConfig newConfig = new ProcessorManagementServiceConfig(
                ProcessorServiceDefaults.Parameters["AppTypeName"].Value,
                ProcessorServiceDefaults.Parameters["AppTypeVersion"].Value,
                ProcessorServiceDefaults.Parameters["ServiceTypeName"].Value,
                ProcessorServiceDefaults.Parameters["AppInstanceNamePrefix"].Value);

            this.Config = newConfig;
        }

        #endregion
    }
}