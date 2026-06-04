#!/usr/bin/env python3
"""Safe stop -> build -> program -> root configure -> start workflow."""

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
    parser = argparse.ArgumentParser(description="Deploy current build safely to one or more nodes.")
    parser.add_argument("--host", default="http://localhost:4499")
    parser.add_argument("--cli", default=str(Path(__file__).resolve().parents[1] / "agent_cli" / "coral_agent.py"))
    parser.add_argument("--program", action="append", default=[], help="UUID=LogicName. Repeat for each MCU node; each LogicName may appear once.")
    parser.add_argument("--root-logic", default=None, help="Root logic name, or omit to keep current setting.")
    parser.add_argument("--require-all", action="store_true", help="Fail if any node does not start.")
    parser.add_argument("--manual-start", action="store_true", help="Do not call /api/start. Wait for a human to click Start in the web UI.")
    parser.add_argument("--wait-before-state", type=float, default=0.0, help="Seconds to wait before reading state, useful with --manual-start.")
    args = parser.parse_args()

    cli = Path(args.cli)
    summary: dict[str, object] = {"steps": []}

    summary["steps"].append({"stop": run_cli(cli, args.host, "stop", "--wait-idle", "--timeout", "8")})
    summary["steps"].append({"build": run_cli(cli, args.host, "build")})

    program_pairs: list[tuple[str, str]] = []
    logic_to_uuid: dict[str, str] = {}
    for item in args.program:
        if "=" not in item:
            raise RuntimeError("--program must be UUID=LogicName")
        uuid, logic = item.split("=", 1)
        logic_key = logic.strip().lower()
        if logic_key in logic_to_uuid and logic_to_uuid[logic_key] != uuid:
            raise RuntimeError(
                f"LogicName '{logic}' is assigned to multiple nodes ({logic_to_uuid[logic_key]} and {uuid}). "
                "One Logic class can only be programmed to one MCU node; create a separate Logic class per node."
            )
        logic_to_uuid[logic_key] = uuid
        program_pairs.append((uuid, logic))

    for uuid, logic in program_pairs:
        summary["steps"].append({"program": run_cli(cli, args.host, "node", "program", "--uuid", uuid, "--logic", logic)})

    if args.root_logic is not None:
        summary["steps"].append({"root": run_cli(cli, args.host, "root", "configure", "--logic", args.root_logic)})

    if args.manual_start:
        summary["steps"].append({
            "manualStart": {
                "ok": True,
                "message": "Build/program/root configure completed. Waiting for the human operator to click Start in the web UI."
            }
        })
    else:
        start_args = ["start"]
        if args.require_all:
            start_args.append("--require-all")
        summary["steps"].append({"start": run_cli(cli, args.host, *start_args)})
    if args.wait_before_state > 0:
        time.sleep(args.wait_before_state)
    try:
        summary["state"] = run_cli(cli, args.host, "state")
    except RuntimeError as ex:
        summary["stateError"] = str(ex)
    print(json.dumps({"ok": True, **summary}, ensure_ascii=True, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
