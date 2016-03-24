// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    using System;
    using System.Net.Http;
    using System.Threading.Tasks;
    using IoTProcessorManagement.Clients;
    using Microsoft.ServiceFabric.Data;

    internal class ProcessorOperationStatusChangeHandler : ProcessorOperationHandlerBase
    {
        public ProcessorOperationStatusChangeHandler(
            ProcessorManagementService svc,
            ProcessorOperation Operation) : base(svc, Operation)
        {
        }

        public override async Task RunOperation(ITransaction tx)
        {
            Processor processor = await this.GetProcessorAsync(this.processorOperation.ProcessorName, tx);

            if (ProcessorOperationType.Pause == this.processorOperation.OperationType)
            {
                await this.Pause(processor);
                processor.ProcessorStatus &= ~ProcessorStatus.PendingPause;
                processor.ProcessorStatus |= ProcessorStatus.Paused;
                await this.UpdateProcessorAsync(processor, tx);
            }


            if (ProcessorOperationType.Stop == this.processorOperation.OperationType)
            {
                await this.Stop(processor);
                processor.ProcessorStatus &= ~ProcessorStatus.PendingStop;
                processor.ProcessorStatus |= ProcessorStatus.Stopped;
                await this.UpdateProcessorAsync(processor, tx);
            }

            if (ProcessorOperationType.Resume == this.processorOperation.OperationType)
            {
                await this.Resume(processor);
                processor.ProcessorStatus &= ~ProcessorStatus.PendingResume;
                processor.ProcessorStatus &= ~ProcessorStatus.Paused;


                // processor.ProcessorStatus |= ProcessorStatus.Provisioned;
                await this.UpdateProcessorAsync(processor, tx);
            }


            if (ProcessorOperationType.DrainStop == this.processorOperation.OperationType)
            {
                await this.DrainStop(processor);
                processor.ProcessorStatus &= ~ProcessorStatus.PendingDrainStop;
                processor.ProcessorStatus |= ProcessorStatus.Stopped;
                await this.UpdateProcessorAsync(processor, tx);
            }

            if (ProcessorOperationType.RuntimeStatusCheck == this.processorOperation.OperationType)
            {
                throw new InvalidOperationException("Run time status check should not be called using a Task() handler");
            }
        }

        public override async Task<T> ExecuteOperation<T>(ITransaction tx)
        {
            if (this.processorOperation.OperationType != ProcessorOperationType.RuntimeStatusCheck)
            {
                throw new InvalidOperationException("Execute operation for status change handler can not handle anything except runtime status check");
            }

            Processor processor = await this.GetProcessorAsync(this.processorOperation.ProcessorName, tx);
            return await this.GetProcessorRuntimeStatusAsync(processor) as T;
        }

        private async Task Pause(Processor processor)
        {
            HttpRequestMessage requestMessage = await this.GetBasicPartitionHttpRequestMessageAsync();
            requestMessage.Method = HttpMethod.Post;
            Task<HttpResponseMessage>[] tasks =
                await this.SendHttpAllServicePartitionAsync(processor.ServiceFabricServiceName, requestMessage, "eventhubprocessor/pause");
            await Task.WhenAll(tasks);

            ServiceEventSource.Current.Message(string.Format("Processor {0} with App {1} paused", processor.Name, processor.ServiceFabricServiceName));
        }

        private async Task Stop(Processor processor)
        {
            HttpRequestMessage requestMessage = await this.GetBasicPartitionHttpRequestMessageAsync();
            requestMessage.Method = HttpMethod.Post;
            Task<HttpResponseMessage>[] tasks =
                await this.SendHttpAllServicePartitionAsync(processor.ServiceFabricServiceName, requestMessage, "eventhubprocessor/stop");
            await Task.WhenAll(tasks);

            ServiceEventSource.Current.Message(string.Format("Processor {0} with App {1} stopped", processor.Name, processor.ServiceFabricServiceName));
        }

        private async Task Resume(Processor processor)
        {
            HttpRequestMessage requestMessage = await this.GetBasicPartitionHttpRequestMessageAsync();
            requestMessage.Method = HttpMethod.Post;
            Task<HttpResponseMessage>[] tasks =
                await this.SendHttpAllServicePartitionAsync(processor.ServiceFabricServiceName, requestMessage, "eventhubprocessor/resume");
            await Task.WhenAll(tasks);

            ServiceEventSource.Current.Message(string.Format("Processor {0} with App {1} resumed", processor.Name, processor.ServiceFabricServiceName));
        }

        private async Task DrainStop(Processor processor)
        {
            HttpRequestMessage requestMessage = await this.GetBasicPartitionHttpRequestMessageAsync();
            requestMessage.Method = HttpMethod.Post;
            Task<HttpResponseMessage>[] tasks =
                await this.SendHttpAllServicePartitionAsync(processor.ServiceFabricServiceName, requestMessage, "eventhubprocessor/drainstop");

            // await all tasks if yu want to wait for drain.

            ServiceEventSource.Current.Message(
                string.Format("Processor {0} with App {1} is going into drain stop phase", processor.Name, processor.ServiceFabricServiceName));
        }

        private async Task<Task<HttpResponseMessage>[]> GetProcessorRuntimeStatusAsync(Processor processor)
        {
            HttpRequestMessage requestMessage = await this.GetBasicPartitionHttpRequestMessageAsync();
            requestMessage.Method = HttpMethod.Get;

            Task<HttpResponseMessage>[] tasks =
                await this.SendHttpAllServicePartitionAsync(processor.ServiceFabricServiceName, requestMessage, "eventhubprocessor/");
            return tasks;
        }
    }
}