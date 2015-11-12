// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    using System;

    public class ProcessorOperationHandlerFactory
    {
        public ProcessorOperationHandlerBase CreateHandler(
            ProcessorManagementService Svc,
            ProcessorOperation Operation)
        {
            switch (Operation.OperationType)
            {
                case ProcessorOperationType.Add:
                    return new ProcessorOperationAddHandler(Svc, Operation);

                case ProcessorOperationType.Delete:
                    return new ProcessorOperationDeleteHandler(Svc, Operation);

                case ProcessorOperationType.Pause:
                    return new ProcessorOperationStatusChangeHandler(Svc, Operation);

                case ProcessorOperationType.Resume:
                    return new ProcessorOperationStatusChangeHandler(Svc, Operation);

                case ProcessorOperationType.Stop:
                    return new ProcessorOperationStatusChangeHandler(Svc, Operation);

                case ProcessorOperationType.DrainStop:
                    return new ProcessorOperationStatusChangeHandler(Svc, Operation);

                case ProcessorOperationType.RuntimeStatusCheck:
                    return new ProcessorOperationStatusChangeHandler(Svc, Operation);

                case ProcessorOperationType.Update:
                    return new ProcessorOperationUpdateHandler(Svc, Operation);


                default:
                    throw new InvalidOperationException("Can not identify Processor Operation");
            }
        }
    }
}