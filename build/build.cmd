@if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Enterprise\MSBuild" set msbuildpath=%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Enterprise\MSBuild\15.0\Bin
@if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Community\MSBuild" set msbuildpath=%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin
@if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Professional\MSBuild" set msbuildpath=%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Professional\MSBuild\15.0\Bin

nuget.exe restore %~dp0\..\src\IoT.sln
REM dotnet restore %~dp0\..\src

"%msbuildpath%\msbuild" %~dp0\..\src\Iot.Admin.Application\Iot.Admin.Application.sfproj /t:Package /p:Platform=x64
"%msbuildpath%\msbuild" %~dp0\..\src\Iot.Ingestion.Application\Iot.Ingestion.Application.sfproj /t:Package /p:Platform=x64
"%msbuildpath%\msbuild" %~dp0\..\src\Iot.Tenant.Application\Iot.Tenant.Application.sfproj /t:Package /p:Platform=x64
 
