// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace EventHubProcessor
{
    // Each Web API controller will implement this interface
    // the Owin pipeline assigns a dependancy resolver to inject
    // each controller with a service reference. 
    public interface IEventHubProcessorController
    {
        IoTEventHubProcessorService ProcessorService { get; set; }
    }
}