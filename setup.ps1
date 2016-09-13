$buildConfig = "Debug"
$imageStore = "file:C:\SfDevCluster\Data\ImageStoreShare"

Connect-ServiceFabricCluster

Unregister-ServiceFabricApplicationType -ApplicationTypeName IotIngestionApplicationType -ApplicationTypeVersion 1.0.0 -Force
Copy-ServiceFabricApplicationPackage -ApplicationPackagePath "src\Iot.Ingestion.Application\pkg\$buildConfig" -ImageStoreConnectionString $imageStore -ApplicationPackagePathInImageStore Iot.Ingestion.Application
Register-ServiceFabricApplicationType -ApplicationPathInImageStore Iot.Ingestion.Application

$app = Get-ServiceFabricApplication -ApplicationName fabric:/Iot.Ingestion/Homer
$parameters = ConvertFrom-StringData $app."ApplicationParameters"
Start-ServiceFabricApplicationUpgrade -ApplicationName fabric:/Iot.Ingestion/Homer -ApplicationTypeVersion 1.0.1 -Monitored -FailureAction Rollback -ApplicationParameter $parameters


Unregister-ServiceFabricApplicationType -ApplicationTypeName IotTenantApplicationType -ApplicationTypeVersion 1.0.0 -Force
Copy-ServiceFabricApplicationPackage -ApplicationPackagePath "src\Iot.Tenant.Application\pkg\$buildConfig" -ImageStoreConnectionString $imageStore -ApplicationPackagePathInImageStore Iot.Tenant.Application
Register-ServiceFabricApplicationType -ApplicationPathInImageStore Iot.Tenant.Application