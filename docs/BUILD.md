# Build And Development

This document is for developers. The README is focused on app usage.

## .NET WPF App

The production app is the .NET WPF implementation under `dotnet/`.

```powershell
cd dotnet
dotnet run --project src\PlcIoCheckerQr.Wpf
```

WPF requires Windows Desktop SDK support.

## Tests

```powershell
dotnet test dotnet\PlcIoCheckerQr.sln --no-restore
```

## Build Single-File EXE

```powershell
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

The GitHub Release zip contains only `PlcIoCheckerProjectBuilder.exe`; it uses
the embedded language resources.
