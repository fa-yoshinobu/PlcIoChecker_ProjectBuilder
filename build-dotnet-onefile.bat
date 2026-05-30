@echo off
setlocal

set "ROOT=%~dp0"
set "PROJECT=%ROOT%dotnet\src\PlcIoCheckerQr.Wpf\PlcIoCheckerQr.Wpf.csproj"
set "OUT=%ROOT%dotnet\publish\win-x64"

if not exist "%PROJECT%" (
  echo Project file not found: %PROJECT%
  exit /b 1
)

if exist "%OUT%" rmdir /s /q "%OUT%"

dotnet publish "%PROJECT%" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -o "%OUT%" ^
  /p:PublishSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true ^
  /p:EnableCompressionInSingleFile=true ^
  /p:DebugType=None ^
  /p:DebugSymbols=false

if errorlevel 1 exit /b %errorlevel%

echo.
echo Single-file EXE:
dir /b "%OUT%\*.exe"
echo.
echo Output folder: %OUT%
