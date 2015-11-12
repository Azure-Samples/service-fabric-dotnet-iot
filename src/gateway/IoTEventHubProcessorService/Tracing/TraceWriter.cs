// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace EventHubProcessor
{
    using IoTProcessorManagement.Clients;
    using IoTProcessorManagement.Common;

    public class TraceWriter : ITraceWriter
    {
        public bool EnablePrefix = false;

        public TraceWriter(IoTEventHubProcessorService svc)
        {
            this.Svc = svc;
        }

        public IoTEventHubProcessorService Svc { get; }

        public void TraceMessage(string message)
        {
            string prefix = "";

            if (this.EnablePrefix)
            {
                Processor assignedProcessor = this.Svc.GetAssignedProcessorAsync().Result;

                if (null != assignedProcessor)
                {
                    prefix = string.Format("Assigned Processor Name:{0}", assignedProcessor.Name);
                }
            }
            ServiceEventSource.Current.ServiceMessage(this.Svc, string.Concat(prefix, "\n", message));
        }
    }
}