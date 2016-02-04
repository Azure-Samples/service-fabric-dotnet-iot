#Configuration, Deployment & Debugging
The following documentation covers how to configure, deploy and optionally debug the sample solution.

## Configuration  ##

### Configure Storage Actor ###
Modify */src/Actors/StorageActor/PackageRoot/Config/Settings.xml* by providing the following values
- **ConnectionString** Enter the complete Azure Storage connection string.
- **TableName** Optionally, change the Azure Table name.


### Configure Power BI Actor ###
Modify */src/Actors/PowerBIActor/PackageRoot/Config/Settings.xml* by providing the following values
- **ClientId** Application Client ID (Power BI Actor has to be provisioned in Azure Active Directory).
- **Username** Azure Active Directory user with access on Power BI dashboard.
- **Password** password for the above user.

> You don't need to change the rest of the configuration keys.
### Azure Deployment ###
The following is additional configuration needed when deploying the solution to Azure Service Fabric Clusters
1. Ensure that port used by Processor Management Service has a load balancing rule configured on your Azure Load Balancer (Part of your cluster). To change the port check */src/Gateway/IoTProcessorManagementService/PackageRoot/ServiceManifest.xml* (The Resources/EndPoint Node).
2. Modify */src/Gateway/IoTProcessorManagementService/PackageRoot/Config/Settings.xml* (*PublishingAddressHostName* parameter) with the FQDN assigned to your load balancer. This address is used by the service as Publishing Address.

#### Using The Management PowerShell ####  
The management PowerShell module points by default to local cluster, however you can execute

```
#PowerShell ..
$MgmtEP = Get-IoTManagementApiEndPoint -ServiceFabricEndPoint {Cluster EndPoint XXX:19000}
# All cmdlets expect optional parameter named ManagementEndPoint where you can pass the $MgmtEP acquired above.
```
Alternatively you can replace the cluster name in IoT-Functions.psm1, check *_default_ServiceFabricConnection* variable then re-import the module.

### Optional: Modify Event Schema ###
The solution depends on the following properties
1. Device Actor uses FloorId property to activate or connect to existing Floor Actor
2. Floor Actor uses BuildingId property to activate or connect to existing building Actor
3. Event Hub publisher is used to identify the device (as a unique device id).

> If you choose to modify these properties then the associated code will have to be modified as well.

To modify event schema, please modify
1. */src/actors/PowerBIActor/PackageRoot/Data/Datasetschema.json* Used to provision the PowerBI Dataset
> 2. */src/Gateway/IoTProcessorManagement/internalfunctions.cs* Contains a static method used by a cmdlet that sends test events to the solution

> If the dataset has been created, then you will need to navigate to PowerBI dashboard and manually delete the dataset.

### Optional: Configure Event Processor Service ###
You can modify the number of Event Hub Processor service partitions by modifying */src/Gateway/ProcessorApp/ApplicationManifest.xml* ServiceTemplates node.

> Event Processor Service supports *UniformInt64Partition* partitioning only.

## Deployment ##
The following are the steps to manually deploy the solution (using VS.NET):

1. Right click on *IoTApplication* application and click Publish.
2. Right click on *IoTEventHubProcessorApp* application and click Publish.
3. Right Click on *IoTProcessorManagementApp* application and click Publish.

> VS.NET creates a default *IoTEventHupProcessorApp* Service Fabric application as part of the default publishing process. This service is not needed (management component provision these as needed), please remove it by using Service Fabric Explorer or Service Fabric PowerShell.

## Debugging the Solution ##
If you are trying to debug either the management or the actor applications then you can just set each as a VS.NET startup project and follow standard debugging procedure. Alternatively you can use VS.NET's Debug->Attach to Process method.

If you are trying to debug the event processor then you can use one of the following methods:

### Wait for Debugger Attach###
Un-comment *_WAIT_FOR_DEBUGGER* conditional compilation flag in *IoTEventHubProcessorService* class (first two lines). This flag forces the *RunAsync* method to wait for a debugger. Follow standard deployment procedures. Then use Debug->Attach to Process to attach to all IoTEventHubProcessor.exe instances.

###Debugging Processor Code as a Stand Alone App###
1. Uncomment *_VS_DEPLOY* conditional compilation flag.
2. Modify the code in *GetAssignedProcessorAsync* method by hard wiring a processor definition (in *#if _VS_DEPLOY* condition).
3. Set *IoTEventHubProcessorApp* as VS.NET startup project.
4. Publish *IoTApplication* by Right Click->Publish.
5. Press F5
