#!/usr/bin/env python3
"""Create a three-simulated-node AGV demo with Root joystick control.

This workflow is intentionally opinionated so weaker Agents can run a complete
black-box exercise without hand-writing HTTP code:

new project -> add 3 simulated nodes -> sync one C# file -> build ->
program each MCU node -> configure Root -> start -> set joystick -> dump state.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
from pathlib import Path
from urllib import request


AGV_SOURCE = r'''using CartActivator;
using System;

public class AgvDriveCart : CartDefinition
{
    [AsUpperIO] public int agv_drive_left_cmd;
    [AsUpperIO] public int agv_drive_right_cmd;

    [AsLowerIO] public int agv_drive_left_feedback;
    [AsLowerIO] public int agv_drive_right_feedback;
}

[LogicRunOnMCU(scanInterval = 100)]
public class AgvDriveNodeLogic : LadderLogic<AgvDriveCart>
{
    public override void Operation(int iteration)
    {
        cart.agv_drive_left_feedback = cart.agv_drive_left_cmd;
        cart.agv_drive_right_feedback = cart.agv_drive_right_cmd;
        if (iteration % 10 == 0)
            Console.WriteLine($"DRIVE left={cart.agv_drive_left_feedback} right={cart.agv_drive_right_feedback}");
    }
}

public class AgvSafetyCart : CartDefinition
{
    [AsUpperIO] public int agv_drive_left_cmd;
    [AsUpperIO] public int agv_drive_right_cmd;
    [AsUpperIO] public int agv_safety_brake_cmd;

    [AsLowerIO] public int agv_safety_ok;
    [AsLowerIO] public int agv_safety_zone;
}

[LogicRunOnMCU(scanInterval = 100)]
public class AgvSafetyNodeLogic : LadderLogic<AgvSafetyCart>
{
    public override void Operation(int iteration)
    {
        cart.agv_safety_ok = cart.agv_safety_brake_cmd == 0 ? 1 : 0;
        cart.agv_safety_zone = Math.Abs(cart.agv_drive_left_cmd) + Math.Abs(cart.agv_drive_right_cmd) > 700 ? 2 : 1;
        if (iteration % 15 == 0)
            Console.WriteLine($"SAFETY ok={cart.agv_safety_ok} zone={cart.agv_safety_zone}");
    }
}

public class AgvActuatorCart : CartDefinition
{
    [AsUpperIO] public int agv_actuator_lift_cmd;

    [AsLowerIO] public int agv_actuator_beacon;
    [AsLowerIO] public int agv_actuator_lift_feedback;
}

[LogicRunOnMCU(scanInterval = 100)]
public class AgvActuatorStatusNodeLogic : LadderLogic<AgvActuatorCart>
{
    public override void Operation(int iteration)
    {
        cart.agv_actuator_lift_feedback = cart.agv_actuator_lift_cmd;
        cart.agv_actuator_beacon = iteration % 20 < 10 ? 1 : 0;
        if (iteration % 12 == 0)
            Console.WriteLine($"ACTUATOR beacon={cart.agv_actuator_beacon} lift={cart.agv_actuator_lift_feedback}");
    }
}

public class AgvRootCart : CartDefinition
{
    [AsUpperIO] public int agv_drive_left_cmd;
    [AsUpperIO] public int agv_drive_right_cmd;
    [AsUpperIO] public int agv_safety_brake_cmd;
    [AsUpperIO] public int agv_actuator_lift_cmd;
}

[LogicRunOnRoot(scanInterval = 100)]
public class AgvJoystickRootLogic : RootLogic<AgvRootCart>
{
    [AsControlItem] public int joystickX;
    [AsControlItem] public int joystickY;
    [AsControlItem] public int liftCommand;
    [AsControlItem] public bool emergencyStop;

    private int _iteration;

    public override void Operation()
    {
        var forward = joystickY * 4;
        var turn = joystickX * 4;
        cart.agv_drive_left_cmd = emergencyStop ? 0 : forward - turn;
        cart.agv_drive_right_cmd = emergencyStop ? 0 : forward + turn;
        cart.agv_safety_brake_cmd = emergencyStop ? 1 : 0;
        cart.agv_actuator_lift_cmd = emergencyStop ? 0 : liftCommand;
        _iteration++;
        if (_iteration % 10 == 0)
            Console.WriteLine($"ROOT x={joystickX} y={joystickY} left={cart.agv_drive_left_cmd} right={cart.agv_drive_right_cmd}");
    }
}
'''


def post_json(host: str, path: str, payload: dict) -> dict:
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    req = request.Request(
        host.rstrip("/") + path,
        data=data,
        headers={"Content-Type": "application/json; charset=utf-8", "Accept": "application/json"},
        method="POST",
    )
    with request.urlopen(req, timeout=120) as resp:
        return json.loads(resp.read().decode("utf-8"))


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
    parser = argparse.ArgumentParser(description="Run the official three simulated node AGV Agent demo.")
    parser.add_argument("--host", default="http://localhost:4499")
    parser.add_argument("--cli", default=str(Path(__file__).resolve().parents[1] / "agent_cli" / "coral_agent.py"))
    parser.add_argument("--keep-running", action="store_true", help="Do not stop the session at the end.")
    parser.add_argument("--joystick-x", default="30")
    parser.add_argument("--joystick-y", default="80")
    parser.add_argument("--lift", default="45")
    args = parser.parse_args()

    cli = Path(args.cli)
    summary: dict[str, object] = {"steps": []}

    summary["steps"].append({"newProject": post_json(args.host, "/api/project/new", {})})

    node_programs = [
        ("AGV Drive Node", "AgvDriveNodeLogic"),
        ("AGV Safety Node", "AgvSafetyNodeLogic"),
        ("AGV Actuator Node", "AgvActuatorStatusNodeLogic"),
    ]
    nodes: list[dict] = []
    for name, logic in node_programs:
        added = run_cli(cli, args.host, "node", "add-simulated", "--name", name)
        uuid = added.get("uuid")
        if not uuid:
            raise RuntimeError(f"add simulated node did not return uuid: {added}")
        nodes.append({"name": name, "logic": logic, "uuid": uuid})
    summary["steps"].append({"nodes": nodes})

    with tempfile.TemporaryDirectory() as tmp:
        source_path = Path(tmp) / "AgvAgentWorkflow.cs"
        source_path.write_text(AGV_SOURCE, encoding="utf-8")
        summary["steps"].append({
            "sync": run_cli(
                cli,
                args.host,
                "files",
                "sync",
                "--path",
                "assets/inputs/AgvAgentWorkflow.cs",
                "--from-file",
                str(source_path),
                "--message",
                "add official three simulated node AGV demo",
            )
        })

    summary["steps"].append({"build": run_cli(cli, args.host, "build")})

    for node in nodes:
        summary["steps"].append({
            "program": run_cli(cli, args.host, "node", "program", "--uuid", str(node["uuid"]), "--logic", str(node["logic"]))
        })

    summary["steps"].append({"root": run_cli(cli, args.host, "root", "configure", "--logic", "AgvJoystickRootLogic")})
    summary["steps"].append({"start": run_cli(cli, args.host, "start", "--require-all")})
    summary["steps"].append({"joystickX": run_cli(cli, args.host, "root", "set-control", "--name", "joystickX", "--value", args.joystick_x)})
    summary["steps"].append({"joystickY": run_cli(cli, args.host, "root", "set-control", "--name", "joystickY", "--value", args.joystick_y)})
    summary["steps"].append({"liftCommand": run_cli(cli, args.host, "root", "set-control", "--name", "liftCommand", "--value", args.lift)})

    try:
        summary["state"] = run_cli(cli, args.host, "state")
    except RuntimeError as ex:
        summary["stateError"] = str(ex)
    summary["nodes"] = nodes

    if not args.keep_running:
        summary["steps"].append({"stop": run_cli(cli, args.host, "stop", "--wait-idle", "--timeout", "8")})

    print(json.dumps({"ok": True, **summary}, ensure_ascii=True, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
