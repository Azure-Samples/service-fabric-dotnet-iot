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

            while (true)
            {
                Console.WriteLine("Commands:");
                Console.WriteLine("1: Register or get a device");
                Console.WriteLine("2: Send data from a device");

                string command = Console.ReadLine();

                switch (command)
                {
                    case "1":
                        AddDeviceAsync().Wait();
                        break;
                    case "2":
                        SendDeviceToCloudMessagesAsync().Wait();
                        break;
                    default:
                        break;
                }
            }
        }

        private static async Task SendDeviceToCloudMessagesAsync()
        {
            Console.WriteLine("Device key: ");
            string deviceKey = Console.ReadLine();

            Console.WriteLine("IoT Hub host name: ");
            string iotHubUri = Console.ReadLine();

            Console.WriteLine("Enter the name of the tenant that owns the device. This should match the name used when creating a tenant application (e.g., Contoso):");
            string tenant = Console.ReadLine();

            DeviceClient deviceClient = DeviceClient.Create(iotHubUri, new DeviceAuthenticationWithRegistrySymmetricKey("testdevice", deviceKey));

            string deviceId = Guid.NewGuid().ToString();
            string header = $"{tenant};{deviceId}";

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
                using (BinaryWriter binaryWriter = new BinaryWriter(stream, Encoding.UTF8, true))
                using (StreamWriter streamWriter = new StreamWriter(stream, Encoding.UTF8, 1024, true))
                using (JsonTextWriter jsonWriter = new JsonTextWriter(streamWriter))
                {
                    binaryWriter.Write(header);
                    serializer.Serialize(jsonWriter, events);
                }

                message = new Microsoft.Azure.Devices.Client.Message(stream.GetBuffer());
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
