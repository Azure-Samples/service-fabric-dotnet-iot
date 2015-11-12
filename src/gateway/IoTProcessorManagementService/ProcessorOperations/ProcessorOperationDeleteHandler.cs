// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    using System;
    using System.Threading.Tasks;
    using IoTProcessorManagement.Clients;
    using Microsoft.ServiceFabric.Data;

    internal class ProcessorOperationDeleteHandler : ProcessorOperationHandlerBase
    {
        public ProcessorOperationDeleteHandler(
            ProcessorManagementService svc,
            ProcessorOperation Opeartion) : base(svc, Opeartion)
        {
        }

        public override async Task RunOperation(ITransaction tx)
        {
            Processor processor = await this.GetProcessorAsync(this.processorOperation.ProcessorName, tx);
            await this.CleanUpServiceFabricCluster(processor);
            processor.ProcessorStatus &= ~ProcessorStatus.PendingDelete;
            processor.ProcessorStatus |= ProcessorStatus.Deleted;
            await this.UpdateProcessorAsync(processor, tx);

            ServiceEventSource.Current.Message(string.Format("Processor:{0} with App:{1} deleted", processor.Name, processor.ServiceFabricServiceName));
        }

        public override Task<T> ExecuteOperation<T>(ITransaction tx)
        {
            throw new NotImplementedException();
        }
    }
}