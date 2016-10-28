CALL "C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\Tools\VsMSBuildCmd.bat"
%~dp0\nuget.exe restore %~dp0\..\src
msbuild %~dp0\..\src\Iot.Admin.Application\Iot.Admin.Application.sfproj /t:Package
msbuild %~dp0\..\src\Iot.Ingestion.Application\Iot.Ingestion.Application.sfproj /t:Package
msbuild %~dp0\..\src\Iot.Tenant.Application\Iot.Tenant.Application.sfproj /t:Package

