# PlcIoChecker QR

Temporary Python tool for creating PLC IO Checker project QR codes.

The mobile apps import the same project JSON used by normal JSON import. For QR
transfer, JSON is minified, raw-deflate-compressed, base64url-encoded, split into small
chunks, and shown as one QR code per chunk.

QR payloads use compressed minified JSON to reduce the number of QR pages. JSON
saved from the PC app remains pretty-printed for manual inspection.

## Setup

```bash
cd /Users/macminim2/Development/PLC_App/PlcIoChecker_QR
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
python app.py
```

## .NET Porting

For the future .NET PC app, read [DOTNET_PORTING_GUIDE.md](DOTNET_PORTING_GUIDE.md).
It records the exact QR payload format, raw deflate requirement, UI scope, and
implementation traps learned from the Python prototype.

## QR Format

Each QR contains:

```text
PLCIOC2D|<session>|<index>|<total>|<sha256>|<payload-chunk>
```

- `index` is 1-based.
- `sha256` is calculated from the minified JSON bytes after decompression.
- `payload-chunk` is a slice of base64url-encoded raw-deflate-compressed JSON without padding.
- `PLCIOC1` and `PLCIOC2Z` are not supported. Invalid or old QR formats should fail visibly.

## Compression Note

Use raw deflate, not Python's normal `zlib.compress()` container format.
Android can read the zlib container, but iOS `Compression.COMPRESSION_ZLIB`
did not restore Python zlib-container bytes correctly in testing. The symptom
was that the QR was detected, but the app rejected it as invalid after checksum
verification. `PLCIOC2D` means the payload is raw deflate (`wbits=-15`) wrapped
with base64url, and both Android and iOS should decode that exact format.

The chunk size and QR display size are adjustable in the PC app. Larger chunks
reduce QR pages. Smaller chunks create more QR pages, but are easier for phones
to read.

Some phone models read dense QR codes better than others. The PC app default is
`QR 1枚の文字数=350` because iOS can be less stable with dense one-page QR
codes. For the safest cross-device test, use:

- `QR 1枚の文字数`: `200` or `350`
- `QR表示サイズpx`: `650` or larger
- `QR誤り訂正`: `L 読取優先`

Trap conditions must use the mobile app enum names exactly:

- `Rise`
- `Fall`
- `Change`
- `GreaterOrEqual`
- `LessOrEqual`
- `Equal`
- `NotEqual`

Do not emit aliases such as `Changed`; the PC-side GUI should prevent invalid
values instead of relying on mobile-side fallback.
