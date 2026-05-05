# .NET Porting Guide for AI Agents

This Python prototype is only a reference implementation. The production tool is
the .NET WPF app under `dotnet/`.

## Current Project JSON

Python and .NET must emit the shared project schema:

- `schema`: `plc-io-checker-project`
- `schemaVersion`: `2`
- `projectId`
- `projectName`
- `plc.vendor`: `MELSEC` or `KEYENCE`
- `plc.cpuModel`
- `plc.connection.mode`: `REAL` or `DEMO_MOCK`
- `plc.connection.host`
- `plc.connection.port`
- `plc.connection.transport`: `TCP` or `UDP`
- `plc.connection.pollingIntervalMs`
- `plc.connection.timeoutMs`
- `plc.melsec.networkNo`
- `plc.melsec.stationNo`
- `plc.melsec.moduleIoNo`
- `plc.melsec.multidropNo`
- `plc.keyence.deviceMode`: `NORMAL` or `XYM`
- `deviceList`
- `timeChart`
- `traps`
- `updatedAtEpochMs`

`timeChart` and `traps` contain their own address and data type. They do not
depend on the address being registered in `deviceList`.

## QR Format

The only supported QR prefix is:

```text
PLCIOC2D|<session>|<index>|<total>|<sha256>|<payload-chunk>
```

Use minified JSON bytes for the SHA-256 checksum. Compress those same bytes with
raw deflate (`zlib.compressobj(level=9, wbits=-15)`) and base64url encode the
compressed bytes without padding.

## GUI Values

- Vendor: `MELSEC`, `KEYENCE`
- Connection mode: `REAL`, `DEMO_MOCK`
- KEYENCE device mode: `NORMAL`, `XYM`
- Transport: `TCP`, `UDP`
- Trap condition: `RISING_EDGE`, `FALLING_EDGE`, `CHANGE`, `GREATER_OR_EQUAL`, `LESS_OR_EQUAL`, `EQUAL`, `NOT_EQUAL`
- Device data type: `BIT`, `INT16`, `UINT16`, `INT32`, `UINT32`, `FLOAT32`

The default QR values are:

- chunk chars: `800`
- display px: `1000`
- correction: `L low 7%`
