# QR And Project JSON Format

This document describes the app-compatible QR payload and project JSON shape.
The README is intentionally kept as a user guide.

## Project JSON

The generated JSON uses the shared `plc-io-checker-project` schema v1 consumed
by Android and iOS.

Generated project JSON includes only shared schema v1 fields. UI-only
preferences and runtime observation values are not emitted.

ProjectBuilder emits shared address metadata in `deviceMeta`. Comments and data
types are stored there once per address. `deviceList`, `timeChart`, and `traps`
store membership and trap settings only.

Top-level fields:

- `schema`
- `schemaVersion`
- `exportInfo`
- `projectId`
- `projectName`
- `plc`
- `deviceList`
- `timeChart`
- `deviceMeta`
- `traps`
- `updatedAtEpochMs`

Exporters should emit fields in this order for diff readability. Importers must
still treat JSON object order as non-semantic.

`exportInfo` is an output memo overwritten by the exporting app. It contains
`source` (`PROJECT_BUILDER`, `ANDROID`, or `IOS`) and `version` (the exporter app
version). Importers must not treat this as project identity.

`deviceList` and `timeChart` entries contain only `address`.
`deviceMeta` entries contain `address`, `dataType`, and optional `comment`.
Comments are normalized to one line and must be 1024 characters or fewer.
Trap entries contain `id`, `enabled`, `address`, `condition`, and
`comparisonValue`; trap data types are resolved through `deviceMeta`.

MELSEC routing uses decimal `networkNo` / `stationNo` values and a canonical
`moduleIo` target name. MultiDrop is not emitted and mobile apps communicate
with MultiDrop fixed to `0x00`. Remote passwords are never emitted in JSON or QR
payloads; the mobile apps store them in device-local secure storage after the
user enters them.

`plc.cpuModel` is the mobile app canonical connection model key. ProjectBuilder
keeps friendly labels such as `MELSEC iQ-R (built-in)`, `KEYENCE KV-8000`, and
`KEYENCE KV-8000 (XYM)` in the UI, but schema v1 JSON emits values such as
`melsec:iq-r`, `melsec:iq-r:rj71en71`, `melsec:qcpu:qj71e71-100`,
`keyence:kv-8000`, and `keyence:kv-8000-xym`.
ProjectBuilder must not emit a separate `plcProfile` field in schema v1.

ProjectBuilder enforces the mobile app registration limits: up to 20 Time Chart
targets and up to 20 trap definitions.

## Value Sets

- `plc.vendor`: `MELSEC`, `KEYENCE`
- `plc.cpuModel`: mobile app canonical model key
  - MELSEC: `melsec:iq-r`, `melsec:iq-r:rj71en71`, `melsec:iq-f`, `melsec:iq-l`, `melsec:mx-r`, `melsec:mx-f`, `melsec:qnudv`, `melsec:qnudv:qj71e71-100`, `melsec:qnu`, `melsec:qnu:qj71e71-100`, `melsec:qcpu:qj71e71-100`, `melsec:lcpu`, `melsec:lcpu:lj71e71-100`
  - KEYENCE: `keyence:kv-nano`, `keyence:kv-nano-xym`, `keyence:kv-3000`, `keyence:kv-3000-xym`, `keyence:kv-5000`, `keyence:kv-5000-xym`, `keyence:kv-7000`, `keyence:kv-7000-xym`, `keyence:kv-8000`, `keyence:kv-8000-xym`, `keyence:kv-x500`, `keyence:kv-x500-xym`
- `plc.connection.mode`: `REAL`, `DEMO_MOCK`
- `plc.connection.transport`: `TCP`, `UDP`
- `traps.condition`: `RISING_EDGE`, `FALLING_EDGE`, `CHANGE`, `GREATER_OR_EQUAL`, `LESS_OR_EQUAL`, `EQUAL`, `NOT_EQUAL`
- `dataType`: `BIT`, `INT16`, `UINT16`, `INT32`, `UINT32`, `FLOAT32`

## QR Payload

Each QR contains:

```text
PLCIOC1|ZSTD|<session>|<index>|<total>|<sha256>|<payload-chunk>
```

- `index` is 1-based.
- `sha256` is calculated from the minified JSON bytes after decompression.
- `payload-chunk` is a slice of base64url-encoded Zstd-compressed JSON without padding.
- QR count is not fixed.
- Readers must join all chunks first, then decompress the combined compressed bytes.

## Compression Requirements

`PLCIOC1|ZSTD` uses a Zstandard frame and requires Zstd support in the importing
app.

## Compatibility Policy

Do not add silent fallback, alias conversion, or compatibility normalization for
invalid values. Invalid data should fail visibly so QR/JSON bugs are caught early.
