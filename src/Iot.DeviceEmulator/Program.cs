using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Exceptions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Fabric;
using System.Fabric.Query;
using Iot.Common;

namespace Iot.DeviceEmulator
{
    class Program
    {
        private static string connectionString;
        private static string clusterAddress;
        private static RegistryManager registryManager;
        private static FabricClient fabricClient;
        private static IEnumerable<Device> devices;
        private static IEnumerable<string> tenants;

        static void Main(string[] args)
        {
            Console.WriteLine("Enter IoT Hub connection string: ");
            connectionString = Console.ReadLine();

            Console.WriteLine("Enter Service Fabric cluster address where your IoT project is deployed (or blank for local): ");
            clusterAddress = Console.ReadLine();

            registryManager = RegistryManager.CreateFromConnectionString(connectionString);
            fabricClient = String.IsNullOrEmpty(clusterAddress)
                ? new FabricClient()
                : new FabricClient(clusterAddress);

            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        devices = await registryManager.GetDevicesAsync(Int32.MaxValue);
                        tenants = (await fabricClient.QueryManager.GetApplicationListAsync())
                            .Where(x => x.ApplicationTypeName == Names.TenantApplicationTypeName)
                            .Select(x => x.ApplicationName.ToString().Replace(Names.TenantApplicationNamePrefix + "/", ""));

                        Console.WriteLine();
                        Console.WriteLine("Devices IDs: ");
                        foreach (Device device in devices)
                        {
                            Console.WriteLine(device.Id);
                        }

                        Console.WriteLine();
                        Console.WriteLine("Tenants: ");
                        foreach (string tenant in tenants)
                        {
                            Console.WriteLine(tenant);
                        }

                        Console.WriteLine();
                        Console.WriteLine("Commands:");
                        Console.WriteLine("1: Register a device");
                        Console.WriteLine("2: Register random devices");
                        Console.WriteLine("3: Send data from a device");
                        Console.WriteLine("4: Send data from all devices");
                        Console.WriteLine("5: Exit");

                        string command = Console.ReadLine();

                        switch (command)
                        {
                            case "1":
                                Console.WriteLine("Make up a device ID: ");
                                string deviceId = Console.ReadLine();
                                await AddDeviceAsync(deviceId);
                                break;
                            case "2":
                                Console.WriteLine("How many devices? ");
                                int num = Int32.Parse(Console.ReadLine());
                                await AddRandomDevicesAsync(num);
                                break;
                            case "3":
                                Console.WriteLine("Tenant: ");
                                string tenant = Console.ReadLine();
                                Console.WriteLine("Device id: ");
                                string deviceKey = Console.ReadLine();
                                await SendDeviceToCloudMessagesAsync(deviceKey, tenant);
                                break;
                            case "4":
                                Console.WriteLine("Tenant: ");
                                string tenantName = Console.ReadLine();
                                Console.WriteLine("Iterations: ");
                                int iterations = Int32.Parse(Console.ReadLine());
                                await SendAllDevices(tenantName, iterations);
                                break;
                            case "5":
                                return;
                            default:
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Oops, {0}", ex.Message);
                    }
                }
            })
            .GetAwaiter().GetResult();
        }

        private static async Task SendAllDevices(string tenant, int iterations)
        {
            for (int i = 0; i < iterations; ++i)
            {
                try
                {
                    List<Task> tasks = new List<Task>(devices.Count());
                    foreach (Device device in devices)
                    {
                        tasks.Add(SendDeviceToCloudMessagesAsync(device.Id, tenant));
                    }

                    await Task.WhenAll(tasks);
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Send failed. {0}", ex.Message);
                }
            }
        }

        private static async Task SendDeviceToCloudMessagesAsync(string deviceId, string tenant)
        {
            string iotHubUri = connectionString.Split(';')
                .First(x => x.StartsWith("HostName=", StringComparison.InvariantCultureIgnoreCase))
                .Replace("HostName=", "").Trim();

            Device device = devices.FirstOrDefault(x => x.Id == deviceId);
            if (device == null)
            {
                Console.WriteLine("Device '{0}' doesn't exist.", deviceId);
            }

            DeviceClient deviceClient = DeviceClient.Create(iotHubUri, new DeviceAuthenticationWithRegistrySymmetricKey(deviceId, device.Authentication.SymmetricKey.PrimaryKey));

            List<object> events = new List<object>();
            for (int i = 0; i < 10; ++i)
            {
                var body = new
                {
                    Timestamp = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(i))
                };

                events.Add(body);
            }

            Microsoft.Azure.Devices.Client.Message message;
            JsonSerializer serializer = new JsonSerializer();
            using (MemoryStream stream = new MemoryStream())
            {
                using (StreamWriter streamWriter = new StreamWriter(stream))
                using (JsonTextWriter jsonWriter = new JsonTextWriter(streamWriter))
                {
                    serializer.Serialize(jsonWriter, events);
                }

                message = new Microsoft.Azure.Devices.Client.Message(stream.GetBuffer());
                message.Properties.Add("TenantID", tenant);
                message.Properties.Add("DeviceID", deviceId);

                await deviceClient.SendEventAsync(message);

                Console.WriteLine($"Sent message: {Encoding.UTF8.GetString(stream.GetBuffer())}");
            }
        }

        private static async Task AddRandomDevicesAsync (int count)
        {
            int start = devices.Count();

            for (int i = start; i < start + count; ++i)
            {
                await AddDeviceAsync("device" + i);
            }
        }

        private static async Task AddDeviceAsync(string deviceId)
        {
            RegistryManager registryManager = RegistryManager.CreateFromConnectionString(connectionString);
            
            try
            {
                await registryManager.AddDeviceAsync(new Device(deviceId));
                Console.WriteLine("Added device {0}", deviceId);
            }
            catch (Microsoft.Azure.Devices.Common.Exceptions.DeviceAlreadyExistsException)
            {
            }
        }
    }

}
