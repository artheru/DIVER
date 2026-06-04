#!/usr/bin/env python3
"""Enable CAN WireTap and collect a short decoded debug snapshot."""

from __future__ import annotations

import argparse
import base64
import json
import subprocess
import sys
import time
from pathlib import Path


def run_cli(cli: Path, host: str, *args: str) -> dict:
    proc = subprocess.run(
        [sys.executable, str(cli), "--host", host, *args],
        text=True,
        encoding="utf-8",
        errors="replace",
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if proc.returncode != 0:
        raise RuntimeError(f"coral_agent failed: {' '.join(args)}\n{proc.stdout}\n{proc.stderr}")
    return json.loads(proc.stdout)


def decode_can_entries(payload: dict) -> list[dict]:
    entries = payload.get("entries") or payload.get("logs") or []
    decoded: list[dict] = []
    for entry in entries:
        msg = entry.get("canMessage") if isinstance(entry, dict) else None
        if not isinstance(msg, dict):
            continue
        raw_data = msg.get("data")
        data_hex = None
        if isinstance(raw_data, str):
            try:
                data_hex = base64.b64decode(raw_data).hex(" ")
            except Exception:
                data_hex = raw_data
        decoded.append(
            {
                "timestamp": entry.get("timestamp"),
                "direction": entry.get("direction"),
                "id": msg.get("id"),
                "dlc": msg.get("dlc"),
                "dataHex": data_hex,
            }
        )
    return decoded


def main() -> int:
    parser = argparse.ArgumentParser(description="Collect CAN WireTap data and a decoded summary.")
    parser.add_argument("--host", default="http://localhost:4499")
    parser.add_argument("--cli", default=str(Path(__file__).resolve().parents[1] / "agent_cli" / "coral_agent.py"))
    parser.add_argument("--uuid", required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--seconds", type=float, default=3.0)
    parser.add_argument("--max", type=int, default=200)
    args = parser.parse_args()

    cli = Path(args.cli)
    enable = run_cli(cli, args.host, "wiretap", "enable", "--uuid", args.uuid, "--port", str(args.port), "--tx", "--rx")
    time.sleep(max(0.1, args.seconds))
    logs = run_cli(cli, args.host, "wiretap", "logs", "--uuid", args.uuid, "--max", str(args.max))
    node_logs = run_cli(cli, args.host, "logs", "node", "--uuid", args.uuid, "--max", str(args.max))
    decoded = decode_can_entries(logs)
    print(json.dumps(
        {
            "ok": True,
            "note": "Confirm CAN port index, bitrate, CAN IDs, and byte order with the user before changing logic.",
            "wiretapEnable": enable,
            "decodedCan": decoded,
            "wiretapLogs": logs,
            "nodeLogs": node_logs,
        },
        ensure_ascii=True,
        indent=2,
    ))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
