// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    /// <summary>
    /// defines the tracing interface between the 
    /// service and an external component (which does not live in the same assembly)
    /// </summary>
    public interface ITraceWriter
    {
        void TraceMessage(string message);
    }
}