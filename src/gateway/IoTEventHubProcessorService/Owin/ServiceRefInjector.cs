// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace EventHubProcessor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Web.Http.Dependencies;

    internal class ServiceRefInjector : IDependencyResolver, IDependencyScope
    {
        public ServiceRefInjector(IoTEventHubProcessorService svc)
        {
            this.Svc = svc;
        }

        public IoTEventHubProcessorService Svc { get; set; }

        public IDependencyScope BeginScope()
        {
            return this;
        }

        public void Dispose()
        {
            // no op
        }

        public object GetService(Type serviceType)
        {
            if (serviceType.GetInterfaces().Contains(typeof(IEventHubProcessorController)))
            {
                IEventHubProcessorController ctrl = (IEventHubProcessorController) Activator.CreateInstance(serviceType);
                ctrl.ProcessorService = this.Svc;
                return ctrl;
            }

            return null;
        }

        public IEnumerable<object> GetServices(Type serviceType)
        {
            return Enumerable.Empty<object>();
        }
    }
}