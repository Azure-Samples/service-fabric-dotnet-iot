// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Common
{
    using System;
    using System.Fabric;

    public class ServiceUriBuilder
    {
        public ServiceUriBuilder(string serviceInstance)
        {
            this.ServiceName = serviceInstance;
        }

        public ServiceUriBuilder(string applicationInstance, string serviceName)
        {
            this.ApplicationInstance = !applicationInstance.StartsWith("fabric:/")
                ? "fabric:/" + applicationInstance
                : applicationInstance;

            this.ServiceName = serviceName;
        }

        /// <summary>
        /// The name of the application instance that contains he service.
        /// </summary>
        public string ApplicationInstance { get; set; }

        /// <summary>
        /// The name of the service instance.
        /// </summary>
        public string ServiceName { get; set; }

        public Uri Build()
        {
            string applicationInstance = this.ApplicationInstance;

            if (String.IsNullOrEmpty(applicationInstance))
            {
                try
                {
                    // the ApplicationName property here automatically prepends "fabric:/" for us
                    applicationInstance = FabricRuntime.GetActivationContext().ApplicationName;
                }
                catch (InvalidOperationException)
                {
                    // FabricRuntime is not available. 
                    // This indicates that this is being called from somewhere outside the Service Fabric cluster.
                    applicationInstance = "test";
                }
            }

            return new Uri(applicationInstance.TrimEnd('/') + "/" + this.ServiceName);
        }
    }
}