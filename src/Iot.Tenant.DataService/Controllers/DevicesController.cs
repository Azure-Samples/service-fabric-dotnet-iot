// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Tenant.DataService.Controllers
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Iot.Tenant.DataService.Models;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Common;

    [Route("api/[controller]")]
    public class DevicesController : Controller
    {
        private readonly CancellationToken serviceCancellationToken;

        private readonly IReliableStateManager stateManager;

        public DevicesController(IReliableStateManager stateManager, ServiceCancellation serviceCancellation)
        {
            this.stateManager = stateManager;
            this.serviceCancellationToken = serviceCancellation.Token;
        }


        [HttpGet]
        [Route("")]
        public async Task<IActionResult> GetAsync()
        {
            CancellationToken token = this.serviceCancellationToken;

            IReliableDictionary<string, DeviceEvent> store =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);

            List<object> devices = new List<object>();
            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                IAsyncEnumerable<KeyValuePair<string, DeviceEvent>> enumerable = await store.CreateEnumerableAsync(tx);
                IAsyncEnumerator<KeyValuePair<string, DeviceEvent>> enumerator = enumerable.GetAsyncEnumerator();

                while (await enumerator.MoveNextAsync(token))
                {
                    devices.Add(
                        new
                        {
                            Id = enumerator.Current.Key,
                            Timestamp = enumerator.Current.Value.Timestamp
                        });
                }
            }

            return this.Ok(devices);
        }

        [HttpGet]
        [Route("queue/length")]
        public async Task<IActionResult> GetQueueLengthAsync()
        {
            IReliableQueue<DeviceEventSeries> queue =
                await this.stateManager.GetOrAddAsync<IReliableQueue<DeviceEventSeries>>(DataService.EventQueueName);

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                long count = await queue.GetCountAsync(tx);

                return this.Ok(count);
            }
        }
    }
}