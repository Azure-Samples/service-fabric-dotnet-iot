// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    using Owin;

    /// <summary>
    ///  defines an Owin Listener specification
    /// CreateOwinPipeline method is expected to create the Owin Pipeline
    /// </summary>
    public interface IOwinListenerSpec
    {
        void CreateOwinPipeline(IAppBuilder app);
    }
}