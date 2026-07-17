# PLC IO Checker Project Builder

<img src="logo.png" alt="PLC IO Checker Project Builder logo" width="240">

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![.NET CI](https://github.com/fa-yoshinobu/PlcIoChecker_ProjectBuilder/actions/workflows/dotnet-ci.yml/badge.svg)](https://github.com/fa-yoshinobu/PlcIoChecker_ProjectBuilder/actions/workflows/dotnet-ci.yml)
![Windows WPF](https://img.shields.io/badge/platform-Windows%20WPF-0078D4?logo=windows)
![Project JSON v2](https://img.shields.io/badge/project%20JSON-v2-2ea44f)
![QR Zstd](https://img.shields.io/badge/QR-Zstd-f97316)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

[![C#](https://img.shields.io/badge/C%23-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)

[![Release](https://github.com/fa-yoshinobu/PlcIoChecker_ProjectBuilder/actions/workflows/release.yml/badge.svg)](https://github.com/fa-yoshinobu/PlcIoChecker_ProjectBuilder/actions/workflows/release.yml)

PLC IO Checker Project Builder is a PC tool for creating PLC IO Checker project settings and transferring them to the mobile apps as JSON or QR codes.

Instead of entering long PLC settings on a phone, edit the project on a PC and import it from the Android/iOS app QR scanner.

## Public Manual

- Manual site: <https://fa-yoshinobu.github.io/PlcIoChecker_Site/>
- ProjectBuilder manual: <https://fa-yoshinobu.github.io/PlcIoChecker_Site/projectbuilder/projectbuilder.html>

## Download

Download the Windows executable package from the release Assets:

- Releases: <https://github.com/fa-yoshinobu/PlcIoChecker_ProjectBuilder/releases>
- Asset file: `PlcIoCheckerProjectBuilder-win-x64.zip`

Unzip `PlcIoCheckerProjectBuilder-win-x64.zip`, then start `PlcIoCheckerProjectBuilder.exe`.

Related repositories:

- ProjectBuilder: <https://github.com/fa-yoshinobu/PlcIoChecker_ProjectBuilder>
- Manual site: <https://github.com/fa-yoshinobu/PlcIoChecker_Site>

Primary configuration areas:

- PLC connection settings
- List registration
- Time Chart targets
- Trap settings

This repository contains only the PC-side project builder and QR export tool. The Android and iOS PLC IO Checker apps are separate paid products, and their source code is not included in this repository.

The supported desktop implementation is the .NET WPF app.

## Usage

1. Start `PlcIoCheckerProjectBuilder.exe`.
2. Enter the project and PLC settings.
   - Project name
   - Vendor
   - CPU model
   - IP address, port, and transport
3. Register monitored addresses in `List`.
   - Rows can be pasted from Excel.
   - Columns are `Address / Data type / Comment`.
4. Register graph targets in `Time Chart`.
   - Pasted columns are `Address / Data type / Comment`.
   - Up to 20 channels can be imported.
5. Register trigger rules in `Trap`.
   - Pasted columns are `Address / Data type / Comment / Condition / Threshold / Enabled`.
   - Examples: rising edge, change, greater than or equal.
   - Up to 20 traps can be imported.
6. Save JSON or generate QR pages.
7. In the Android/iOS app, open `QR Import` and scan the displayed QR pages in order.

Pasting from Excel opens a paste preview dialog that validates every row before
import. Valid rows are marked OK, invalid rows show the error reason, and rows
with a blank address are marked skipped. Importing applies only the OK rows, so
one bad row no longer fails the whole paste.

Clipboard import uses canonical values only. Data type and trap condition must be
explicit; localized aliases, guessed data types, and implicit trap conditions are
not accepted. During JSON / QR generation, a blank data type is filled only when
the same address already has one explicit data type in another row. Trap
conditions that do not match the address kind must be corrected explicitly.

For multi-page QR output, import completes after the mobile app has scanned every page.

Project limits are shared with the mobile apps: 1,000 List devices, 20 Time
Chart channels, 20 Trap definitions, 5 MiB project JSON, 4,096 QR pages, 1 MiB
compressed QR data, and 5 MiB decompressed QR data. Polling accepts 100–10,000
ms and timeout accepts 250–10,000 ms independently. ProjectBuilder accepts and
emits schema version 2 only; old or invalid JSON is rejected without conversion.

## Scanning Tips

- Display each QR as large as possible.
- Scan one page before moving to the next.
- If a device cannot read the QR reliably, reduce the QR chunk size so the tool generates more smaller QR pages.
- After import, confirm that the project name and List contents changed in the mobile app.

## Documents

- [Build and development](docs/BUILD.md)
- [QR/JSON format](docs/QR_JSON_FORMAT.md)
- [GUI requirements](docs/GUI_REQUIREMENTS.md)

## License

| Item | Value |
| --- | --- |
| License | [MIT](LICENSE) |
| Scope | Applies only to the PC-side project builder and QR export tool in this repository. It does not include the Android/iOS PLC IO Checker mobile apps. |
