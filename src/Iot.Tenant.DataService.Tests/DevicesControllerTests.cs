using Iot.Mocks;
using Iot.Tenant.DataService.Controllers;
using Iot.Tenant.DataService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Iot.Tenant.DataService.Tests
{
    public class DevicesControllerTests
    {
        [Fact]
        public async Task GetAll()
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            MockReliableStateManager stateManager = new MockReliableStateManager();

            IReliableDictionary<string, DeviceEvent> store =
                await stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);

            List<string> expected = new List<string>(new string[]
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
            
            IEnumerable<string> actual = ((OkObjectResult)result).Value as IEnumerable<string>;

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

            IEnumerable<string> actual = ((OkObjectResult)result).Value as IEnumerable<string>;

            Assert.False(actual.Any());
        }

        [Fact]
        public async Task GetDevice()
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            MockReliableStateManager stateManager = new MockReliableStateManager();

            IReliableDictionary<string, DeviceEvent> store =
                await stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);

            string expectedKey = "device1";
            DeviceEvent expectedValue = new DeviceEvent(new DateTimeOffset(1, TimeSpan.Zero));

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await store.SetAsync(tx, expectedKey, expectedValue);
                await store.SetAsync(tx, "device2", new DeviceEvent(new DateTimeOffset(2, TimeSpan.Zero)));
            }

            DevicesController target = new DevicesController(stateManager, cancelSource);
            IActionResult result = await target.GetAsync(expectedKey);

            Assert.True(result is OkObjectResult);

            DeviceEvent actual = ((OkObjectResult)result).Value as DeviceEvent;

            Assert.Equal(expectedValue.Timestamp, actual.Timestamp);
        }

        [Fact]
        public async Task GetDeviceNotFound()
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            MockReliableStateManager stateManager = new MockReliableStateManager();

            IReliableDictionary<string, DeviceEvent> store =
                await stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);
            
            DevicesController target = new DevicesController(stateManager, cancelSource);
            IActionResult result = await target.GetAsync("somekey");

            Assert.True(result is NotFoundResult);
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
            long actual = (long)((OkObjectResult)result).Value;

            Assert.Equal(1, actual);
        }
    }
}
