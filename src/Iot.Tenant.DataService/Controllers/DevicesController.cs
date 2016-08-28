// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Tenant.DataService.Controllers
{
    using Iot.Tenant.DataService.Models;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    [Route("api/[controller]")]
    public class DevicesController : Controller
    {
        private readonly CancellationTokenSource serviceCancellationSource;

        private readonly IReliableStateManager stateManager;

        public DevicesController(IReliableStateManager stateManager, CancellationTokenSource serviceCancellationSource)
        {
            this.stateManager = stateManager;
            this.serviceCancellationSource = serviceCancellationSource;
        }


        [HttpGet]
        [Route("")]
        public async Task<IActionResult> Get()
        {
            IReliableDictionary<string, DeviceEvent> store =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);

            List<string> devices = new List<string>();
            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                var enumerable = await store.CreateEnumerableAsync(tx);
                var enumerator = enumerable.GetAsyncEnumerator();

                while (await enumerator.MoveNextAsync(this.serviceCancellationSource.Token))
                {
                    devices.Add(enumerator.Current.Key);
                }
            }

            return this.Ok(devices);
        }

        [HttpGet]
        [Route("{deviceId}")]
        public async Task<IActionResult> Get(string deviceId)
        {
            IReliableDictionary<string, DeviceEvent> store =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                ConditionalValue<DeviceEvent> result = await store.TryGetValueAsync(tx, deviceId);

                if (result.HasValue)
                {
                    return this.Ok(result.Value);
                }

                return this.NotFound();
            }
        }

        [HttpGet]
        [Route("queue/length")]
        public async Task<IActionResult> QueueLength()
        {
            IReliableQueue<DeviceEventSeries> queue =
                await this.stateManager.GetOrAddAsync<IReliableQueue<DeviceEventSeries>>(DataService.EventQueueName);

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                // When the number of items in the queue reaches a certain size..
                return this.Ok(await queue.GetCountAsync(tx));
            }
        }
    }
}
