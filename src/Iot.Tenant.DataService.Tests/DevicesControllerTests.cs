// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Tenant.DataService.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Iot.Mocks;
    using Iot.Tenant.DataService.Controllers;
    using Iot.Tenant.DataService.Models;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Xunit;

    public class DevicesControllerTests
    {
        [Fact]
        public async Task GetAll()
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            MockReliableStateManager stateManager = new MockReliableStateManager();

            IReliableDictionary<string, DeviceEvent> store =
                await stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);

            List<string> expected = new List<string>(
                new string[]
                {
                    "device1",
                    "device2"
                });

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                for (int i = 0; i < expected.Count; ++i)
                {
                    await store.SetAsync(tx, expected[i], new DeviceEvent(DateTimeOffset.UtcNow));
                }
            }

            DevicesController target = new DevicesController(stateManager, cancelSource);
            IActionResult result = await target.GetAsync();

            Assert.True(result is OkObjectResult);

            IEnumerable<string> actual = ((OkObjectResult) result).Value as IEnumerable<string>;

            Assert.True(actual.SequenceEqual(expected));
        }

        [Fact]
        public async Task GetAllEmpty()
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            MockReliableStateManager stateManager = new MockReliableStateManager();

            IReliableDictionary<string, DeviceEvent> store =
                await stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);

            DevicesController target = new DevicesController(stateManager, cancelSource);
            IActionResult result = await target.GetAsync();

            Assert.True(result is OkObjectResult);

            IEnumerable<string> actual = ((OkObjectResult) result).Value as IEnumerable<string>;

            Assert.False(actual.Any());
        }

        [Fact]
        public async Task GetQueueLength()
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            MockReliableStateManager stateManager = new MockReliableStateManager();

            IReliableQueue<DeviceEventSeries> queue =
                await stateManager.GetOrAddAsync<IReliableQueue<DeviceEventSeries>>(DataService.EventQueueName);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await queue.EnqueueAsync(tx, new DeviceEventSeries("", new DeviceEvent[0]));
            }

            DevicesController target = new DevicesController(stateManager, cancelSource);
            IActionResult result = await target.GetQueueLengthAsync();

            Assert.True(result is OkObjectResult);
            long actual = (long) ((OkObjectResult) result).Value;

            Assert.Equal(1, actual);
        }
    }
}