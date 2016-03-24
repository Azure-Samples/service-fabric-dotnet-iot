// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Communication.Client;

    public class ProcessorServiceCommunicationClientFactory : CommunicationClientFactoryBase<ProcessorServiceCommunicationClient>
    {
        private static TimeSpan MaxRetryBackoffIntervalOnNonTransientErrors = TimeSpan.FromSeconds(3);

        public ProcessorServiceCommunicationClientFactory(ServicePartitionResolver resolver = null)
            : base(resolver, new[] {new ProcessorServiceExceptionHandler()})
        {
        }

        protected override void AbortClient(ProcessorServiceCommunicationClient client)
        {
            // Http communication doesn't maintain a communication channel, so nothing to abort.
        }

        protected override bool ValidateClient(string endpoint, ProcessorServiceCommunicationClient client)
        {
            return true;
        }

        protected override bool ValidateClient(ProcessorServiceCommunicationClient clientChannel)
        {
            // Http communication doesn't maintain a communication channel, so nothing to validate.
            return true;
        }

        protected override Task<ProcessorServiceCommunicationClient> CreateClientAsync(
            string endpoint,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(endpoint) || !endpoint.StartsWith("http"))
            {
                throw new InvalidOperationException("The endpoint address is not valid. Please resolve again.");
            }

            string endpointAddress = endpoint;
            if (!endpointAddress.EndsWith("/"))
            {
                endpointAddress = endpointAddress + "/";
            }

            // Create a communication client. This doesn't establish a session with the server.
            return Task.FromResult(new ProcessorServiceCommunicationClient(new Uri(endpointAddress)));
        }
    }
}