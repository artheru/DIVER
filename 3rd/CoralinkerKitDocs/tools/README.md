# Agent Tools

These tools are distributed with the Kit docs so Agents can call Host APIs
without fragile shell quoting or hand-written JSON.

## CLI

Use `agent_cli/coral_agent.py` for common operations. Agents should prefer this
CLI over `curl` for file sync, build, program, Root configure, logs, and WireTap.
Use `<HOST_URI>` for the current CoralinkerHost origin; do not hardcode
localhost unless the user confirms it.

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> docs download --out ai-deck/kit-docs --bundle
python tools/agent_cli/coral_agent.py --host <HOST_URI> project new
python tools/agent_cli/coral_agent.py --host <HOST_URI> state
python tools/agent_cli/coral_agent.py --host <HOST_URI> files sync --path assets/inputs/Logic.cs --from-file ./Logic.cs --message "update logic"
python tools/agent_cli/coral_agent.py --host <HOST_URI> build
python tools/agent_cli/coral_agent.py --host <HOST_URI> node add-simulated --name "Sim Node"
python tools/agent_cli/coral_agent.py --host <HOST_URI> node program --uuid NODE_UUID --logic LogicName
python tools/agent_cli/coral_agent.py --host <HOST_URI> root configure --logic RootLogicName
python tools/agent_cli/coral_agent.py --host <HOST_URI> start --require-all
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables flow
python tools/agent_cli/coral_agent.py --host <HOST_URI> logs node --uuid NODE_UUID
python tools/agent_cli/coral_agent.py --host <HOST_URI> errors fatal
```

## Workflows

Workflows are generic. They support zero, one, or many MCU nodes depending on
which repeated arguments you pass. Each MCU node must use a distinct LogicName;
one Logic class cannot be programmed to multiple nodes because LowerIO fields
would conflict.

| Script | Use |
| --- | --- |
| `workflows/deploy_logic.py` | Upload one local C# file, build, program nodes with distinct LogicNames, optionally configure Root, then start. |
| `workflows/safe_stop_build_program_start.py` | Deploy an already-synced project safely. Use `--manual-start` for real hardware when the human should click Start. |
| `workflows/add_nodes_and_program.py` | Add real/simulated nodes and optionally program them with distinct LogicNames. |
| `workflows/agv_three_sim_demo.py` | Official end-to-end demo: new project, 3 simulated MCU nodes, distinct AGV logic, Root joystick control, build/program/start/verify. |
| `workflows/configure_root_and_controls.py` | Configure Host-side Root logic and set ControlItem values. Root is not MCU-programmed. |
| `workflows/debug_serial.py` | Debug Serial WireTap for RS232 or RS485. |
| `workflows/debug_can_wiretap.py` | Debug CAN WireTap and show a basic decoded summary. |

## Root Logic Difference

MCU nodes use the CLI wrapper:

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> node program --uuid NODE_UUID --logic LogicName
```

Root logic uses the Root CLI commands:

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> root configure --logic RootLogicName
python tools/agent_cli/coral_agent.py --host <HOST_URI> root meta
python tools/agent_cli/coral_agent.py --host <HOST_URI> root set-control --name joystickX --value 20
```

Do not call node program for Root. Build creates Root metadata, and configure
binds that Host-side logic.

## Agent Behavior Rules

Use `ai-deck/kit-docs/` for downloaded docs and tools. Use
`ai-deck/agent_work/<YYYYMMDD-HHMMSS-task>/` for temporary drafts, generated
scripts, response samples, and intermediate files. Use `ai-deck/agent_feedback/`
for feedback to developers. Every Agent-created working directory should include
a `desc.md` describing the directory and files.

For real hardware facts confirmed with the user, update `ai-deck/fact.md`.
Record device models, wiring, polarity, ports, baud rates, payload/protocol
details, load, mounting position, gear ratio, max speed, vehicle configuration,
application scenario, cycle time, safety limits, and open questions.

Before writing custom code, check this directory first:

1. Use a workflow if it matches the task.
2. Use `agent_cli/coral_agent.py` if no workflow matches.
3. Use direct HTTP only for an API not exposed by the CLI.
4. Write temporary Python only when both workflow and CLI are insufficient.

Do not use curl to upload large source text or build escaped JSON by hand.
For sync, use:

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> files sync --path assets/inputs/Logic.cs --from-file ./Logic.cs --message "update logic"
```

For real hardware that may move a vehicle or actuator, do not default to auto
Start. Ask the user for startup authorization. If the human will click Start in
the web UI, use:

```text
python tools/workflows/safe_stop_build_program_start.py --host <HOST_URI> --manual-start --wait-before-state 30
```

For a complete AGV smoke test, use:

```text
python tools/workflows/agv_three_sim_demo.py --host <HOST_URI>
```

## Feedback To Developers

After a non-trivial task, record issues and suggestions for the CoralinkerHost
developers. If the local workspace has an `ai-deck` directory, write feedback to:

```text
ai-deck/agent_feedback/YYYYMMDD-HHMMSS-brief-topic.md
```

Include:

- What task you attempted.
- Which workflow/CLI/API you used.
- What worked.
- What was confusing.
- Exact tool/API problems and reproduction steps.
- Suggested documentation, API, or workflow improvements.

Also summarize the most important findings in your final response to the user.
