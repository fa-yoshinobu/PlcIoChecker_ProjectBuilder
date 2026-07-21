# GUI Requirements

This app exists because configuring PLC IO Checker projects on a smartphone is
tedious. The PC GUI should make project data entry easier, then export project
JSON v1 and QR codes for Android and iOS.

## Primary Workflows

The main screen should prioritize:

- List
  - List rows should allow an optional comment. Duplicate rows with the same
    address share one comment.
- Time Chart
  - Limit registration to 20 targets to match the mobile apps.
- Trap
  - Limit registration to 20 definitions to match the mobile apps.
- Comment
  - Edit address comments and data types stored in `deviceMeta`.

These are the project settings users need to enter and edit most often.

Clipboard paste should use explicit canonical values. Do not silently accept
localized aliases, guess missing data types, or apply default trap conditions for
clipboard data.

Clipboard paste shows a preview dialog before import. Each row is validated
independently and marked OK, error (with the reason), or skipped (blank
address). Import applies only the OK rows; error rows are never silently
converted and must be fixed in the source data.

## Supporting Workflows

These settings are necessary, but they are not the product focus:

- Project name
- Vendor and CPU model
- PLC settings
  - Project name, vendor, CPU model, connection mode, PLC IP / host, port,
    transport, monitor interval, timeout, and MELSEC routing fields.
- QR chunk size, QR display size, QR error correction

They should live in the editor, with output/export controls kept separate from
the row editing grids.

## Project JSON Compatibility

Use the shared `plc-io-checker-project` schema v2 as the source of truth for
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
- `PLCIOC1|ZSTD` QR import format for Zstd-compressed QR payloads

Do not emit removed project keys, UI-only preferences, or runtime trap values.
Store comments and data types once per address in `deviceMeta`; keep
`deviceList`, `timeChart`, and `traps` scoped to their own registration data.
Comments are normalized to one line and limited to 1024 characters.

Use uppercase schema enum values in JSON:

- `MELSEC`, `KEYENCE`
- `REAL`, `DEMO_MOCK`
- `TCP`, `UDP`
- `BIT`, `INT16`, `UINT16`, `INT32`, `UINT32`, `FLOAT32`
- `RISING_EDGE`, `FALLING_EDGE`, `CHANGE`, `GREATER_OR_EQUAL`,
  `LESS_OR_EQUAL`, `EQUAL`, `NOT_EQUAL`
