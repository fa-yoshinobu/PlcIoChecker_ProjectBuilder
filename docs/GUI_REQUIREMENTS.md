# GUI Requirements

This app exists because configuring PLC IO Checker projects on a smartphone is
tedious. The PC GUI should make project data entry easier, then export Android
compatible JSON and QR codes.

## Primary Workflows

The main screen should prioritize:

- Devices
- Time chart targets
- Traps

These are the project settings users need to enter and edit most often.

## Supporting Workflows

These settings are necessary, but they are not the product focus:

- Project name
- Vendor and CPU model
- Connection settings
- QR chunk size, QR display size, QR error correction

They should live behind secondary tabs or output/export areas.

## Android Compatibility

Use `../PlcIoChecker_Android` as the source of truth for JSON structure and
selection values:

- `ProjectDefinition`
- `PlcConnectionConfig`
- `DeviceDefinition`
- `TrapDefinition`
- Android enum names
- `PLCIOC2D` QR import format

Do not invent PC-only aliases for values that Android imports.
