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
    using Common;

    public class DevicesControllerTests
    {
        [Fact]
        public async Task GetAll()
        {
            ServiceCancellation cancelSource = new ServiceCancellation(CancellationToken.None);
            MockReliableStateManager stateManager = new MockReliableStateManager();

            IReliableDictionary<string, DeviceEvent> store =
                await stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);

            Dictionary<string, DeviceEvent> expected = new Dictionary<string, DeviceEvent>();
            expected.Add("device1", new DeviceEvent(DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1))));
            expected.Add("device2", new DeviceEvent(DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(2))));
               

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                foreach (var item in expected)
                {
                    await store.SetAsync(tx, item.Key, item.Value);
                }
            }

            DevicesController target = new DevicesController(stateManager, cancelSource);
            IActionResult result = await target.GetAsync();

            Assert.True(result is OkObjectResult);

            IEnumerable<dynamic> actual = ((OkObjectResult) result).Value as IEnumerable<dynamic>;

            foreach (dynamic item in actual)
            {
                Assert.Equal< DateTimeOffset>(expected[item.Id].Timestamp, item.Timestamp);
            }
        }

        [Fact]
        public async Task GetAllEmpty()
        {
            ServiceCancellation cancelSource = new ServiceCancellation(CancellationToken.None);
            MockReliableStateManager stateManager = new MockReliableStateManager();

            IReliableDictionary<string, DeviceEvent> store =
                await stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);

            DevicesController target = new DevicesController(stateManager, cancelSource);
            IActionResult result = await target.GetAsync();

            Assert.True(result is OkObjectResult);

            IEnumerable<dynamic> actual = ((OkObjectResult) result).Value as IEnumerable<dynamic>;

            Assert.False(actual.Any());
        }

        [Fact]
        public async Task GetQueueLength()
        {
            ServiceCancellation cancelSource = new ServiceCancellation(CancellationToken.None);
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