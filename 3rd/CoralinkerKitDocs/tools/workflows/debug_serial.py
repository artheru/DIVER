#!/usr/bin/env python3
"""Enable Serial WireTap and collect a short debug snapshot.

Use this for RS232 and RS485 ports. The port index must come from the node
layout shown by Host, not from guessing.
"""

from __future__ import annotations

import argparse
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


def main() -> int:
    parser = argparse.ArgumentParser(description="Collect serial WireTap and node log data.")
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
    state = run_cli(cli, args.host, "state")
    print(json.dumps(
        {
            "ok": True,
            "note": "Serial means RS232 or RS485. Confirm wiring, baud rate, and TX/RX direction with the user.",
            "wiretapEnable": enable,
            "wiretapLogs": logs,
            "nodeLogs": node_logs,
            "state": state,
        },
        ensure_ascii=True,
        indent=2,
    ))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
