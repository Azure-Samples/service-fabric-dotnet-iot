// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Tenant.WebService.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Fabric.Query;
    using System.IO;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Iot.Common;
    using Microsoft.AspNetCore.Mvc;
    using Newtonsoft.Json;

    [Route("api/[controller]")]
    public class DevicesController : Controller
    {
        private const string TenantDataServiceName = "DataService";
        private readonly FabricClient fabricClient;
        private readonly CancellationTokenSource cancellationSource;

        public DevicesController(FabricClient fabricClient, CancellationTokenSource cancellationSource)
        {
            this.fabricClient = fabricClient;
            this.cancellationSource = cancellationSource;
        }

        [HttpGet]
        [Route("queue/length")]
        public async Task<IActionResult> GetQueueLengthAsync()
        {
            ServiceUriBuilder uriBuilder = new ServiceUriBuilder(TenantDataServiceName);
            Uri serviceUri = uriBuilder.Build();

            // service may be partitioned.
            // this will aggregate the queue lengths from each partition
            ServicePartitionList partitions = await this.fabricClient.QueryManager.GetPartitionListAsync(serviceUri);

            HttpClient httpClient = new HttpClient(new HttpServiceClientHandler());

            long count = 0;
            foreach (Partition partition in partitions)
            {
                Uri getUrl = new HttpServiceUriBuilder()
                    .SetServiceName(serviceUri)
                    .SetPartitionKey(((Int64RangePartitionInformation) partition.PartitionInformation).LowKey)
                    .SetServicePathAndQuery($"/api/devices/queue/length")
                    .Build();

                HttpResponseMessage response = await httpClient.GetAsync(getUrl, this.cancellationSource.Token);

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    return this.StatusCode((int) response.StatusCode);
                }

                string result = await response.Content.ReadAsStringAsync();

                count += Int64.Parse(result);
            }

            return this.Ok(count);
        }

        [HttpGet]
        [Route("")]
        public async Task<IActionResult> GetDevicesAsync()
        {
            ServiceUriBuilder uriBuilder = new ServiceUriBuilder(TenantDataServiceName);
            Uri serviceUri = uriBuilder.Build();

            // service may be partitioned.
            // this will aggregate device IDs from all partitions
            ServicePartitionList partitions = await this.fabricClient.QueryManager.GetPartitionListAsync(serviceUri);

            HttpClient httpClient = new HttpClient(new HttpServiceClientHandler());

            List<string> deviceIds = new List<string>();
            foreach (Partition partition in partitions)
            {
                Uri getUrl = new HttpServiceUriBuilder()
                    .SetServiceName(serviceUri)
                    .SetPartitionKey(((Int64RangePartitionInformation) partition.PartitionInformation).LowKey)
                    .SetServicePathAndQuery($"/api/devices")
                    .Build();

                HttpResponseMessage response = await httpClient.GetAsync(getUrl, this.cancellationSource.Token);

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    return this.StatusCode((int) response.StatusCode);
                }

                JsonSerializer serializer = new JsonSerializer();
                using (StreamReader streamReader = new StreamReader(await response.Content.ReadAsStreamAsync()))
                {
                    using (JsonTextReader jsonReader = new JsonTextReader(streamReader))
                    {
                        List<string> result = serializer.Deserialize<List<string>>(jsonReader);

                        if (result != null)
                        {
                            deviceIds.AddRange(result);
                        }
                    }
                }
            }

            return this.Ok(deviceIds);
        }

        [HttpGet]
        [Route("{deviceId}")]
        public async Task<IActionResult> GetDevicesAsync(string deviceId)
        {
            ServiceUriBuilder uriBuilder = new ServiceUriBuilder(TenantDataServiceName);
            Uri serviceUri = uriBuilder.Build();

            Uri getUrl = new HttpServiceUriBuilder()
                .SetServiceName(serviceUri)
                .SetPartitionKey(FnvHash.Hash(deviceId))
                .SetServicePathAndQuery($"/api/devices/{deviceId}")
                .Build();

            HttpClient httpClient = new HttpClient(new HttpServiceClientHandler());
            HttpResponseMessage response = await httpClient.GetAsync(getUrl, this.cancellationSource.Token);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                return this.StatusCode((int) response.StatusCode);
            }

            string result = await response.Content.ReadAsStringAsync();

            return this.Ok(result);
        }
    }
}