// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    using System;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
    using IoTProcessorManagement.Clients;
    using Microsoft.ServiceFabric.Data;
    using Newtonsoft.Json;

    internal class ProcessorOperationUpdateHandler : ProcessorOperationHandlerBase
    {
        public ProcessorOperationUpdateHandler(
            ProcessorManagementService svc,
            ProcessorOperation Operation) : base(svc, Operation)
        {
        }

        public override async Task RunOperation(ITransaction tx)
        {
            Processor processor = await this.GetProcessorAsync(this.processorOperation.ProcessorName, tx);
            await this.SendUpdateMessages(processor);

            processor.ProcessorStatus &= ~ProcessorStatus.PendingUpdate;
            processor.ProcessorStatus |= ProcessorStatus.Updated;
            await this.UpdateProcessorAsync(processor, tx);
        }

        public override Task<T> ExecuteOperation<T>(ITransaction tx)
        {
            // Update operation does not support return values. 
            throw new NotImplementedException();
        }

        private async Task SendUpdateMessages(Processor processor)
        {
            HttpRequestMessage requestMessage = await this.GetBasicPartitionHttpRequestMessageAsync();
            requestMessage.Method = HttpMethod.Put;
            requestMessage.Content = new StringContent(JsonConvert.SerializeObject(processor), Encoding.UTF8, "application/json");
            Task<HttpResponseMessage>[] tasks =
                await this.SendHttpAllServicePartitionAsync(processor.ServiceFabricServiceName, requestMessage, "eventhubprocessor/");
            await Task.WhenAll(tasks);

            ServiceEventSource.Current.Message(string.Format("Processor {0} with App {1} updated", processor.Name, processor.ServiceFabricServiceName));
        }
    }
}