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
    public class EventControllerTests
    {
        [Fact]
        public async Task MissingPayload()
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            MockReliableStateManager stateManager = new MockReliableStateManager();

            string expectedDeviceId = "some-device";

            EventsController target = new EventsController(stateManager, cancelSource);

            IActionResult result = await target.Post(expectedDeviceId, null);

            Assert.True(result is BadRequestResult);
        }

        [Fact]
        public async Task MissingDeviceId()
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            MockReliableStateManager stateManager = new MockReliableStateManager();

            EventsController target = new EventsController(stateManager, cancelSource);

            IActionResult result = await target.Post(null, new DeviceEvent[0]);

            Assert.True(result is BadRequestResult);
        }

        [Fact]
        public async Task NoEvents()
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            MockReliableStateManager stateManager = new MockReliableStateManager();

            string expectedDeviceId = "some-device";

            EventsController target = new EventsController(stateManager, cancelSource);

            IActionResult result = await target.Post(expectedDeviceId, new DeviceEvent[0]);

            Assert.True(result is OkResult);
        }

        [Fact]
        public async Task SingleEvent()
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            MockReliableStateManager stateManager = new MockReliableStateManager();

            IReliableDictionary<string, DeviceEvent> store =
                await stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);

            IReliableQueue<DeviceEventSeries> queue =
                await stateManager.GetOrAddAsync<IReliableQueue<DeviceEventSeries>>(DataService.EventQueueName);

            string expectedDeviceId = "some-device";
            DeviceEvent expectedDeviceEvent = new DeviceEvent(new DateTimeOffset(1, TimeSpan.Zero));

            EventsController target = new EventsController(stateManager, cancelSource);

            IActionResult result = await target.Post(expectedDeviceId, new[] { expectedDeviceEvent });

            Assert.True(result is OkResult);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                ConditionalValue<DeviceEvent> actualStoredEvent = await store.TryGetValueAsync(tx, expectedDeviceId);
                ConditionalValue<DeviceEventSeries> actualQueuedEvent = await queue.TryDequeueAsync(tx);

                Assert.True(actualStoredEvent.HasValue);
                Assert.Equal(expectedDeviceEvent.Timestamp, actualStoredEvent.Value.Timestamp);

                Assert.True(actualQueuedEvent.HasValue);
                Assert.Equal(expectedDeviceEvent.Timestamp, actualQueuedEvent.Value.Events.First().Timestamp);

                await tx.CommitAsync();
            }
        }

        [Fact]
        public async Task AddMostRecentEvent()
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            MockReliableStateManager stateManager = new MockReliableStateManager();

            IReliableDictionary<string, DeviceEvent> store =
                await stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);

            IReliableQueue<DeviceEventSeries> queue =
                await stateManager.GetOrAddAsync<IReliableQueue<DeviceEventSeries>>(DataService.EventQueueName);

            string expectedDeviceId = "some-device";

            List<DeviceEvent> expectedDeviceList = new List<DeviceEvent>();
            DeviceEvent expectedDeviceEvent = new DeviceEvent(new DateTimeOffset(100, TimeSpan.Zero));
            for (int i = 0; i < 10; ++i)
            {
                expectedDeviceList.Add(new DeviceEvent(new DateTimeOffset(i, TimeSpan.Zero)));
            }
            expectedDeviceList.Insert(4, expectedDeviceEvent);

            EventsController target = new EventsController(stateManager, cancelSource);

            IActionResult result = await target.Post(expectedDeviceId, expectedDeviceList);

            Assert.True(result is OkResult);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                ConditionalValue<DeviceEvent> actualStoredEvent = await store.TryGetValueAsync(tx, expectedDeviceId);
                ConditionalValue<DeviceEventSeries> actualQueuedEvent = await queue.TryDequeueAsync(tx);

                Assert.True(actualStoredEvent.HasValue);
                Assert.Equal(expectedDeviceEvent.Timestamp, actualStoredEvent.Value.Timestamp);

                Assert.True(actualQueuedEvent.HasValue);
                Assert.True(actualQueuedEvent.Value.Events.Select(x => x.Timestamp).SequenceEqual(expectedDeviceList.Select(x => x.Timestamp)));

                await tx.CommitAsync();
            }
        }


        [Fact]
        public async Task UpdateMostRecentEvent()
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            MockReliableStateManager stateManager = new MockReliableStateManager();

            IReliableDictionary<string, DeviceEvent> store =
                await stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);

            IReliableQueue<DeviceEventSeries> queue =
                await stateManager.GetOrAddAsync<IReliableQueue<DeviceEventSeries>>(DataService.EventQueueName);

            string expectedDeviceId = "some-device";
            DeviceEvent expectedDeviceEvent = new DeviceEvent(new DateTimeOffset(100, TimeSpan.Zero));
            EventsController target = new EventsController(stateManager, cancelSource);
            IActionResult result = await target.Post(expectedDeviceId, new[] { expectedDeviceEvent });

            Assert.True(result is OkResult);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                ConditionalValue<DeviceEvent> actualStoredEvent = await store.TryGetValueAsync(tx, expectedDeviceId);

                Assert.True(actualStoredEvent.HasValue);
                Assert.Equal(expectedDeviceEvent.Timestamp, actualStoredEvent.Value.Timestamp);

                await tx.CommitAsync();
            }

            expectedDeviceEvent = new DeviceEvent(new DateTimeOffset(200, TimeSpan.Zero));
            result = await target.Post(expectedDeviceId, new[] { expectedDeviceEvent });

            Assert.True(result is OkResult);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                ConditionalValue<DeviceEvent> actualStoredEvent = await store.TryGetValueAsync(tx, expectedDeviceId);

                Assert.True(actualStoredEvent.HasValue);
                Assert.Equal(expectedDeviceEvent.Timestamp, actualStoredEvent.Value.Timestamp);

                await tx.CommitAsync();
            }
        }


        [Fact]
        public async Task IgnoreOldEvent()
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            MockReliableStateManager stateManager = new MockReliableStateManager();

            IReliableDictionary<string, DeviceEvent> store =
                await stateManager.GetOrAddAsync<IReliableDictionary<string, DeviceEvent>>(DataService.EventDictionaryName);

            IReliableQueue<DeviceEventSeries> queue =
                await stateManager.GetOrAddAsync<IReliableQueue<DeviceEventSeries>>(DataService.EventQueueName);

            string expectedDeviceId = "some-device";
            DeviceEvent expectedDeviceEvent = new DeviceEvent(new DateTimeOffset(100, TimeSpan.Zero));
            EventsController target = new EventsController(stateManager, cancelSource);
            IActionResult result = await target.Post(expectedDeviceId, new[] { expectedDeviceEvent });

            Assert.True(result is OkResult);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                ConditionalValue<DeviceEvent> actualStoredEvent = await store.TryGetValueAsync(tx, expectedDeviceId);

                Assert.True(actualStoredEvent.HasValue);
                Assert.Equal(expectedDeviceEvent.Timestamp, actualStoredEvent.Value.Timestamp);

                await tx.CommitAsync();
            }

            DeviceEvent oldEvent = new DeviceEvent(new DateTimeOffset(10, TimeSpan.Zero));
            result = await target.Post(expectedDeviceId, new[] { oldEvent });
            Assert.True(result is OkResult);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                ConditionalValue<DeviceEvent> actualStoredEvent = await store.TryGetValueAsync(tx, expectedDeviceId);

                Assert.True(actualStoredEvent.HasValue);
                Assert.Equal(expectedDeviceEvent.Timestamp, actualStoredEvent.Value.Timestamp);

                await tx.CommitAsync();
            }
        }
    }
}
