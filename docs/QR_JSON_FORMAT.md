# QR And Project JSON Format

This document describes the app-compatible QR payload and project JSON shape.
The README is intentionally kept as a user guide.

## Project JSON

The generated JSON uses the shared `plc-io-checker-project` schema v2 consumed
by Android and iOS.

Generated project JSON includes only shared v2 schema fields. UI-only
preferences and runtime observation values are not emitted.

ProjectBuilder may emit `deviceList[].comment` when a device comment is set.
If the same device address is registered more than once, the first non-empty
comment for that address is copied to each matching `deviceList` entry.

Top-level fields:

- `schema`
- `schemaVersion`
- `projectId`
- `projectName`
- `plc`
- `deviceList`
- `timeChart`
- `traps`
- `updatedAtEpochMs`

`deviceList` entries contain `address` and `dataType`. They may also contain
`comment` when a ProjectBuilder device comment is set.

## Value Sets

- `plc.vendor`: `MELSEC`, `KEYENCE`
- `plc.connection.mode`: `REAL`, `DEMO_MOCK`
- `plc.keyence.deviceMode`: `NORMAL`, `XYM`
- `plc.connection.transport`: `TCP`, `UDP`
- `traps.condition`: `RISING_EDGE`, `FALLING_EDGE`, `CHANGE`, `GREATER_OR_EQUAL`, `LESS_OR_EQUAL`, `EQUAL`, `NOT_EQUAL`
- `dataType`: `BIT`, `INT16`, `UINT16`, `INT32`, `UINT32`, `FLOAT32`

## QR Payload

Each QR contains:

```text
PLCIOC3|ZSTD|<session>|<index>|<total>|<sha256>|<payload-chunk>
```

- `index` is 1-based.
- `sha256` is calculated from the minified JSON bytes after decompression.
- `payload-chunk` is a slice of base64url-encoded Zstd-compressed JSON without padding.
- QR count is not fixed.
- Readers must join all chunks first, then decompress the combined compressed bytes.

## Compression Requirements

`PLCIOC3|ZSTD` uses a Zstandard frame and requires Zstd support in the importing
app. Older `PLCIOC2D` raw-deflate QR payloads are intentionally unsupported.

## Compatibility Policy

Do not add silent fallback, alias conversion, or compatibility normalization for
invalid values. Invalid data should fail visibly so QR/JSON bugs are caught early.
