for /d /r %~dp0\..\src %%d in (obj, bin, pkg) do @if exist "%%d" rd /s/q "%%d"
