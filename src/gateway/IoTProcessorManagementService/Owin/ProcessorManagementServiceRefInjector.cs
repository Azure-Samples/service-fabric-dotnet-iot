// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Web.Http.Dependencies;

    internal class ProcessorManagementServiceRefInjector : IDependencyResolver, IDependencyScope
    {
        public ProcessorManagementServiceRefInjector(ProcessorManagementService svc)
        {
            this.Svc = svc;
        }

        public ProcessorManagementService Svc { get; set; }

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
            if (serviceType.GetInterfaces().Contains(typeof(ProcessorManagementServiceApiController)))
            {
                ProcessorManagementServiceApiController reliableStateCtrl = (ProcessorManagementServiceApiController) Activator.CreateInstance(serviceType);
                reliableStateCtrl.Svc = this.Svc;
                return reliableStateCtrl;
            }

            return null;
        }

        public IEnumerable<object> GetServices(Type serviceType)
        {
            return Enumerable.Empty<object>();
        }
    }
}