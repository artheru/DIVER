#!/usr/bin/env python3
"""Coralinker Host Agent CLI.

This tool keeps Agents away from fragile shell quoting and hand-written JSON.
It uses only Python's standard library so it can run from an unpacked docs
bundle without installing packages.
"""

from __future__ import annotations

import argparse
import base64
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path
from typing import Any


DEFAULT_HOST = os.environ.get("CORALINKER_HOST", "http://localhost:4499")

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")


class HostError(RuntimeError):
    def __init__(self, message: str, status: int | None = None, payload: Any = None) -> None:
        super().__init__(message)
        self.status = status
        self.payload = payload


def print_json(value: Any) -> None:
    print(json.dumps(value, ensure_ascii=True, indent=2))


def url_for(host: str, path: str, query: dict[str, Any] | None = None) -> str:
    host = host.rstrip("/")
    if not path.startswith("/"):
        path = "/" + path
    if query:
        clean = {k: v for k, v in query.items() if v is not None}
        if clean:
            path += "?" + urllib.parse.urlencode(clean)
    return host + path


def request_json(host: str, method: str, path: str, body: Any = None, query: dict[str, Any] | None = None) -> Any:
    data = None
    headers = {"Accept": "application/json", "X-Agent-Tool": "coral_agent.py"}
    if body is not None:
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
        headers["Content-Type"] = "application/json; charset=utf-8"
    req = urllib.request.Request(url_for(host, path, query), data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            raw = resp.read()
            if not raw:
                return {"ok": True}
            text = raw.decode("utf-8")
            try:
                return json.loads(text)
            except json.JSONDecodeError:
                return {"ok": True, "text": text}
    except urllib.error.HTTPError as ex:
        raw = ex.read()
        payload: Any
        try:
            payload = json.loads(raw.decode("utf-8")) if raw else None
        except json.JSONDecodeError:
            payload = raw.decode("utf-8", errors="replace")
        raise HostError(f"HTTP {ex.code}: {payload}", ex.code, payload) from ex
    except urllib.error.URLError as ex:
        raise HostError(f"Request failed: {ex}") from ex


def request_bytes(host: str, path: str) -> bytes:
    req = urllib.request.Request(url_for(host, path), headers={"X-Agent-Tool": "coral_agent.py"})
    with urllib.request.urlopen(req, timeout=120) as resp:
        return resp.read()


def require_ok(payload: Any, what: str, require_all: bool = False) -> Any:
    if not isinstance(payload, dict):
        raise HostError(f"{what} returned non-object payload", payload=payload)
    if payload.get("ok") is False:
        raise HostError(f"{what} failed: {payload.get('error') or payload}", payload=payload)
    if require_all and "successNodes" in payload and "totalNodes" in payload:
        if payload.get("successNodes") != payload.get("totalNodes"):
            raise HostError(f"{what} partially failed: {payload}", payload=payload)
    return payload


def snapshot(host: str) -> dict[str, Any]:
    payload = require_ok(request_json(host, "GET", "/api/files/snapshot"), "files snapshot")
    return payload["snapshot"]


def find_snapshot_file(snap: dict[str, Any], path: str) -> dict[str, Any] | None:
    normalized = path.replace("\\", "/")
    for item in snap.get("files", []):
        if item.get("path") == normalized:
            return item
    return None


def cmd_docs_download(args: argparse.Namespace) -> int:
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    if args.bundle:
        data = request_bytes(args.host, "/api/docs/kit/bundle.zip")
        zip_path = out / "kit-docs-bundle.zip"
        zip_path.write_bytes(data)
        with zipfile.ZipFile(zip_path) as zf:
            zf.extractall(out)
        print_json({"ok": True, "out": str(out), "bundle": str(zip_path)})
        return 0

    docs = require_ok(request_json(args.host, "GET", "/api/docs/kit"), "docs index")["docs"]
    for item in docs.get("files", []):
        rel = item["path"]
        text = request_bytes(args.host, "/api/docs/kit/md/" + rel).decode("utf-8")
        dest = out / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_text(text, encoding="utf-8")
    (out / "docs-index.json").write_text(json.dumps(docs, ensure_ascii=False, indent=2), encoding="utf-8")
    print_json({"ok": True, "out": str(out), "files": len(docs.get("files", []))})
    return 0


def cmd_state(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "GET", "/api/agent/state"), "agent state"))
    return 0


