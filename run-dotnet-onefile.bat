@echo off
setlocal

set "EXE=%~dp0dotnet\publish\win-x64\PlcIoCheckerProjectBuilder.exe"

if not exist "%EXE%" (
  call "%~dp0build-dotnet-onefile.bat"
  if errorlevel 1 exit /b %errorlevel%
)

start "" "%EXE%"
