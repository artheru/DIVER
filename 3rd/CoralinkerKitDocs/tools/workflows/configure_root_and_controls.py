#!/usr/bin/env python3
"""Configure Root logic and optionally set Root ControlItem values.

Root logic is Host-side .NET logic. It is discovered during build and bound by
POST /api/root/configure. It is not programmed with /api/node/{uuid}/program.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
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
    parser = argparse.ArgumentParser(description="Configure Root logic and set ControlItem values.")
    parser.add_argument("--host", default="http://localhost:4499")
    parser.add_argument("--cli", default=str(Path(__file__).resolve().parents[1] / "agent_cli" / "coral_agent.py"))
    parser.add_argument("--logic", required=True, help="Root logic name, or null/none to clear.")
    parser.add_argument("--set", action="append", default=[], help="ControlName=Value. Repeat as needed.")
    args = parser.parse_args()

    cli = Path(args.cli)
    summary: dict[str, object] = {
        "note": "Root logic is configured, not node-programmed. Build creates Root metadata; configure binds it.",
        "steps": [],
    }
    summary["steps"].append({"configure": run_cli(cli, args.host, "root", "configure", "--logic", args.logic)})
    summary["steps"].append({"meta": run_cli(cli, args.host, "root", "meta")})

    for item in args.set:
        if "=" not in item:
            raise RuntimeError("--set must be ControlName=Value")
        name, value = item.split("=", 1)
        summary["steps"].append({"setControl": run_cli(cli, args.host, "root", "set-control", "--name", name, "--value", value)})

    summary["state"] = run_cli(cli, args.host, "state")
    print(json.dumps({"ok": True, **summary}, ensure_ascii=True, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
