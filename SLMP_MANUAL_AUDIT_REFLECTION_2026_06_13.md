# SLMP Manual Audit Reflection - 2026-06-13

This repository was checked as a downstream project after the SLMP library API
cleanup.

## Result

- No direct SLMP API usage requiring source changes was found.
- The project JSON is affected only by interpretation: `plc.cpuModel` remains a
  mobile app model label such as `iQ-R` or `KV-X500`. It is not the SLMP library
  profile string and must not be changed to `melsec:iq-r`.
- ProjectBuilder does not emit a `plcProfile` field in schema v3. Android and
  iOS map `cpuModel` to the canonical SLMP profile internally.

## Verification

```text
dotnet build dotnet\PlcIoCheckerQr.sln
passed

dotnet test dotnet\PlcIoCheckerQr.sln
61 passed
```

## Notes

- `docs/QR_JSON_FORMAT.md` and payload tests pin the current JSON behavior:
  `cpuModel` is preserved and `plcProfile` is absent.
- The detailed SLMP manual decisions are recorded in the SLMP implementation
  repositories and `plc-comm-slmp-cross-verify`.
