// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Tenant.DataService.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Iot.Tenant.DataService.Models;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Newtonsoft.Json;
    using System.IO;

    [Route("api/[controller]")]
    public class EventsController : Controller
    {
        private readonly CancellationTokenSource serviceCancellationSource;

        private readonly IReliableStateManager stateManager;

        public EventsController(IReliableStateManager stateManager, CancellationTokenSource serviceCancellationSource)
        {
            this.stateManager = stateManager;
            this.serviceCancellationSource = serviceCancellationSource;
        }


        [HttpPost]
        [Route("{deviceId}")]
        public async Task<IActionResult> Post(string deviceId, [FromBody] IEnumerable<DeviceEvent> events)
        {
            if (String.IsNullOrEmpty(deviceId))
            {
                return this.BadRequest();
            }

            if (events == null)
            {
                return this.BadRequest();
            }

            DeviceEvent max = events.FirstOrDefault();

            if (max == null)
            {
                return this.Ok();
            }

            DeviceEventSeries eventList = new DeviceEventSeries(deviceId, events);

            IReliableDictionary<string, DeviceEvent> store =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);

            IReliableQueue<DeviceEventSeries> queue =
                await this.stateManager.GetOrAddAsync<IReliableQueue<DeviceEventSeries>>(DataService.EventQueueName);

            // determine the most recent event in the time series
            foreach (DeviceEvent item in events)
            {
                if (item.Timestamp > max.Timestamp)
                {
                    max = item;
                }
            }

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                // Update the current value if the max in the new set is more recent
                // Or add the max in the current set if a value for this device doesn't exist.
                await store.AddOrUpdateAsync(
                    tx,
                    deviceId,
                    max,
                    (key, currentValue) =>
                    {
                        return max.Timestamp > currentValue.Timestamp
                            ? max
                            : currentValue;
                    });

                // Queue the time series for offload
                await queue.EnqueueAsync(tx, eventList);

                // Commit
                await tx.CommitAsync();
            }

            return this.Ok();
        }
    }
}