def cmd_project_new(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "POST", "/api/project/new", {}), "project new"))
    return 0


def cmd_files_snapshot(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "GET", "/api/files/snapshot"), "files snapshot"))
    return 0


def cmd_files_sync(args: argparse.Namespace) -> int:
    path = args.path.replace("\\", "/")
    if not path.startswith("assets/inputs/") or not path.endswith(".cs"):
        raise HostError("Agent sync path must be assets/inputs/*.cs")
    text = Path(args.from_file).read_text(encoding="utf-8")
    snap = snapshot(args.host)
    item = find_snapshot_file(snap, path)
    payload = {
        "baseHead": snap.get("head"),
        "commitMessage": args.message,
        "force": args.force,
        "changes": [
            {
                "path": path,
                "action": "write",
                "kind": "text",
                "text": text,
                "baseHash": item.get("sha256") if item else None,
            }
        ],
    }
    if args.dry_run:
        preview = dict(payload)
        preview["changes"] = [{**payload["changes"][0], "text": f"<{len(text)} chars>"}]
        print_json({"ok": True, "dryRun": True, "payload": preview})
        return 0
    print_json(require_ok(request_json(args.host, "POST", "/api/files/sync", payload), "files sync"))
    return 0


def cmd_build(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "POST", "/api/build"), "build"))
    return 0


def cmd_node_program(args: argparse.Namespace) -> int:
    payload = {"logicName": args.logic}
    print_json(require_ok(request_json(args.host, "POST", f"/api/node/{args.uuid}/program", payload), "node program"))
    return 0


def cmd_node_add(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "POST", "/api/node/add", {"mcuUri": args.uri}), "node add"))
    return 0


def cmd_node_probe(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "POST", "/api/node/probe", {"mcuUri": args.uri}), "node probe"))
    return 0


def cmd_node_add_simulated(args: argparse.Namespace) -> int:
    payload = {"name": args.name} if args.name else {}
    print_json(require_ok(request_json(args.host, "POST", "/api/node/add-simulated", payload), "node add-simulated"))
    return 0


def cmd_node_info(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "GET", f"/api/node/{args.uuid}"), "node info"))
    return 0


def cmd_node_state(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "GET", f"/api/node/{args.uuid}/state"), "node state"))
    return 0


def cmd_node_list(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "GET", "/api/nodes"), "nodes list"))
    return 0


def cmd_node_states(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "GET", "/api/nodes/state"), "nodes state"))
    return 0


def cmd_node_remove(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "POST", f"/api/node/{args.uuid}/remove", {}), "node remove"))
    return 0


def cmd_start(args: argparse.Namespace) -> int:
    result = request_json(args.host, "POST", "/api/start")
    print_json(result)
    require_ok(result, "start", require_all=args.require_all)
    return 0


def cmd_stop(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "POST", "/api/stop"), "stop"))
    if args.wait_idle:
        deadline = time.time() + args.timeout
        while time.time() < deadline:
            state = require_ok(request_json(args.host, "GET", "/api/session/state"), "session state")
            if state.get("state") == "Idle" or not state.get("isRunning"):
                return 0
            time.sleep(0.25)
        raise HostError("Timed out waiting for session Idle")
    return 0


def cmd_root_configure(args: argparse.Namespace) -> int:
    logic_name = None if args.logic in ("", "null", "none") else args.logic
    print_json(require_ok(request_json(args.host, "POST", "/api/root/configure", {"logicName": logic_name}), "root configure"))
    return 0


def cmd_root_meta(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "GET", "/api/root/control/meta"), "root control meta"))
    return 0


def parse_control_value(value: str) -> Any:
    lowered = value.lower()
    if lowered == "true":
        return True
    if lowered == "false":
        return False
    try:
        if any(ch in value for ch in [".", "e", "E"]):
            return float(value)
        return int(value)
    except ValueError:
        return value


def cmd_root_set_control(args: argparse.Namespace) -> int:
    payload = {"name": args.name, "value": parse_control_value(args.value)}
    print_json(require_ok(request_json(args.host, "POST", "/api/root/control/set", payload), "root control set"))
    return 0


def cmd_logs_node(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "GET", f"/api/logs/node/{args.uuid}", query={"maxCount": args.max}), "node logs"))
    return 0


