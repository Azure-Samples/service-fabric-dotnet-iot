using Iot.Admin.WebService.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Fabric;
using System.Fabric.Description;
using System.Fabric.Query;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Iot.Admin.WebService.Controllers
{
    [Route("api/[Controller]")]
    public class IngestionController : Controller
    {
        private const string IngestionApplicationPrefix = "fabric:/Ingestion";

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
            ApplicationList applications = await this.fabricClient.QueryManager.GetApplicationListAsync(
                    new Uri(IngestionApplicationPrefix), 
                    operationTimeout, 
                    this.cancellationTokenSource.Token);

            return this.Ok(applications.Select(x => x.ApplicationName.ToString()));
        }

        [HttpPost]
        [Route("{name}")]
        public async Task<IActionResult> Post([FromRoute]string name, [FromBody]IngestionApplicationParams parameters)
        {
            NameValueCollection appInstanceParameters = new NameValueCollection();
            appInstanceParameters["IotHubConnectionString"] = parameters.IotHubConnectionString;

            ApplicationDescription application = new ApplicationDescription(
                new Uri($"{IngestionApplicationPrefix}/{name}"),
                WebService.IngestionApplicationType,
                parameters.Version,
                appInstanceParameters);

            try
            {
                await this.fabricClient.ApplicationManager.CreateApplicationAsync(application, this.operationTimeout, this.cancellationTokenSource.Token);
            }
            catch (FabricElementAlreadyExistsException)
            {
                // application instance already exists, move and create the service
            }

            StatefulServiceDescription service = new StatefulServiceDescription()
            {
                ApplicationName = application.ApplicationName,
                HasPersistedState = true,
                MinReplicaSetSize = 3,
                TargetReplicaSetSize = 3,
                PartitionSchemeDescription = new UniformInt64RangePartitionSchemeDescription(parameters.PartitionCount, 1, parameters.PartitionCount),
                ServiceName = new Uri(application.ApplicationName, "RouterService"),
                ServiceTypeName = WebService.IngestionRouterServiceType
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
                    new Uri($"{IngestionApplicationPrefix}/{name}"),
                    this.operationTimeout,
                    this.cancellationTokenSource.Token);
            }
            catch (FabricElementNotFoundException)
            {
                // service doesn't exist

            }

            return this.Ok();
        }
    }
}
