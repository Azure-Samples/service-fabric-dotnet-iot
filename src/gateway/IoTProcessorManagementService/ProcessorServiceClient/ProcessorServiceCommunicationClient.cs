// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    using System;
    using System.Fabric;
    using Microsoft.ServiceFabric.Services.Communication.Client;

    public class ProcessorServiceCommunicationClient : ICommunicationClient
    {
        public ProcessorServiceCommunicationClient(Uri baseAddress)
        {
            this.BaseAddress = baseAddress;
        }

        /// <summary>
        /// The service base address.
        /// </summary>
        public Uri BaseAddress { get; private set; }

        public ResolvedServiceEndpoint Endpoint { get; set; }

        public string ListenerName { get; set; }

        /// <summary>
        /// The resolved service partition which contains the resolved service endpoints.
        /// </summary>
        public ResolvedServicePartition ResolvedServicePartition { get; set; }
    }
}