def cmd_logs_named(args: argparse.Namespace) -> int:
    path = {
        "terminal": "/api/logs/terminal",
        "build": "/api/logs/build",
        "root": "/api/logs/root",
    }[args.kind]
    print_json(require_ok(request_json(args.host, "GET", path), f"{args.kind} logs"))
    return 0


def cmd_variables_get(args: argparse.Namespace) -> int:
    path = {
        "meta": "/api/variables/meta",
        "values": "/api/variables",
        "flow": "/api/variables/flow",
    }[args.kind]
    print_json(require_ok(request_json(args.host, "GET", path), f"variables {args.kind}"))
    return 0


def cmd_variable_set(args: argparse.Namespace) -> int:
    payload: dict[str, Any] = {"name": args.name, "value": parse_control_value(args.value)}
    if args.type:
        payload["typeHint"] = args.type
    print_json(require_ok(request_json(args.host, "POST", "/api/variable/set", payload), "variable set"))
    return 0


def cmd_errors_fatal(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "GET", "/api/errors/fatal"), "fatal errors"))
    return 0


def cmd_logic_list(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "GET", "/api/logic/list"), "logic list"))
    return 0


def cmd_wiretap_enable(args: argparse.Namespace) -> int:
    flags = 0
    if args.rx:
        flags |= 1
    if args.tx:
        flags |= 2
    payload = {"portIndex": args.port, "flags": flags}
    print_json(require_ok(request_json(args.host, "POST", f"/api/node/{args.uuid}/wiretap", payload), "wiretap enable"))
    return 0


def cmd_wiretap_logs(args: argparse.Namespace) -> int:
    print_json(require_ok(request_json(args.host, "GET", f"/api/node/{args.uuid}/wiretap/logs", query={"maxCount": args.max}), "wiretap logs"))
    return 0


