// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Admin.WebService.Controllers
{
    using Iot.Admin.WebService.Models;
    using Iot.Admin.WebService.ViewModels;
    using Iot.Common;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Mvc;
    using Azure.Messaging.EventHubs;
    using Azure.Messaging.EventHubs.Producer;
    using System;
    using System.Collections.Specialized;
    using System.Fabric;
    using System.Fabric.Description;
    using System.Fabric.Query;
    using System.Linq;
    using System.Threading.Tasks;

    [Route("api/[Controller]")]
    public class IngestionController : Controller
    {
        private readonly TimeSpan operationTimeout = TimeSpan.FromSeconds(20);
        private readonly FabricClient fabricClient;
        private readonly IApplicationLifetime appLifetime;

        public IngestionController(FabricClient fabricClient, IApplicationLifetime appLifetime)
        {
            this.fabricClient = fabricClient;
            this.appLifetime = appLifetime;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            ApplicationList applications = await this.fabricClient.QueryManager.GetApplicationListAsync();

            return this.Ok(
                applications
                    .Where(x => x.ApplicationTypeName == Names.IngestionApplicationTypeName)
                    .Select(
                        x =>
                            new ApplicationViewModel(
                                x.ApplicationName.ToString(),
                                x.ApplicationStatus.ToString(),
                                x.ApplicationTypeVersion,
                                x.ApplicationParameters)));
        }

        [HttpPost]
        [Route("{name}")]
        public async Task<IActionResult> Post([FromRoute] string name, [FromBody] IngestionApplicationParams parameters)
        {
            // Determine the number of IoT Hub partitions.
            // The ingestion service will be created with the same number of partitions.
            EventHubProducerClient producer = new EventHubProducerClient(parameters.IotHubConnectionString, "eventHubName");
            EventHubProperties eventHubProperties = await producer.GetEventHubPropertiesAsync();

            // Application parameters are passed to the Ingestion application instance.
            NameValueCollection appInstanceParameters = new NameValueCollection();
            appInstanceParameters["IotHubConnectionString"] = parameters.IotHubConnectionString;

            ApplicationDescription application = new ApplicationDescription(
                new Uri($"{Names.IngestionApplicationPrefix}/{name}"),
                Names.IngestionApplicationTypeName,
                parameters.Version,
                appInstanceParameters);

            // Create a named application instance
            await this.fabricClient.ApplicationManager.CreateApplicationAsync(application, this.operationTimeout, this.appLifetime.ApplicationStopping);

            // Next, create named instances of the services that run in the application.
            ServiceUriBuilder serviceNameUriBuilder = new ServiceUriBuilder(application.ApplicationName.ToString(), Names.IngestionRouterServiceName);

            StatefulServiceDescription service = new StatefulServiceDescription()
            {
                ApplicationName = application.ApplicationName,
                HasPersistedState = true,
                MinReplicaSetSize = 3,
                TargetReplicaSetSize = 3,
                PartitionSchemeDescription = new UniformInt64RangePartitionSchemeDescription(eventHubProperties.PartitionIds.Length, 0, eventHubProperties.PartitionIds.Length - 1),
                ServiceName = serviceNameUriBuilder.Build(),
                ServiceTypeName = Names.IngestionRouterServiceTypeName
            };

            await this.fabricClient.ServiceManager.CreateServiceAsync(service, this.operationTimeout, this.appLifetime.ApplicationStopping);

            return this.Ok();
        }

        [HttpDelete]
        [Route("{name}")]
        public async Task<IActionResult> Delete(string name)
        {
            try
            {
                await this.fabricClient.ApplicationManager.DeleteApplicationAsync(
                    new DeleteApplicationDescription(new Uri($"{Names.IngestionApplicationPrefix}/{name}")),
                    this.operationTimeout,
                    this.appLifetime.ApplicationStopping);
            }
            catch (FabricElementNotFoundException)
            {
                // service doesn't exist; nothing to delete
            }

            return this.Ok();
        }
    }
}