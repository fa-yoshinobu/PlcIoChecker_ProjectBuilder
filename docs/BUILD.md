# Build And Development

This document is for developers. The README is focused on app usage.

## .NET WPF App

The production app is the .NET WPF implementation under `dotnet/`.

```powershell
cd D:\github\PlcIoChecker_ProjectBuilder\dotnet
dotnet run --project src\PlcIoCheckerQr.Wpf
```

WPF requires Windows. Core library tests can run on macOS/Linux, but the WPF app
itself uses Windows Desktop SDK support.

## Tests

```powershell
cd D:\github\PlcIoChecker_ProjectBuilder
dotnet test dotnet\tests\PlcIoCheckerQr.Core.Tests\PlcIoCheckerQr.Core.Tests.csproj
```

On macOS with a newer SDK, this repository has also been tested with:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test dotnet/tests/PlcIoCheckerQr.Core.Tests/PlcIoCheckerQr.Core.Tests.csproj
```

## Build Single-File EXE

```powershell
cd D:\github\PlcIoChecker_ProjectBuilder
.\build-dotnet-onefile.bat
```

The executable is written to:

```text
dotnet\publish\win-x64\PlcIoCheckerProjectBuilder.exe
```

Language resources are published to `dotnet\publish\win-x64\Languages\*.json`
so UI text can be edited without rebuilding. The same resources are also
embedded in the executable as a fallback when the external language files are
missing.

The GitHub Release zip contains only `PlcIoCheckerProjectBuilder.exe`. External
language JSON files are not included in the release package.