def cmd_node_configure_port(args: argparse.Namespace) -> int:
    info = require_ok(request_json(args.host, "GET", f"/api/node/{args.uuid}"), "node info")
    node = info.get("node") or info.get("info") or info
    ports = node.get("portConfigs") or []
    if not ports:
        raise HostError("Node has no portConfigs; read layout first or re-add node.")
    matched = False
    merged = []
    for index, port in enumerate(ports):
        p = dict(port)
        if (args.name and p.get("name") == args.name) or (args.index is not None and index == args.index):
            p["baud"] = args.baud
            matched = True
        merged.append(p)
    if not matched:
        raise HostError("No matching port found")
    payload = {"portConfigs": merged}
    print_json(require_ok(request_json(args.host, "POST", f"/api/node/{args.uuid}/configure", payload), "node configure port"))
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Coralinker Host Agent CLI")
    parser.add_argument("--host", default=DEFAULT_HOST)
    sub = parser.add_subparsers(dest="command", required=True)

    docs = sub.add_parser("docs")
    docs_sub = docs.add_subparsers(dest="docs_command", required=True)
    docs_download = docs_sub.add_parser("download")
    docs_download.add_argument("--out", required=True)
    docs_download.add_argument("--bundle", action="store_true")
    docs_download.set_defaults(func=cmd_docs_download)

    sub.add_parser("state").set_defaults(func=cmd_state)

    project = sub.add_parser("project")
    project_sub = project.add_subparsers(dest="project_command", required=True)
    project_sub.add_parser("new").set_defaults(func=cmd_project_new)

    files = sub.add_parser("files")
    files_sub = files.add_subparsers(dest="files_command", required=True)
    files_sub.add_parser("snapshot").set_defaults(func=cmd_files_snapshot)
    sync = files_sub.add_parser("sync")
    sync.add_argument("--path", required=True)
    sync.add_argument("--from-file", required=True)
    sync.add_argument("--message", required=True)
    sync.add_argument("--force", action="store_true")
    sync.add_argument("--dry-run", action="store_true")
    sync.set_defaults(func=cmd_files_sync)

    sub.add_parser("build").set_defaults(func=cmd_build)

    node = sub.add_parser("node")
    node_sub = node.add_subparsers(dest="node_command", required=True)
    probe = node_sub.add_parser("probe")
    probe.add_argument("--uri", required=True)
    probe.set_defaults(func=cmd_node_probe)
    add = node_sub.add_parser("add")
    add.add_argument("--uri", required=True)
    add.set_defaults(func=cmd_node_add)
    add_sim = node_sub.add_parser("add-simulated")
    add_sim.add_argument("--name")
    add_sim.set_defaults(func=cmd_node_add_simulated)
    info = node_sub.add_parser("info")
    info.add_argument("--uuid", required=True)
    info.set_defaults(func=cmd_node_info)
    state_info = node_sub.add_parser("state")
    state_info.add_argument("--uuid", required=True)
    state_info.set_defaults(func=cmd_node_state)
    node_sub.add_parser("list").set_defaults(func=cmd_node_list)
    node_sub.add_parser("states").set_defaults(func=cmd_node_states)
    remove = node_sub.add_parser("remove")
    remove.add_argument("--uuid", required=True)
    remove.set_defaults(func=cmd_node_remove)
    program = node_sub.add_parser("program")
    program.add_argument("--uuid", required=True)
    program.add_argument("--logic", required=True)
    program.set_defaults(func=cmd_node_program)
    cfg_port = node_sub.add_parser("configure-port")
    cfg_port.add_argument("--uuid", required=True)
    cfg_port.add_argument("--name")
    cfg_port.add_argument("--index", type=int)
    cfg_port.add_argument("--baud", type=int, required=True)
    cfg_port.set_defaults(func=cmd_node_configure_port)

    start = sub.add_parser("start")
    start.add_argument("--require-all", action="store_true")
    start.set_defaults(func=cmd_start)
    stop = sub.add_parser("stop")
    stop.add_argument("--wait-idle", action="store_true")
    stop.add_argument("--timeout", type=float, default=5.0)
    stop.set_defaults(func=cmd_stop)

    root = sub.add_parser("root")
    root_sub = root.add_subparsers(dest="root_command", required=True)
    root_configure = root_sub.add_parser("configure")
    root_configure.add_argument("--logic", required=True)
    root_configure.set_defaults(func=cmd_root_configure)
    root_sub.add_parser("meta").set_defaults(func=cmd_root_meta)
    root_set = root_sub.add_parser("set-control")
    root_set.add_argument("--name", required=True)
    root_set.add_argument("--value", required=True)
    root_set.set_defaults(func=cmd_root_set_control)

    logs = sub.add_parser("logs")
    logs_sub = logs.add_subparsers(dest="logs_command", required=True)
    for log_kind in ["terminal", "build", "root"]:
        parser_named_log = logs_sub.add_parser(log_kind)
        parser_named_log.set_defaults(func=cmd_logs_named, kind=log_kind)
    node_logs = logs_sub.add_parser("node")
    node_logs.add_argument("--uuid", required=True)
    node_logs.add_argument("--max", type=int, default=200)
    node_logs.set_defaults(func=cmd_logs_node)

    variables = sub.add_parser("variables")
    variables_sub = variables.add_subparsers(dest="variables_command", required=True)
    for var_kind in ["meta", "values", "flow"]:
        parser_vars = variables_sub.add_parser(var_kind)
        parser_vars.set_defaults(func=cmd_variables_get, kind=var_kind)
    var_set = variables_sub.add_parser("set")
    var_set.add_argument("--name", required=True)
    var_set.add_argument("--value", required=True)
    var_set.add_argument("--type")
    var_set.set_defaults(func=cmd_variable_set)

    errors = sub.add_parser("errors")
    errors_sub = errors.add_subparsers(dest="errors_command", required=True)
    errors_sub.add_parser("fatal").set_defaults(func=cmd_errors_fatal)

    logic = sub.add_parser("logic")
    logic_sub = logic.add_subparsers(dest="logic_command", required=True)
    logic_sub.add_parser("list").set_defaults(func=cmd_logic_list)

    wiretap = sub.add_parser("wiretap")
    wiretap_sub = wiretap.add_subparsers(dest="wiretap_command", required=True)
    wt_enable = wiretap_sub.add_parser("enable")
    wt_enable.add_argument("--uuid", required=True)
    wt_enable.add_argument("--port", type=int, required=True)
    wt_enable.add_argument("--tx", action="store_true")
    wt_enable.add_argument("--rx", action="store_true")
    wt_enable.set_defaults(func=cmd_wiretap_enable)
    wt_logs = wiretap_sub.add_parser("logs")
    wt_logs.add_argument("--uuid", required=True)
    wt_logs.add_argument("--max", type=int, default=200)
    wt_logs.set_defaults(func=cmd_wiretap_logs)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except HostError as ex:
        print_json({"ok": False, "error": str(ex), "status": ex.status, "payload": ex.payload})
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
