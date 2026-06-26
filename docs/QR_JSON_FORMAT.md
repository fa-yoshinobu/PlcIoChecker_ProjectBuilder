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
- `projectId`
- `projectName`
- `plc`
- `deviceList`
- `timeChart`
- `deviceMeta`
- `traps`
- `updatedAtEpochMs`

`deviceList` and `timeChart` entries contain only `address`.
`deviceMeta` entries contain `address`, `dataType`, and optional `comment`.
Trap entries contain `id`, `enabled`, `address`, `condition`, and
`comparisonValue`; trap data types are resolved through `deviceMeta`.

MELSEC routing uses decimal `networkNo` / `stationNo` values and `0x`-prefixed
hexadecimal `moduleIoNo` / `multidropNo` strings. Remote passwords are never
emitted in JSON or QR payloads; the mobile apps store them in device-local
secure storage after the user enters them.

`plc.cpuModel` is the mobile app canonical connection model key. ProjectBuilder
keeps friendly labels such as `iQ-R` and `KV-8000` in the UI, but schema v1 JSON
emits values such as `melsec:iq-r` and `keyence:kv-8000`. ProjectBuilder must
not emit a separate `plcProfile` field in schema v1.

ProjectBuilder enforces the mobile app registration limits: up to 20 Time Chart
targets and up to 20 trap definitions.

## Value Sets

- `plc.vendor`: `MELSEC`, `KEYENCE`
- `plc.cpuModel`: mobile app canonical model key, for example `melsec:iq-r` or `keyence:kv-x500`
- `plc.connection.mode`: `REAL`, `DEMO_MOCK`
- `plc.keyence.deviceMode`: `NORMAL`, `XYM`
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
