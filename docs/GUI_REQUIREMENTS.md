# GUI Requirements

This app exists because configuring PLC IO Checker projects on a smartphone is
tedious. The PC GUI should make project data entry easier, then export project
JSON v2 and QR codes for Android and iOS.

## Primary Workflows

The main screen should prioritize:

- Devices
  - Device rows should allow an optional comment. Duplicate rows with the same
    address share one comment.
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

## Project JSON Compatibility

Use the shared `plc-io-checker-project` schema v2 as the source of truth for
JSON structure and selection values:

- `projectId`
- `projectName`
- `plc`
- `deviceList`
- `timeChart`
- `traps`
- `updatedAtEpochMs`
- `PLCIOC3|ZSTD` QR import format for Zstd-compressed QR payloads

Do not emit removed project keys, UI-only preferences, or runtime trap values.

Use uppercase v2 enum values in JSON:

- `MELSEC`, `KEYENCE`
- `REAL`, `DEMO_MOCK`
- `NORMAL`, `XYM`
- `TCP`, `UDP`
- `BIT`, `INT16`, `UINT16`, `INT32`, `UINT32`, `FLOAT32`
- `RISING_EDGE`, `FALLING_EDGE`, `CHANGE`, `GREATER_OR_EQUAL`,
  `LESS_OR_EQUAL`, `EQUAL`, `NOT_EQUAL`
