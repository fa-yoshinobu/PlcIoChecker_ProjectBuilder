# PlcIoChecker QR

PC tool for creating PLC IO Checker project settings for Android.

The Android app in `../PlcIoChecker_Android` is the QR reader. The QR JSON and
selection values in this repository follow that Android app's `ProjectDefinition`
model and enum names.

## Product Intent

This app exists to avoid difficult smartphone text entry. Project data is edited
on a PC, then exported as Android-compatible JSON or QR codes.

Primary workflows:

- Device settings
- Time chart target settings
- Trap settings

Project metadata, connection settings, and QR output settings are supporting
workflow areas.

## Folders

- `dotnet/`: current .NET WPF implementation and tests.
- `python/`: earlier Python test implementation kept for reference.

## .NET WPF App

```powershell
cd D:\github\PlcIoChecker_QR\dotnet
dotnet run --project src\PlcIoCheckerQr.Wpf
```

The app can generate QR pages, save Android-importable JSON, and save QR PNG
files. QR display uses a dedicated screen so it can be shown large enough for
phone scanning.

## Build Single-File EXE

```powershell
cd D:\github\PlcIoChecker_QR
.\build-dotnet-onefile.bat
```

The executable is written to `dotnet\publish\win-x64`.

## Android-Compatible QR Format

Each QR contains:

```text
PLCIOC2D|<session>|<index>|<total>|<sha256>|<payload-chunk>
```

- `index` is 1-based.
- `sha256` is calculated from the minified JSON bytes after decompression.
- `payload-chunk` is a slice of base64url-encoded raw-deflate-compressed JSON without padding.
- Android reads this with `Inflater(true)`, so the .NET app uses raw deflate, not zlib or gzip containers.

## Android Enum Values Used By The GUI

- Vendor: `Melsec`, `Keyence`
- Connection mode: `Real`, `DemoMock`
- KEYENCE display: `Normal`, `Xym`
- Transport: `Tcp`, `Udp`
- Block density: `Compact`, `Detailed`
- Trap condition: `Rise`, `Fall`, `Change`, `GreaterOrEqual`, `LessOrEqual`, `Equal`, `NotEqual`
- Device data type: `Bit`, `Int16`, `UInt16`, `Int32`, `UInt32`, `Float32`

## Python Reference

```powershell
cd D:\github\PlcIoChecker_QR\python
python -m venv .venv
.\.venv\Scripts\activate
pip install -r requirements.txt
python app.py
```
