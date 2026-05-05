# PlcIoChecker QR Python Memo

The Python folder is a prototype and regression reference for QR payload
generation. Keep it aligned with the .NET WPF app and the Android/iOS import
schema.

## Scope

- Build project JSON schema v2.
- Generate QR text with `PLCIOC2D`.
- Use raw deflate and base64url without padding.
- Keep QR defaults at 800 payload characters, 1000 px preview, and correction L.

## Project Data Rules

- `deviceList` is the device registration list.
- `timeChart` is an independent list of typed monitor targets, capped at 20.
- `traps` is an independent list of typed trap targets.
- MELSEC routing fields are emitted only under `plc.melsec`.
- KEYENCE mode is emitted only under `plc.keyence.deviceMode`.

The Python prototype should not invent compatibility fallbacks. If an input value
is outside the schema value set, fail early.
