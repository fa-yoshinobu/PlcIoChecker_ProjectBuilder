from __future__ import annotations

import base64
import hashlib
import json
import re
import time
import uuid
from dataclasses import dataclass
from typing import Iterable


QR_PREFIX = "PLCIOC1"
VALID_TRAP_CONDITIONS = {
    "Rise",
    "Fall",
    "Change",
    "GreaterOrEqual",
    "LessOrEqual",
    "Equal",
    "NotEqual",
}


@dataclass(frozen=True)
class QrChunk:
    session: str
    index: int
    total: int
    checksum: str
    payload: str

    def text(self) -> str:
        return f"{QR_PREFIX}|{self.session}|{self.index}|{self.total}|{self.checksum}|{self.payload}"


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
    name: str,
    vendor: str,
    connection_mode: str,
    host: str,
    port: int,
    monitor_interval_ms: int,
    timeout_ms: int,
    machine_label: str,
    keyence_device_mode: str,
    transport_mode: str,
    network: int,
    station: int,
    module_io: int,
    multidrop: int,
    devices_text: str,
    watch_text: str,
    traps_text: str,
    block_display_density: str = "Compact",
) -> dict:
    now_ms = int(time.time() * 1000)
    project_id = f"{slugify(name)}-{now_ms}"
    devices = []
    for line in parse_lines(devices_text):
        parts = [part.strip() for part in line.split(",")]
        if not parts or not parts[0]:
            continue
        address = parts[0].upper()
        data_type = parts[1] if len(parts) > 1 and parts[1] else guess_data_type(address)
        devices.append({"address": address, "dataType": data_type})

    watch_items = [line.upper() for line in parse_lines(watch_text)]

    traps = []
    for offset, line in enumerate(parse_lines(traps_text), start=1):
        parts = [part.strip() for part in line.split(",")]
        if len(parts) < 2 or not parts[0] or not parts[1]:
            continue
        threshold = None
        if len(parts) >= 3 and parts[2]:
            threshold = float(parts[2])
        enabled = True
        if len(parts) >= 4 and parts[3]:
            enabled = parts[3].lower() not in {"0", "false", "off", "no"}
        traps.append(
            {
                "id": f"trap-{offset}",
                "address": parts[0].upper(),
                "enabled": enabled,
                "condition": validate_trap_condition(parts[1]),
                "threshold": threshold,
                "triggerCount": 0,
                "lastTriggeredAtEpochMs": None,
                "lastObservedValue": None,
            }
        )

    return {
        "id": project_id,
        "name": name,
        "connection": {
            "vendor": vendor,
            "connectionMode": connection_mode,
            "host": host,
            "port": port,
            "monitorIntervalMs": monitor_interval_ms,
            "timeoutMs": timeout_ms,
            "machineLabel": machine_label,
            "keyenceDeviceMode": keyence_device_mode,
            "transportMode": transport_mode,
            "network": network,
            "station": station,
            "moduleIo": module_io,
            "multidrop": multidrop,
        },
        "devices": devices,
        "watchItems": watch_items,
        "traps": traps,
        "settings": {"blockDisplayDensity": block_display_density},
        "updatedAtEpochMs": now_ms,
    }


def validate_trap_condition(text: str) -> str:
    value = text.strip()
    if value not in VALID_TRAP_CONDITIONS:
        allowed = ", ".join(sorted(VALID_TRAP_CONDITIONS))
        raise ValueError(f"Invalid trap condition: {value}. Use one of: {allowed}")
    return value


def guess_data_type(address: str) -> str:
    bit_prefixes = ("X", "Y", "M", "L", "F", "B", "SB", "SM", "STC", "TC", "TS", "CC", "CS", "R", "MR", "LR", "CR", "VB")
    normalized = address.upper()
    return "Bit" if any(normalized.startswith(prefix) for prefix in bit_prefixes) else "Int16"


def project_json_bytes(project: dict) -> bytes:
    return json.dumps(project, ensure_ascii=False, indent=2).encode("utf-8")


def project_qr_bytes(project: dict) -> bytes:
    return json.dumps(project, ensure_ascii=False, separators=(",", ":")).encode("utf-8")


def encode_project_chunks(project: dict, chunk_size: int) -> list[QrChunk]:
    safe_chunk_size = max(200, int(chunk_size))
    data = project_qr_bytes(project)
    checksum = hashlib.sha256(data).hexdigest()
    encoded = base64.urlsafe_b64encode(data).decode("ascii").rstrip("=")
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
    data = base64.urlsafe_b64decode(encoded + padding)
    if hashlib.sha256(data).hexdigest() != first.checksum:
        raise ValueError("QR checksum mismatch")
    return data
