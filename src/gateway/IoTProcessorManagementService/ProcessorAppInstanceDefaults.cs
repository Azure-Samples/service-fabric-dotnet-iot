// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    public class ProcessorManagementServiceConfig
    {
        public readonly string AppTypeName;
        public readonly string AppTypeVersion;
        public readonly string ServiceTypeName;
        public readonly string AppInstanceNamePrefix;

        public ProcessorManagementServiceConfig(
            string processorAppTypeName,
            string processorAppTypeVersion,
            string processorServiceTypeName,
            string processorAppInstanceNamePrefix)
        {
            this.AppTypeName = processorAppTypeName;
            this.AppTypeVersion = processorAppTypeVersion;
            this.ServiceTypeName = processorServiceTypeName;
            this.AppInstanceNamePrefix = processorAppInstanceNamePrefix;
        }
    }
}