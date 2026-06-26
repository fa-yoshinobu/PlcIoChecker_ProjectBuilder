# GUI Requirements

This app exists because configuring PLC IO Checker projects on a smartphone is
tedious. The PC GUI should make project data entry easier, then export project
JSON v5 and QR codes for Android and iOS.

## Primary Workflows

The main screen should prioritize:

- Devices
  - Device rows should allow an optional comment. Duplicate rows with the same
    address share one comment.
- Time chart targets
  - Limit registration to 20 targets to match the mobile apps.
- Traps
  - Limit registration to 20 definitions to match the mobile apps.

These are the project settings users need to enter and edit most often.

## Supporting Workflows

These settings are necessary, but they are not the product focus:

- Project name
- Vendor and CPU model
- Connection settings
  - MELSEC connection settings include a remote password field.
- QR chunk size, QR display size, QR error correction

They should live behind secondary tabs or output/export areas.

## Project JSON Compatibility

Use the shared `plc-io-checker-project` schema v5 as the source of truth for
JSON structure and selection values:

- `schema`
- `schemaVersion`
- `projectId`
- `projectName`
- `plc`
- `deviceList`
- `timeChart`
- `deviceMeta`
- `traps`
- `updatedAtEpochMs`
- `PLCIOC3|ZSTD` QR import format for Zstd-compressed QR payloads

Do not emit removed project keys, UI-only preferences, or runtime trap values.
Store comments and data types once per address in `deviceMeta`; keep
`deviceList`, `timeChart`, and `traps` scoped to their own registration data.

Use uppercase schema enum values in JSON:

- `MELSEC`, `KEYENCE`
- `REAL`, `DEMO_MOCK`
- `NORMAL`, `XYM`
- `TCP`, `UDP`
- `BIT`, `INT16`, `UINT16`, `INT32`, `UINT32`, `FLOAT32`
- `RISING_EDGE`, `FALLING_EDGE`, `CHANGE`, `GREATER_OR_EQUAL`,
  `LESS_OR_EQUAL`, `EQUAL`, `NOT_EQUAL`
