// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Fabric.Description;
    using System.Fabric.Query;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using IoTProcessorManagement.Clients;
    using IoTProcessorManagement.Common;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Services.Communication.Client;

    public abstract class ProcessorOperationHandlerBase
    {
        protected ProcessorManagementService Svc;
        protected ProcessorOperation processorOperation;

        public ProcessorOperationHandlerBase(ProcessorManagementService svc, ProcessorOperation Operation)
        {
            this.Svc = svc;
            this.processorOperation = Operation;
        }

        public abstract Task RunOperation(ITransaction tx);
        public abstract Task<T> ExecuteOperation<T>(ITransaction tx) where T : class;

        protected async Task UpdateProcessorAsync(
            Processor processor, ITransaction tx = null, bool CommitInNewTransaction = false, bool OverwriteServiceFabricnames = false)
        {
            ITransaction _trx = CommitInNewTransaction ? this.Svc.StateManager.CreateTransaction() : tx;

            if (null == _trx)
            {
                throw new InvalidOperationException("Save processor need a transaction to work with if it is not commitable");
            }


            await this.Svc.ProcessorStateStore.AddOrUpdateAsync(
                _trx,
                processor.Name,
                processor,
                (name, proc) =>
                {
                    proc.SafeUpdate(processor, OverwriteServiceFabricnames);
                    return proc;
                });

            if (CommitInNewTransaction)
            {
                await _trx.CommitAsync();
                _trx.Dispose();
            }


            ServiceEventSource.Current.Message(
                string.Format(
                    "processor {0} Updated with tx:{1} NewCommit:{2} OverwriteNames:{3}",
                    processor.Name,
                    tx == null,
                    CommitInNewTransaction,
                    OverwriteServiceFabricnames));
        }

        protected async Task<Processor> GetProcessorAsync(string ProcessorName, ITransaction tx = null)
        {
            ITransaction _trx = tx ?? this.Svc.StateManager.CreateTransaction();


            Processor processor;
            ConditionalResult<Processor> cResult = await this.Svc.ProcessorStateStore.TryGetValueAsync(_trx, ProcessorName);
            if (cResult.HasValue)
            {
                processor = cResult.Value;
            }
            else
            {
                processor = null;
            }

            if (null == tx)
            {
                await _trx.CommitAsync();
                _trx.Dispose();
            }

            return processor;
        }

        protected async Task<bool> ReEnqueAsync(ITransaction tx)
        {
            this.processorOperation.RetryCount++;

            if (this.processorOperation.RetryCount > ProcessorManagementService.MaxProcessorOpeartionRetry)
            {
                return false;
            }

            await this.Svc.ProcessorOperationsQueue.EnqueueAsync(tx, this.processorOperation);
            return true;
        }

        protected Task<HttpRequestMessage> GetBasicPartitionHttpRequestMessageAsync()
        {
            // if you want to add default heads such as AuthN, add'em here. 

            return Task.FromResult(new HttpRequestMessage());
        }

        protected async Task<Task<HttpResponseMessage>[]> SendHttpAllServicePartitionAsync(string ServiceName, HttpRequestMessage Message, string requestPath)
        {
            IList<ServicePartitionClient<ProcessorServiceCommunicationClient>> partitionClients = null;
            // Get the list of representative service partition clients.
            partitionClients = await this.GetServicePartitionClientsAsync(ServiceName);

            IList<Task<HttpResponseMessage>> tasks = new List<Task<HttpResponseMessage>>(partitionClients.Count);

            foreach (ServicePartitionClient<ProcessorServiceCommunicationClient> partitionClient in partitionClients)
            {
                HttpRequestMessage message = await this.cloneHttpRequestMesageAsync(Message);


                // partitionClient internally resolves the address and retries on transient errors based on the configured retry policy.
                tasks.Add(
                    partitionClient.InvokeWithRetryAsync(
                        client =>
                        {
                            message.RequestUri = new Uri(string.Concat(client.BaseAddress, requestPath));
                            HttpClient httpclient = new HttpClient();
                            return httpclient.SendAsync(message);
                        }));
            }

            return tasks.ToArray();
        }

        private async Task<IList<ServicePartitionClient<ProcessorServiceCommunicationClient>>> GetServicePartitionClientsAsync(
            string ServiceName,
            int MaxQueryRetryCount = 5,
            int BackOffRetryDelaySec = 3)
        {
            for (int i = 0; i < MaxQueryRetryCount; i++)
            {
                try
                {
                    FabricClient fabricClient = new FabricClient();
                    Uri serviceUri = new Uri(ServiceName);

                    // Get Partition List for the target service name 
                    ServicePartitionList partitionList = await fabricClient.QueryManager.GetPartitionListAsync(serviceUri);


                    // all event processor services may have n partitions, but always using Uniform partitioning. 
                    // grab all partitions into clients list. 
                    IList<ServicePartitionClient<ProcessorServiceCommunicationClient>> partitionClients =
                        new List<ServicePartitionClient<ProcessorServiceCommunicationClient>>(partitionList.Count);
                    foreach (Partition partition in partitionList)
                    {
                        Int64RangePartitionInformation partitionInfo = partition.PartitionInformation as Int64RangePartitionInformation;
                        partitionClients.Add(
                            new ServicePartitionClient<ProcessorServiceCommunicationClient>(
                                this.Svc.ProcessorServiceCommunicationClientFactory,
                                new Uri(ServiceName),
                                partitionInfo.LowKey));
                    }

                    return partitionClients;
                }
                catch (FabricTransientException ex)
                {
                    if (i == MaxQueryRetryCount - 1)
                    {
                        ServiceEventSource.Current.ServiceMessage(
                            this.Svc,
                            "Processor Operation Handler failed to resolve service partition after:{0} retry with backoff retry:{1} E:{2} Stack Trace:{3}",
                            MaxQueryRetryCount,
                            BackOffRetryDelaySec,
                            ex.Message,
                            ex.StackTrace);
                        throw;
                    }
                }


                await Task.Delay(TimeSpan.FromSeconds(BackOffRetryDelaySec).Milliseconds);
            }

            throw new TimeoutException("Retry timeout is exhausted and creating representative partition clients wasn't successful");
        }

        private Task<HttpRequestMessage> cloneHttpRequestMesageAsync(HttpRequestMessage Source)
        {
            // poor man's http method cloner
            // ding, ding, ding! if the content was previoulsy consumed 

            HttpRequestMessage copy = new HttpRequestMessage(Source.Method, Source.RequestUri);

            copy.Version = Source.Version;

            foreach (KeyValuePair<string, object> p in Source.Properties)
            {
                copy.Properties.Add(p);
            }


            foreach (KeyValuePair<string, IEnumerable<string>> requestHeader in Source.Headers)
            {
                copy.Headers.TryAddWithoutValidation(requestHeader.Key, requestHeader.Value);
            }

            if (Source.Method != HttpMethod.Get)
            {
                copy.Content = Source.Content;
            }


            return Task.FromResult(copy);
        }

        #region Service Fabric Application & Services Management 

        protected async Task CleanUpServiceFabricCluster(Processor processor)
        {
            try
            {
                await this.DeleteServiceAsync(processor);
            }
            catch (AggregateException aex)
            {
                AggregateException ae = aex.Flatten();
                ServiceEventSource.Current.ServiceMessage(
                    this.Svc,
                    "Delete Service for processor:{0} service:{1} failed, will keep working normally E:{2} StackTrace:{3}",
                    processor.Name,
                    processor.ServiceFabricServiceName,
                    ae.GetCombinedExceptionMessage(),
                    ae.GetCombinedExceptionStackTrace());
            }


            try
            {
                await this.DeleteAppAsync(processor);
            }
            catch (AggregateException aex)
            {
                AggregateException ae = aex.Flatten();
                ServiceEventSource.Current.ServiceMessage(
                    this.Svc,
                    "Delete App for processor:{0} app:{1} failed, will keep working normally E:{2} StackTrace:{3}",
                    processor.Name,
                    processor.ServiceFabricAppInstanceName,
                    ae.GetCombinedExceptionMessage(),
                    ae.GetCombinedExceptionStackTrace());
            }
        }


        protected async Task DeleteServiceAsync(Processor processor)
        {
            Uri sServiceName = new Uri(processor.ServiceFabricServiceName);

            FabricClient fabricClient = new FabricClient();
            await fabricClient.ServiceManager.DeleteServiceAsync(sServiceName);

            ServiceEventSource.Current.ServiceMessage(
                this.Svc,
                "Service for processor:{0} service:{1} deleted.",
                processor.Name,
                processor.ServiceFabricServiceName);
        }

        protected async Task DeleteAppAsync(Processor processor)
        {
            FabricClient fabricClient = new FabricClient();
            await fabricClient.ApplicationManager.DeleteApplicationAsync(new Uri(processor.ServiceFabricAppInstanceName));
            ServiceEventSource.Current.ServiceMessage(this.Svc, "App for processor:{0} app:{1} deleted", processor.Name, processor.ServiceFabricAppInstanceName);
        }


        protected async Task CreateAppAsync(Processor processor)
        {
            FabricClient fabricClient = new FabricClient();
            ApplicationDescription appDesc = new ApplicationDescription(
                new Uri(processor.ServiceFabricAppInstanceName),
                processor.ServiceFabricAppTypeName,
                processor.ServiceFabricAppTypeVersion);


            // create the app
            await fabricClient.ApplicationManager.CreateApplicationAsync(appDesc);
            ServiceEventSource.Current.ServiceMessage(this.Svc, "App for processor:{0} app:{1} created", processor.Name, processor.ServiceFabricAppInstanceName);
        }

        protected async Task CreateServiceAsync(Processor processor)
        {
            FabricClient fabricClient = new FabricClient();
            await fabricClient.ServiceManager.CreateServiceFromTemplateAsync(
                new Uri(processor.ServiceFabricAppInstanceName),
                new Uri(processor.ServiceFabricServiceName),
                this.Svc.Config.ServiceTypeName,
                processor.AsBytes()
                );


            ServiceEventSource.Current.ServiceMessage(
                this.Svc,
                "Service for processor:{0} service:{1} created.",
                processor.Name,
                processor.ServiceFabricServiceName);
        }

        #endregion
    }
}