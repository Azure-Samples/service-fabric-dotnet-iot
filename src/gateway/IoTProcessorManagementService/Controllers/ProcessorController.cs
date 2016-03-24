// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Http;
    using IoTProcessorManagement.Clients;
    using IoTProcessorManagement.Common;
    using Microsoft.ServiceFabric.Data;
    using Newtonsoft.Json;

    public class ProcessorController : ApiController, ProcessorManagementServiceApiController
    {
        /// <summary>
        ///   HTTP configuration creates a dependancy resolver that ensures that service ref is set .
        /// </summary>
        public ProcessorManagementService Svc { get; set; }

        [HttpGet]
        [Route("processor/")]
        public async Task<List<Processor>> GetAll()
        {
            //dirty read
            List<Processor> processors = new List<Processor>();
            using (ITransaction tx = this.Svc.StateManager.CreateTransaction())
            {
                IAsyncEnumerable<KeyValuePair<string, Processor>> enumerable = await this.Svc.ProcessorStateStore.CreateEnumerableAsync(tx);

                await enumerable.ForeachAsync(CancellationToken.None, item => { processors.Add(item.Value); });
            }

            return processors;
        }

        [HttpGet]
        [Route("processor/{ProcessorName}/detailed")]
        public async Task<List<ProcessorRuntimeStatus>> GetDetailedStatus([FromUri] string ProcessorName)
        {
            string[] validationErrors = Processor.ValidateProcessName(ProcessorName);
            if (null != validationErrors)
            {
                Utils.ThrowHttpError(validationErrors);
            }

            Processor processor;
            using (ITransaction tx = this.Svc.StateManager.CreateTransaction())
            {
                // do we have it? 
                ConditionalValue<Processor> cResults = await this.Svc.ProcessorStateStore.TryGetValueAsync(tx, ProcessorName);
                if (!cResults.HasValue)
                {
                    Utils.ThrowHttpError(string.Format("Processor with the name {0} does not exist", ProcessorName));
                }

                processor = cResults.Value;
            }

            ProcessorOperationHandlerFactory factory = new ProcessorOperationHandlerFactory();
            ProcessorOperationHandlerBase operationHandler = factory.CreateHandler(
                this.Svc,
                new ProcessorOperation() {OperationType = ProcessorOperationType.RuntimeStatusCheck, ProcessorName = ProcessorName});

            List<ProcessorRuntimeStatus> runtimeStatus = new List<ProcessorRuntimeStatus>();
            Task<HttpResponseMessage>[] tasks = await operationHandler.ExecuteOperation<Task<HttpResponseMessage>[]>(null);

            await Task.WhenAll(tasks);

            foreach (Task<HttpResponseMessage> completedTask in tasks)
            {
                HttpResponseMessage httpResponse = completedTask.Result;
                if (!httpResponse.IsSuccessStatusCode)
                {
                    Utils.ThrowHttpError("error aggregating status from processor partitions");
                }

                runtimeStatus.Add(JsonConvert.DeserializeObject<ProcessorRuntimeStatus>(await httpResponse.Content.ReadAsStringAsync()));
            }
            return runtimeStatus;
        }

        [HttpGet]
        [Route("processor/{ProcessorName}")]
        public async Task<Processor> Get([FromUri] string ProcessorName)
        {
            string[] validationErrors = Processor.ValidateProcessName(ProcessorName);
            if (null != validationErrors)
            {
                Utils.ThrowHttpError(validationErrors);
            }


            using (ITransaction tx = this.Svc.StateManager.CreateTransaction())
            {
                // do we have it? 
                ConditionalValue<Processor> cResults = await this.Svc.ProcessorStateStore.TryGetValueAsync(tx, ProcessorName);
                if (!cResults.HasValue)
                {
                    Utils.ThrowHttpError(string.Format("Processor with the name {0} does not exist", ProcessorName));
                }

                return cResults.Value;
            }
        }

        [HttpPost]
        [Route("processor/{ProcessorName}")]
        public async Task<Processor> Add([FromUri] string ProcessorName, [FromBody] Processor processor)
        {
            processor.Name = ProcessorName;

            string[] validationErrors = processor.Validate();
            if (null != validationErrors)
            {
                Utils.ThrowHttpError(validationErrors);
            }


            processor.ProcessorStatus = ProcessorStatus.New;

            using (ITransaction tx = this.Svc.StateManager.CreateTransaction())
            {
                // do we have it? 
                ConditionalValue<Processor> cResults = await this.Svc.ProcessorStateStore.TryGetValueAsync(tx, processor.Name);
                if (cResults.HasValue)
                {
                    Utils.ThrowHttpError(
                        string.Format("Processor with the name {0} currently exists with status", processor.Name),
                        string.Format(
                            "Processor {0} is currently {1} and mapped to app {2}",
                            cResults.Value.Name,
                            cResults.Value.ProcessorStatus.ToString(),
                            cResults.Value.ServiceFabricAppInstanceName));
                }

                // save it 
                await this.Svc.ProcessorStateStore.AddAsync(tx, processor.Name, processor);
                // create it it
                await
                    this.Svc.ProcessorOperationsQueue.EnqueueAsync(
                        tx,
                        new ProcessorOperation() {OperationType = ProcessorOperationType.Add, ProcessorName = processor.Name});


                await tx.CommitAsync();

                ServiceEventSource.Current.Message(string.Format("Queued create for processor {0} ", processor.Name));
            }


            return processor;
        }

        [HttpDelete]
        [Route("processor/{ProcessorName}")]
        public async Task<Processor> Delete([FromUri] string ProcessorName)
        {
            string[] validationErrors = Processor.ValidateProcessName(ProcessorName);
            if (null != validationErrors)
            {
                Utils.ThrowHttpError(validationErrors);
            }


            Processor existing;
            using (ITransaction tx = this.Svc.StateManager.CreateTransaction())
            {
                // do we have it? 
                ConditionalValue<Processor> cResults = await this.Svc.ProcessorStateStore.TryGetValueAsync(tx, ProcessorName);
                if (!cResults.HasValue)
                {
                    Utils.ThrowHttpError(string.Format("Processor with the name {0} does not exists", ProcessorName));
                }

                existing = cResults.Value;


                if (existing.IsOkToDelete())
                {
                    Utils.ThrowHttpError(
                        string.Format("Processor with the name {0} not valid for this operation", ProcessorName, existing.ProcessorStatusString));
                }

                existing.ProcessorStatus |= ProcessorStatus.PendingDelete;
                existing = await this.Svc.ProcessorStateStore.AddOrUpdateAsync(
                    tx,
                    existing.Name,
                    existing,
                    (name, proc) =>
                    {
                        proc.SafeUpdate(existing);
                        return proc;
                    });
                // delete it
                await
                    this.Svc.ProcessorOperationsQueue.EnqueueAsync(
                        tx,
                        new ProcessorOperation() {OperationType = ProcessorOperationType.Delete, ProcessorName = ProcessorName});
                await tx.CommitAsync();
            }
            return existing;
        }

        #region Per Processor  Actions

        [HttpPut]
        [Route("processor/{ProcessorName}")]
        public async Task<Processor> Update([FromUri] string ProcessorName, [FromBody] Processor processor)
        {
            processor.Name = ProcessorName;

            string[] validationErrors = processor.Validate();
            if (null != validationErrors)
            {
                Utils.ThrowHttpError(validationErrors);
            }


            Processor existing;
            using (ITransaction tx = this.Svc.StateManager.CreateTransaction())
            {
                // do we have it? 
                ConditionalValue<Processor> cResults = await this.Svc.ProcessorStateStore.TryGetValueAsync(tx, ProcessorName);
                if (!cResults.HasValue)
                {
                    Utils.ThrowHttpError(string.Format("Processor with the name {0} does not exists", ProcessorName));
                }

                existing = cResults.Value;


                if (existing.IsOkToQueueOperation())
                {
                    Utils.ThrowHttpError(
                        string.Format("Processor with the name {0} not valid for this operation", ProcessorName, existing.ProcessorStatusString));
                }

                existing.Hubs = processor.Hubs;
                existing.ProcessorStatus |= ProcessorStatus.PendingUpdate;
                existing = await this.Svc.ProcessorStateStore.AddOrUpdateAsync(
                    tx,
                    existing.Name,
                    existing,
                    (name, proc) =>
                    {
                        proc.SafeUpdate(existing, false, true);
                        return proc;
                    });

                await
                    this.Svc.ProcessorOperationsQueue.EnqueueAsync(
                        tx,
                        new ProcessorOperation() {OperationType = ProcessorOperationType.Update, ProcessorName = ProcessorName});
                await tx.CommitAsync();
                ServiceEventSource.Current.Message(string.Format("Queued pause command for Processor {0} ", existing.Name));
            }

            return existing;
        }


        [HttpPost]
        [Route("processor/{ProcessorName}/pause")]
        public async Task<Processor> Pause([FromUri] string ProcessorName)
        {
            string[] validationErrors = Processor.ValidateProcessName(ProcessorName);
            if (null != validationErrors)
            {
                Utils.ThrowHttpError(validationErrors);
            }


            Processor existing;
            using (ITransaction tx = this.Svc.StateManager.CreateTransaction())
            {
                // do we have it? 
                ConditionalValue<Processor> cResults = await this.Svc.ProcessorStateStore.TryGetValueAsync(tx, ProcessorName);
                if (!cResults.HasValue)
                {
                    Utils.ThrowHttpError(string.Format("Processor with the name {0} does not exists", ProcessorName));
                }

                existing = cResults.Value;


                if (existing.IsOkToQueueOperation())
                {
                    Utils.ThrowHttpError(
                        string.Format("Processor with the name {0} not valid for this operation", ProcessorName, existing.ProcessorStatusString));
                }

                existing.ProcessorStatus |= ProcessorStatus.PendingPause;
                existing = await this.Svc.ProcessorStateStore.AddOrUpdateAsync(
                    tx,
                    existing.Name,
                    existing,
                    (name, proc) =>
                    {
                        proc.SafeUpdate(existing);
                        return proc;
                    });

                await
                    this.Svc.ProcessorOperationsQueue.EnqueueAsync(
                        tx,
                        new ProcessorOperation() {OperationType = ProcessorOperationType.Pause, ProcessorName = ProcessorName});
                await tx.CommitAsync();
                ServiceEventSource.Current.Message(string.Format("Queued pause command for Processor {0} ", existing.Name));
            }

            return existing;
        }

        [HttpPost]
        [Route("processor/{ProcessorName}/stop")]
        public async Task<Processor> Stop([FromUri] string ProcessorName)
        {
            string[] validationErrors = Processor.ValidateProcessName(ProcessorName);
            if (null != validationErrors)
            {
                Utils.ThrowHttpError(validationErrors);
            }


            Processor existing;
            using (ITransaction tx = this.Svc.StateManager.CreateTransaction())
            {
                // do we have it? 
                ConditionalValue<Processor> cResults = await this.Svc.ProcessorStateStore.TryGetValueAsync(tx, ProcessorName);
                if (!cResults.HasValue)
                {
                    Utils.ThrowHttpError(string.Format("processor with the name {0} does not exists", ProcessorName));
                }

                existing = cResults.Value;


                if (existing.IsOkToQueueOperation())
                {
                    Utils.ThrowHttpError(
                        string.Format("Processor with the name {0} not valid for this operation", ProcessorName, existing.ProcessorStatusString));
                }

                existing.ProcessorStatus |= ProcessorStatus.PendingStop;

                existing = await this.Svc.ProcessorStateStore.AddOrUpdateAsync(
                    tx,
                    existing.Name,
                    existing,
                    (name, proc) =>
                    {
                        proc.SafeUpdate(existing);
                        return proc;
                    });


                await
                    this.Svc.ProcessorOperationsQueue.EnqueueAsync(
                        tx,
                        new ProcessorOperation() {OperationType = ProcessorOperationType.Stop, ProcessorName = ProcessorName});
                await tx.CommitAsync();


                ServiceEventSource.Current.Message(string.Format("Queued stop command for processor {0} ", existing.Name));
            }

            return existing;
        }

        [HttpPost]
        [Route("processor/{ProcessorName}/resume")]
        public async Task<Processor> Resume([FromUri] string ProcessorName)
        {
            string[] validationErrors = IoTProcessorManagement.Clients.Processor.ValidateProcessName(ProcessorName);
            if (null != validationErrors)
            {
                Utils.ThrowHttpError(validationErrors);
            }


            Processor existing;
            using (ITransaction tx = this.Svc.StateManager.CreateTransaction())
            {
                // do we have it? 
                ConditionalValue<Processor> cResults = await this.Svc.ProcessorStateStore.TryGetValueAsync(tx, ProcessorName);
                if (!cResults.HasValue)
                {
                    Utils.ThrowHttpError(string.Format("Processor with the name {0} does not exists", ProcessorName));
                }

                existing = cResults.Value;


                if (existing.IsOkToQueueOperation())
                {
                    Utils.ThrowHttpError(
                        string.Format("Processor with the name {0} not valid for this operation", ProcessorName, existing.ProcessorStatusString));
                }

                existing.ProcessorStatus |= ProcessorStatus.PendingResume;
                existing = await this.Svc.ProcessorStateStore.AddOrUpdateAsync(
                    tx,
                    existing.Name,
                    existing,
                    (name, proc) =>
                    {
                        proc.SafeUpdate(existing);
                        return proc;
                    });


                await
                    this.Svc.ProcessorOperationsQueue.EnqueueAsync(
                        tx,
                        new ProcessorOperation() {OperationType = ProcessorOperationType.Resume, ProcessorName = ProcessorName});
                await tx.CommitAsync();

                ServiceEventSource.Current.Message(string.Format("Queued resume command for processor {0} ", existing.Name));
            }

            return existing;
        }


        [HttpPost]
        [Route("processor/{ProcessorName}/drainstop")]
        public async Task<Processor> DrainStop([FromUri] string ProcessorName)
        {
            string[] validationErrors = Processor.ValidateProcessName(ProcessorName);
            if (null != validationErrors)
            {
                Utils.ThrowHttpError(validationErrors);
            }


            Processor existing;
            using (ITransaction tx = this.Svc.StateManager.CreateTransaction())
            {
                // do we have it? 
                ConditionalValue<Processor> cResults = await this.Svc.ProcessorStateStore.TryGetValueAsync(tx, ProcessorName);
                if (!cResults.HasValue)
                {
                    Utils.ThrowHttpError(string.Format("Processor with the name {0} does not exists", ProcessorName));
                }

                existing = cResults.Value;


                if (existing.IsOkToQueueOperation())
                {
                    Utils.ThrowHttpError(
                        string.Format("Processor with the name {0} not valid for this operation", ProcessorName, existing.ProcessorStatusString));
                }

                existing.ProcessorStatus |= ProcessorStatus.PendingDrainStop;
                existing = await this.Svc.ProcessorStateStore.AddOrUpdateAsync(
                    tx,
                    existing.Name,
                    existing,
                    (name, proc) =>
                    {
                        proc.SafeUpdate(existing);
                        return proc;
                    });
                await
                    this.Svc.ProcessorOperationsQueue.EnqueueAsync(
                        tx,
                        new ProcessorOperation() {OperationType = ProcessorOperationType.DrainStop, ProcessorName = ProcessorName});
                await tx.CommitAsync();

                ServiceEventSource.Current.Message(string.Format("Queued drain/stop command for processor {0} ", existing.Name));
            }


            return existing;
        }

        #endregion
    }
}