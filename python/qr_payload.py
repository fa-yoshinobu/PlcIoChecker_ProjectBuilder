from __future__ import annotations

import base64
import hashlib
import io
import json
import re
import time
import uuid
from dataclasses import dataclass
from typing import Iterable

import zstandard as zstd


QR_PREFIX = "PLCIOC3"
QR_ALGORITHM = "ZSTD"
PROJECT_SCHEMA = "plc-io-checker-project"
PROJECT_SCHEMA_VERSION = 2
MAX_TIME_CHART_CHANNELS = 20

VALID_VENDORS = {"MELSEC", "KEYENCE"}
VALID_CONNECTION_MODES = {"REAL", "DEMO_MOCK"}
VALID_KEYENCE_DEVICE_MODES = {"NORMAL", "XYM"}
VALID_TRANSPORTS = {"TCP", "UDP"}
VALID_DATA_TYPES = {"BIT", "INT16", "UINT16", "INT32", "UINT32", "FLOAT32"}
VALID_TRAP_CONDITIONS = {
    "RISING_EDGE",
    "FALLING_EDGE",
    "CHANGE",
    "GREATER_OR_EQUAL",
    "LESS_OR_EQUAL",
    "EQUAL",
    "NOT_EQUAL",
}


@dataclass(frozen=True)
class QrChunk:
    session: str
    index: int
    total: int
    checksum: str
    payload: str

    def text(self) -> str:
        return f"{QR_PREFIX}|{QR_ALGORITHM}|{self.session}|{self.index}|{self.total}|{self.checksum}|{self.payload}"


def slugify(text: str, fallback: str = "plc-project") -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")
    return slug or fallback


def parse_lines(text: str) -> list[str]:
    return [
        line.strip()
        for line in text.splitlines()
        if line.strip() and not line.strip().startswith("#")
    ]


def make_project(
    *,
    project_name: str,
    vendor: str,
    connection_mode: str,
    host: str,
    port: int,
    polling_interval_ms: int,
    timeout_ms: int,
    cpu_model: str,
    keyence_device_mode: str,
    transport: str,
    network_no: int,
    station_no: int,
    module_io_no: int,
    multidrop_no: int,
    device_list_text: str,
    time_chart_text: str,
    traps_text: str,
) -> dict:
    now_ms = int(time.time() * 1000)
    project_id = f"{slugify(project_name)}-{now_ms}"
    normalized_vendor = validate_value(vendor, VALID_VENDORS, "vendor")
    device_list = parse_device_list(device_list_text, normalized_vendor)
    device_types: dict[str, str] = {}
    for item in device_list:
        device_types.setdefault(item["address"], item["dataType"])
    time_chart = parse_time_chart(time_chart_text, device_types, normalized_vendor)
    traps = parse_traps(traps_text, device_types, normalized_vendor)

    plc = {
        "vendor": normalized_vendor,
        "cpuModel": cpu_model.strip(),
        "connection": {
            "mode": validate_value(connection_mode, VALID_CONNECTION_MODES, "connection mode"),
            "host": host.strip(),
            "port": int(port),
            "transport": validate_value(transport, VALID_TRANSPORTS, "transport"),
            "pollingIntervalMs": int(polling_interval_ms),
            "timeoutMs": int(timeout_ms),
        },
    }
    if normalized_vendor == "MELSEC":
        plc["melsec"] = {
            "networkNo": int(network_no),
            "stationNo": int(station_no),
            "moduleIoNo": int(module_io_no),
            "multidropNo": int(multidrop_no),
        }
    else:
        plc["keyence"] = {
            "deviceMode": validate_value(keyence_device_mode, VALID_KEYENCE_DEVICE_MODES, "KEYENCE device mode"),
        }

    return {
        "schema": PROJECT_SCHEMA,
        "schemaVersion": PROJECT_SCHEMA_VERSION,
        "projectId": project_id,
        "projectName": project_name,
        "plc": plc,
        "deviceList": device_list,
        "timeChart": time_chart,
        "traps": traps,
        "updatedAtEpochMs": now_ms,
    }


def parse_device_list(text: str, vendor: str) -> list[dict[str, str]]:
    parsed: list[dict[str, str]] = []
    comments_by_address: dict[str, str] = {}
    for line in parse_lines(text):
        parts = [part.strip() for part in line.split(",")]
        if not parts or not parts[0]:
            continue
        address = normalize_address(parts[0])
        has_data_type = len(parts) > 1 and is_data_type_field(parts[1])
        data_type = normalize_data_type(
            parts[1] if has_data_type else guess_data_type(address, vendor),
            address,
            vendor,
        )
        comment_index = 2 if has_data_type else 2 if len(parts) > 2 and not parts[1] else 1
        comment = comment_from_parts(parts, comment_index)
        if comment and address not in comments_by_address:
            comments_by_address[address] = comment
        parsed.append({"address": address, "dataType": data_type, "comment": comment})

    result: list[dict[str, str]] = []
    for item in parsed:
        common_comment = comments_by_address.get(item["address"], "")
        if common_comment:
            result.append({**item, "comment": common_comment})
        else:
            result.append({"address": item["address"], "dataType": item["dataType"]})
    return result


def is_data_type_field(text: str) -> bool:
    return text.strip().upper() in VALID_DATA_TYPES


def comment_from_parts(parts: list[str], start_index: int) -> str:
    if start_index >= len(parts):
        return ""
    return normalize_comment(",".join(parts[start_index:]))


