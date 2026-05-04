# PlcIoChecker QR

Temporary Python tool for creating PLC IO Checker project QR codes.

The mobile apps import the same project JSON used by normal JSON import. The
JSON bytes are base64url-encoded, split into small chunks, and shown as one QR
code per chunk.

QR payloads use minified JSON to reduce the number of QR pages. JSON saved from
the PC app remains pretty-printed for manual inspection.

## Setup

```bash
cd /Users/macminim2/Development/PLC_App/PlcIoChecker_QR
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
python app.py
```

## QR Format

Each QR contains:

```text
PLCIOC1|<session>|<index>|<total>|<sha256>|<payload-chunk>
```

- `index` is 1-based.
- `sha256` is calculated from the original JSON bytes.
- `payload-chunk` is a slice of base64url text without padding.

The chunk size and QR display size are adjustable in the PC app. Larger chunks
reduce QR pages. Smaller chunks create more QR pages, but are easier for phones
to read.

Some phone models read dense QR codes better than others. For the safest
cross-device test, lower the chunk size and use:

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
