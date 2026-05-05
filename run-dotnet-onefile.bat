@echo off
setlocal

set "EXE=%~dp0dotnet\publish\win-x64\PlcIoCheckerQr.Wpf.exe"

if not exist "%EXE%" (
  call "%~dp0build-dotnet-onefile.bat"
  if errorlevel 1 exit /b %errorlevel%
)

start "" "%EXE%"
