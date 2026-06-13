# SLMP Manual Audit Reflection - 2026-06-13

This repository was checked as a downstream project after the SLMP library API
cleanup.

## Result

- No direct SLMP API usage requiring source changes was found.
- The project JSON is affected by the mobile app profile-label cleanup:
  `plc.cpuModel` is now the mobile app canonical connection model key, such as
  `melsec:iq-r` or `keyence:kv-x500`.
- ProjectBuilder keeps friendly CPU model labels in the UI, maps them to
  canonical JSON labels at export time, and maps canonical JSON labels back to
  friendly labels when loading JSON.
- ProjectBuilder does not emit a `plcProfile` field in schema v3.

## Verification

```text
dotnet build dotnet/src/PlcIoCheckerQr.Core/PlcIoCheckerQr.Core.csproj
passed

DOTNET_ROLL_FORWARD=Major dotnet test dotnet/tests/PlcIoCheckerQr.Core.Tests/PlcIoCheckerQr.Core.Tests.csproj
63 passed
```

## Notes

- `docs/QR_JSON_FORMAT.md` and payload tests pin the current JSON behavior:
  `cpuModel` is canonical and `plcProfile` is absent.
- The detailed SLMP manual decisions are recorded in the SLMP implementation
  repositories and `plc-comm-slmp-cross-verify`.
