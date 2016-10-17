CALL "C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\Tools\VsMSBuildCmd.bat"
msbuild .\..\src\Iot.Admin.Application\Iot.Admin.Application.sfproj /t:Package
msbuild .\..\src\Iot.Ingestion.Application\Iot.Ingestion.Application.sfproj /t:Package
msbuild .\..\src\Iot.Tenant.Application\Iot.Tenant.Application.sfproj /t:Package

