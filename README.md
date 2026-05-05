# PlcIoChecker QR

PC tool for creating PLC IO Checker project settings for Android.

The Android app in `../PlcIoChecker_Android` is the QR reader. The QR JSON uses
the shared `plc-io-checker-project` schema v2 consumed by the Android and iOS
apps.

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

## App-Compatible QR Format

Each QR contains:

```text
PLCIOC2D|<session>|<index>|<total>|<sha256>|<payload-chunk>
```

- `index` is 1-based.
- `sha256` is calculated from the minified JSON bytes after decompression.
- `payload-chunk` is a slice of base64url-encoded raw-deflate-compressed JSON without padding.
- Android reads this with `Inflater(true)` and iOS reads it with zlib raw inflate
  (`inflateInit2(..., -MAX_WBITS)`). The .NET app therefore uses raw deflate,
  not zlib or gzip containers.
- Do not replace the app-side decoder with a normal zlib/gzip container decoder.
  Raw deflate is intentional; otherwise QR payloads can scan successfully but
  fail during import or checksum validation.
- QR count is not fixed. Readers must join all chunks first, then decompress the
  combined compressed bytes.

## Project JSON v2 Values

- `plc.vendor`: `MELSEC`, `KEYENCE`
- `plc.connection.mode`: `REAL`, `DEMO_MOCK`
- `plc.keyence.deviceMode`: `NORMAL`, `XYM`
- `plc.connection.transport`: `TCP`, `UDP`
- `traps.condition`: `RISING_EDGE`, `FALLING_EDGE`, `CHANGE`, `GREATER_OR_EQUAL`, `LESS_OR_EQUAL`, `EQUAL`, `NOT_EQUAL`
- `dataType`: `BIT`, `INT16`, `UINT16`, `INT32`, `UINT32`, `FLOAT32`

Generated project JSON includes only the shared v2 schema fields. UI-only
preferences and runtime observation values are not emitted.

## Python Reference

```powershell
cd D:\github\PlcIoChecker_QR\python
python -m venv .venv
.\.venv\Scripts\activate
pip install -r requirements.txt
python app.py
```
