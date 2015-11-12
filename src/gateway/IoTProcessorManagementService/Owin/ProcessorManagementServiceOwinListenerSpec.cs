// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    using System.Web.Http;
    using IoTProcessorManagement.Common;
    using Owin;

    /// <summary>
    /// This class helps in building an Owin pipeline specific 
    /// to the need of the controller service. 
    /// CtrlSvc need to need to
    /// 1- Map Web API 
    /// 2- Inject state management in each controller created
    /// 3- TODO: Use ADAL to authenticate requests 
    /// </summary>
    internal class ProcessorManagementServiceOwinListenerSpec : IOwinListenerSpec
    {
        private readonly ProcessorManagementService service;

        public ProcessorManagementServiceOwinListenerSpec(ProcessorManagementService service)
        {
            this.service = service;
        }

        public void CreateOwinPipeline(IAppBuilder app)
        {
            //TODO: Map ADAL
            HttpConfiguration config = new HttpConfiguration();

            // inject state manager in relevant controllers 
            config.DependencyResolver = new ProcessorManagementServiceRefInjector(this.service);
            // map API routes
            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "{controller}/{id}",
                defaults: new {id = RouteParameter.Optional}
                );

            // use the Web API
            app.UseWebApi(config);
        }
    }
}