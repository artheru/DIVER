#!/usr/bin/env python3
"""Generic add/probe/program workflow for zero, one, or many nodes."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
import subprocess


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
    parser = argparse.ArgumentParser(description="Add zero, one, or many MCU nodes and optionally program them.")
    parser.add_argument("--host", default="http://localhost:4499")
    parser.add_argument("--cli", default=str(Path(__file__).resolve().parents[1] / "agent_cli" / "coral_agent.py"))
    parser.add_argument("--add-uri", action="append", default=[], help="Real node URI. Repeat for multiple nodes.")
    parser.add_argument("--add-simulated", type=int, default=0, help="Number of simulated nodes to add.")
    parser.add_argument("--program", action="append", default=[], help="UUID=LogicName. Repeat for each MCU node; each LogicName may appear once.")
    parser.add_argument("--root-logic", default=None, help="Optional Root logic to configure. Root is not programmed like MCU nodes.")
    parser.add_argument("--start", action="store_true")
    parser.add_argument("--require-all", action="store_true")
    args = parser.parse_args()

    cli = Path(args.cli)
    summary: dict[str, object] = {"added": [], "programmed": [], "steps": []}

    for uri in args.add_uri:
        result = run_cli(cli, args.host, "node", "add", "--uri", uri)
        summary["added"].append(result)

    for index in range(args.add_simulated):
        result = run_cli(cli, args.host, "node", "add-simulated", "--name", f"Simulated Node {index + 1}")
        summary["added"].append(result)

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
        programmed = run_cli(cli, args.host, "node", "program", "--uuid", uuid, "--logic", logic)
        summary["programmed"].append(programmed)

    if args.root_logic is not None:
        summary["steps"].append({
            "rootConfigure": run_cli(cli, args.host, "root", "configure", "--logic", args.root_logic),
            "note": "Root logic is configured on Host. It does not use /api/node/{uuid}/program.",
        })

    if args.start:
        start_args = ["start"]
        if args.require_all:
            start_args.append("--require-all")
        summary["steps"].append({"start": run_cli(cli, args.host, *start_args)})

    try:
        summary["state"] = run_cli(cli, args.host, "state")
    except RuntimeError as ex:
        summary["stateError"] = str(ex)
    print(json.dumps({"ok": True, **summary}, ensure_ascii=True, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
