// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Admin.WebService.Controllers
{
    using System;
    using System.Collections.Specialized;
    using System.Fabric;
    using System.Fabric.Description;
    using System.Fabric.Query;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Iot.Admin.WebService.Models;
    using Microsoft.AspNetCore.Mvc;
    using Common;
    using ViewModels;
    using Microsoft.ServiceBus.Messaging;

    [Route("api/[Controller]")]
    public class IngestionController : Controller
    {
        private readonly TimeSpan operationTimeout = TimeSpan.FromSeconds(20);
        private readonly FabricClient fabricClient;
        private readonly CancellationTokenSource cancellationTokenSource;

        public IngestionController(FabricClient fabricClient, CancellationTokenSource cancellationTokenSource)
        {
            this.fabricClient = fabricClient;
            this.cancellationTokenSource = cancellationTokenSource;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            ApplicationList applications = await this.fabricClient.QueryManager.GetApplicationListAsync();

            return this.Ok(applications
                .Where(x => x.ApplicationTypeName == Names.IngestionApplicationTypeName)
                .Select(x => new ApplicationViewModel(x.ApplicationName.ToString(), x.ApplicationStatus.ToString(), x.ApplicationTypeVersion, x.ApplicationParameters)));
        }

        [HttpPost]
        [Route("{name}")]
        public async Task<IActionResult> Post([FromRoute] string name, [FromBody] IngestionApplicationParams parameters)
        {
            // Determine the number of IoT Hub partitions.
            // The ingestion service will be created with the same number of partitions.
            EventHubClient eventHubClient = EventHubClient.CreateFromConnectionString(parameters.IotHubConnectionString, "messages/events");
            EventHubRuntimeInformation eventHubInfo = await eventHubClient.GetRuntimeInformationAsync();

            // Application parameters are passed to the Ingestion application instance.
            NameValueCollection appInstanceParameters = new NameValueCollection();
            appInstanceParameters["IotHubConnectionString"] = parameters.IotHubConnectionString;

            ApplicationDescription application = new ApplicationDescription(
                new Uri($"{Names.IngestionApplicationPrefix}/{name}"),
                Names.IngestionApplicationTypeName,
                parameters.Version,
                appInstanceParameters);

            // Create a named application instance
            await this.fabricClient.ApplicationManager.CreateApplicationAsync(application, this.operationTimeout, this.cancellationTokenSource.Token);

            // Next, create named instances of the services that run in the application.
            ServiceUriBuilder serviceNameUriBuilder = new ServiceUriBuilder(application.ApplicationName.ToString(), Names.IngestionRouterServiceName);

            StatefulServiceDescription service = new StatefulServiceDescription()
            {
                ApplicationName = application.ApplicationName,
                HasPersistedState = true,
                MinReplicaSetSize = 3,
                TargetReplicaSetSize = 3,
                PartitionSchemeDescription = new UniformInt64RangePartitionSchemeDescription(eventHubInfo.PartitionCount, 0, eventHubInfo.PartitionCount - 1),
                ServiceName = serviceNameUriBuilder.Build(),
                ServiceTypeName = Names.IngestionRouterServiceTypeName
            };

            await this.fabricClient.ServiceManager.CreateServiceAsync(service, this.operationTimeout, this.cancellationTokenSource.Token);

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
                    this.cancellationTokenSource.Token);
            }
            catch (FabricElementNotFoundException)
            {
                // service doesn't exist; nothing to delete
            }

            return this.Ok();
        }
    }
}