def normalize_comment(text: str) -> str:
    return text.replace("\r", " ").replace("\n", " ").strip()


def parse_time_chart(text: str, device_types: dict[str, str], vendor: str) -> list[dict[str, str]]:
    result: list[dict[str, str]] = []
    seen: set[str] = set()
    for line in parse_lines(text):
        parts = [part.strip() for part in line.split(",")]
        if not parts or not parts[0]:
            continue
        address = normalize_address(parts[0])
        if address in seen:
            continue
        data_type = parts[1] if len(parts) > 1 and parts[1] else device_types.get(address, guess_data_type(address, vendor))
        result.append({"address": address, "dataType": normalize_data_type(data_type, address, vendor)})
        seen.add(address)
        if len(result) >= MAX_TIME_CHART_CHANNELS:
            break
    return result


def parse_traps(text: str, device_types: dict[str, str], vendor: str) -> list[dict[str, object]]:
    result: list[dict[str, object]] = []
    for offset, line in enumerate(parse_lines(text), start=1):
        parts = [part.strip() for part in line.split(",")]
        if len(parts) < 3 or not parts[0] or not parts[1] or not parts[2]:
            raise ValueError("Trap format is address,dataType,condition,comparisonValue,enabled")
        address = normalize_address(parts[0])
        data_type = normalize_data_type(parts[1] or device_types.get(address, guess_data_type(address, vendor)), address, vendor)
        condition = validate_value(parts[2], VALID_TRAP_CONDITIONS, "trap condition")
        comparison_value = None
        if len(parts) >= 4 and parts[3]:
            comparison_value = float(parts[3])
        enabled = True
        if len(parts) >= 5 and parts[4]:
            enabled = parts[4].lower() not in {"0", "false", "off", "no"}
        result.append(
            {
                "id": f"trap-{offset}",
                "enabled": enabled,
                "address": address,
                "dataType": data_type,
                "condition": condition,
                "comparisonValue": comparison_value,
            }
        )
    return result


def validate_value(text: str, allowed: set[str], label: str) -> str:
    value = text.strip().upper()
    if value not in allowed:
        allowed_text = ", ".join(sorted(allowed))
        raise ValueError(f"Invalid {label}: {text}. Use one of: {allowed_text}")
    return value


def normalize_address(text: str) -> str:
    return text.strip().upper()


def normalize_data_type(text: str, address: str, vendor: str) -> str:
    value = validate_value(text, VALID_DATA_TYPES, "data type")
    if guess_data_type(address, vendor) == "BIT":
        return "BIT"
    return "INT16" if value == "BIT" else value


def guess_data_type(address: str, vendor: str) -> str:
    melsec_bit_prefixes = ("STC", "TC", "TS", "CC", "CS", "X", "Y", "M", "L", "F", "B", "SB", "SM")
    keyence_bit_prefixes = ("MR", "LR", "CR", "VB", "X", "Y", "M", "L", "R", "B")
    normalized = address.upper()
    bit_prefixes = keyence_bit_prefixes if vendor == "KEYENCE" else melsec_bit_prefixes
    return "BIT" if any(normalized.startswith(prefix) for prefix in bit_prefixes) else "INT16"


def project_json_bytes(project: dict) -> bytes:
    return json.dumps(without_none(project), ensure_ascii=False, indent=2).encode("utf-8")


def project_qr_json_bytes(project: dict) -> bytes:
    return json.dumps(without_none(project), ensure_ascii=False, separators=(",", ":")).encode("utf-8")


def without_none(value: object) -> object:
    if isinstance(value, dict):
        return {key: without_none(item) for key, item in value.items() if item is not None}
    if isinstance(value, list):
        return [without_none(item) for item in value]
    return value


def project_qr_bytes(project: dict) -> bytes:
    return zstd.ZstdCompressor(level=19).compress(project_qr_json_bytes(project))


def encode_project_chunks(project: dict, chunk_size: int) -> list[QrChunk]:
    safe_chunk_size = max(200, int(chunk_size))
    data = project_qr_json_bytes(project)
    compressed_data = project_qr_bytes(project)
    checksum = hashlib.sha256(data).hexdigest()
    encoded = base64.urlsafe_b64encode(compressed_data).decode("ascii").rstrip("=")
    session = uuid.uuid4().hex[:12]
    payloads = [encoded[i : i + safe_chunk_size] for i in range(0, len(encoded), safe_chunk_size)] or [""]
    total = len(payloads)
    return [
        QrChunk(session=session, index=index + 1, total=total, checksum=checksum, payload=payload)
        for index, payload in enumerate(payloads)
    ]


def decode_chunks(chunks: Iterable[QrChunk]) -> bytes:
    chunk_list = sorted(chunks, key=lambda chunk: chunk.index)
    if not chunk_list:
        raise ValueError("No QR chunks")
    first = chunk_list[0]
    if len(chunk_list) != first.total:
        raise ValueError("Missing QR chunks")
    if any(chunk.session != first.session or chunk.total != first.total or chunk.checksum != first.checksum for chunk in chunk_list):
        raise ValueError("QR chunks do not belong to the same project")
    encoded = "".join(chunk.payload for chunk in chunk_list)
    padding = "=" * (-len(encoded) % 4)
    compressed_data = base64.urlsafe_b64decode(encoded + padding)
    with zstd.ZstdDecompressor().stream_reader(io.BytesIO(compressed_data)) as reader:
        data = reader.read()
    if hashlib.sha256(data).hexdigest() != first.checksum:
        raise ValueError("QR checksum mismatch")
    return data
