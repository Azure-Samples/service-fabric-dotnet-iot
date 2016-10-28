---
services: service-fabric
platforms: dotnet
author: vturecek
---

# Service Fabric IoT Sample #

## Setup

 1. [Set up your Service Fabric development environment](https://azure.microsoft.com/documentation/articles/service-fabric-get-started/).
 2. [Create an IoT Hub in Azure](https://azure.microsoft.com/documentation/articles/iot-hub-csharp-csharp-getstarted/#create-an-iot-hub) or use an existing IoT Hub in your Azure subscription that is not currently being used in a production application. If you're creating a new IoT Hub to run the sample, you can simply use the **F1 - Free** tier under **pricing and scale tier** to avoid incurring charges to your subscription while running the sample. 

## Deploy

 1. Make sure you have a local cluster running
 2. Open a PowerShell window and CD to `service-fabric-dotnet-iot\build`
 3. Connect to your cluster using `Connect-ServiceFabricCluster`.
 4. Run `.\deploy.ps1`

## View application traces
 1. Open the solution in Visual Studio.
 2. Open the Diagnostics Event viewer: View -> Other Windows -> Diagnostic events
 3. In the Diagnostics Event viewer, click the Configuration button with the gear icon, and replace the existing ETW providers with the following ETW providers, then click "Apply."

   ```
   Microsoft-Iot.Ingestion.RouterService
   Microsoft-IoT.Admin.WebService
   Microsoft-IoT.Tenant.WebService
   Microsoft-IoT.Tenant.DataService
   Microsoft-ServiceFabric-Services
   ```

## Have fun
 1. Once the application deployment has completed, go to `http://localhost:8081/iot` in a web browser to access the admin web UI.
 2. Using the admin UI, create an ingestion application. Give it any name, your [IoT Hub connection string], and click "Add."(https://azure.microsoft.com/documentation/articles/iot-hub-csharp-csharp-getstarted/#create-an-iot-hub).
 3. Using the admin UI, create a tenant application. Give it any name, any number of data service partitions, 1 web service instance if running locally, or -1 if running it Azure, and click "Add."
 4. Once the tenant application is created, click the "Web portal" link to see the tenant's dashboard. *Note:* It may take a minute for the web portal to become available.
 5. Run the simple device emulator that's included with the sample under `service-fabric-dotnet-iot\src\Iot.DeviceEmulator`.
    1. Open the solution in Visual Studio 2015.
    2. Set the Iot.DeviceEmulator project as the start-up project and press F5 or ctrl+F5 to run it.
    3. Follow the instructions in the command prompt to register devices with IoT Hub and send messages to the tenant application created in step 3.

## Clean up
 1. Open a PowerShell window and CD to `service-fabric-dotnet-iot\build`
 2. Run `.\obliterate.ps1`

## Conceptual overview

This IoT sample project demonstrates a multi-tenant IoT solution using Azure IoT Hub for device message ingress and Azure Service Fabric for device message access and processing. In this example, the system allows an adminstrator to add any number of "tenants" to the system to consume messages through any number of IoT Hub instances. Tenants can view their devices and device messages through a Web UI. Messages sent from devices are expected to include a tenant name and device ID for the message ingestion application to determine which tenant to send the message to.

![Conceptual][1]

### Patterns demonstrated in this sample

 - Reading messages from IoT Hub with [EventHubReceiver](https://msdn.microsoft.com/library/microsoft.servicebus.messaging.eventhubreceiver.aspx). A partitioned stateful Service Fabric service provides a simple and intuitive way to read from IoT Hub partitions by simply mapping stateful service partitions to IoT Hub partitions one-to-one. This approach does not require any addition coordination or leasing between IoT Hub readers. Service Fabric manages creation, placement, availability, and scaling of the stateful service partitions. A Reliable Collection is used in each partition to keep track of the position, or *offset* in the IoT Hub event stream.
 - Multi-tenancy using dynamically-created named application instances. An administrative application uses the Service Fabric management APIs to create a new named application instance for each new tenant, so that each tenant gets their own instance of the message storing and processing service, isolated from other tenants.
 - Service-to-service communication using HTTP with ASP.NET Core. Services expose HTTP endpoints to communicate with other services.

## Architectural overview

This example makes use of several Azure products:
 - **IoT Hub** for device management and device message ingress.
 - **Service Fabric** hosts the message processing applications. 
 - **Azure Storage (optional)** can optionally be used for long-term message storage, but is not shown in this example.

![Overview][2]

### Structure

The solution is composed of three separate Service Fabric applications, each with their own set of services:

#### Admin Application
Used by an adminstrator to create instances of the Ingestion Application and Tenant Application.
 - **Web Service**: The only service in this application, the web service is a stateless ASP.NET Core service that presents a management UI. API controllers perform Service Fabric management operations using `FabricClient`. 

#### Ingestion Application
An instance of this application connects to an IoT Hub, reads device events, and forwards data to the tenant identified in the device event message.
 - **Router Service**: The only service in this application, the router service is the stateful partitioned service that connects to IoT Hub and forwards device messages to the appropriate tenant application.

 #### Tenant Application
A tenant is a user of the system. The system can have multiple tenants. Device event data flows into tenant applications for viewing and processing.
 - **Data Service**: A stateful service that holds the most recent device message for a tenant. This service is partitioned so that tenants with a large number of devices can scale horizontally to meet their requirements. 
 - **Web Service**: A stateless ASP.NET Core service that presents a UI for the tenant to see devices and their most recent messages. 

### Deployment

This sample project **does not** create instances of each application during deployment time. The project is designed to create new application instances at runtime through the Admin UI. In order to create application instances at runtime, the application types must be registered at deployment time. A fresh deployment of this sample looks like this:

 - IoTAdminApplicationType
   - fabric:/IoT.Admin.Application
 - IotIngestionApplicationType
 - IotTenantApplicationType

 Note that only the IoTAdminApplicationType has a named instance running. The other two application types are registered, but no named application instances are created by default. The web UI in the admin application is used to dynamicall create named instances of the other two applications.
 

<!--Image references-->
[1]: ./docs/conceptual.png
[2]: ./docs/overview.png