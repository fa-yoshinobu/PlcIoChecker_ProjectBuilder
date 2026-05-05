# QR And Project JSON Format

This document describes the app-compatible QR payload and project JSON shape.
The README is intentionally kept as a user guide.

## Project JSON

The generated JSON uses the shared `plc-io-checker-project` schema v2 consumed
by Android and iOS.

Generated project JSON includes only shared v2 schema fields. UI-only
preferences and runtime observation values are not emitted.

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
PLCIOC2D|<session>|<index>|<total>|<sha256>|<payload-chunk>
```

- `index` is 1-based.
- `sha256` is calculated from the minified JSON bytes after decompression.
- `payload-chunk` is a slice of base64url-encoded raw-deflate-compressed JSON without padding.
- QR count is not fixed.
- Readers must join all chunks first, then decompress the combined compressed bytes.

## Raw Deflate Requirement

Android reads this with `Inflater(true)` and iOS reads it with zlib raw inflate
(`inflateInit2(..., -MAX_WBITS)`). The .NET app therefore uses raw deflate, not
zlib or gzip containers.

Do not replace the app-side decoder with a normal zlib/gzip container decoder.
Raw deflate is intentional; otherwise QR payloads can scan successfully but fail
during import or checksum validation.

## Compatibility Policy

Do not add silent fallback, alias conversion, or compatibility normalization for
invalid values. Invalid data should fail visibly so QR/JSON bugs are caught early.
