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

namespace Iot.DeviceEmulator
{
    class Program
    {
        static string connectionString;
        
        static void Main(string[] args)
        {
            Console.WriteLine("Enter IoT Hub connection string: ");
            connectionString = Console.ReadLine();

            Task.Run(async () =>
            {
                while (true)
                {
                    Console.WriteLine("Commands:");
                    Console.WriteLine("1: Register or get a device");
                    Console.WriteLine("2: Send data from a device");
                    Console.WriteLine("3: Exit");

                    string command = Console.ReadLine();

                    switch (command)
                    {
                        case "1":
                            await AddDeviceAsync();
                            break;
                        case "2":
                            await SendDeviceToCloudMessagesAsync();
                            break;
                        case "3":
                            return;
                        default:
                            break;
                    }
                }
            }).GetAwaiter().GetResult();
        }

        private static async Task SendDeviceToCloudMessagesAsync()
        {
            Console.WriteLine("Device key: ");
            string deviceKey = Console.ReadLine();

            Console.WriteLine("Device ID: ");
            string deviceName = Console.ReadLine();
            
            Console.WriteLine("Enter the name of the tenant that owns the device. This should match the name used when creating a tenant application (e.g., Contoso):");
            string tenant = Console.ReadLine();

            string iotHubUri = connectionString.Split(';')
                .First(x => x.StartsWith("HostName=", StringComparison.InvariantCultureIgnoreCase))
                .Replace("HostName=", "").Trim();

            string deviceId = Guid.NewGuid().ToString();

            DeviceClient deviceClient = DeviceClient.Create(iotHubUri, new DeviceAuthenticationWithRegistrySymmetricKey(deviceName, deviceKey));
            
            List<object> events = new List<object>();
            for (int i = 0; i < 20; ++i)
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

        private static async Task AddDeviceAsync()
        {
            Console.WriteLine("Make up a device ID: ");
            string deviceId = Console.ReadLine();

            RegistryManager registryManager = RegistryManager.CreateFromConnectionString(connectionString);
            
            Device device;

            try
            {
                device = await registryManager.AddDeviceAsync(new Device(deviceId));
            }
            catch (Microsoft.Azure.Devices.Common.Exceptions.DeviceAlreadyExistsException)
            {
                device = await registryManager.GetDeviceAsync(deviceId);
            }
            Console.WriteLine($"Device key for {deviceId} is: {device.Authentication.SymmetricKey.PrimaryKey}");
        }
    }

}
