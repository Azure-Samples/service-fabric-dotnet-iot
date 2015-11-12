// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    public class ProcessorOperation
    {
        public ProcessorOperation()
        {
            this.RetryCount = 1;
        }

        public string ProcessorName { get; set; }

        public ProcessorOperationType OperationType { get; set; }

        public int RetryCount { get; set; }
    }

    public enum ProcessorOperationType
    {
        Add,
        Pause,
        Resume,
        Stop,
        DrainStop,
        Delete,
        Update,
        RuntimeStatusCheck
    }
}