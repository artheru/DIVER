# Agent API 调用说明

本文是 **API 参考**，不是 Agent 默认操作教程。

Agent 默认工作方式：

1. 先下载 `<HOST_URI>/api/docs/kit/bundle.zip`。
2. 解压后先读 `tools/README.md`。
3. 优先使用 `tools/agent_cli/coral_agent.py` 和 `tools/workflows/*.py`。
4. 只有现有 CLI/workflow 不覆盖任务时，才直接按本文调用 GET/POST。

不要因为本文列出 GET/POST/JSON，就默认用 curl 或手写 HTTP 脚本完成任务。

Host 地址占位符：

```text
<HOST_URI>
```

`<HOST_URI>` 表示当前 CoralinkerHost 地址，例如用户打开文档时 URL 的 origin。示例中不要写死 localhost，除非用户确认 Host 就运行在本机 4499 端口。

所有 JSON 响应字段使用 camelCase。除特别说明外，成功响应包含 `ok = true`。

## Python 工具入口

下载 bundle 后，工具在解压目录下：

```text
tools/agent_cli/coral_agent.py
tools/workflows/
```

常用命令：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> docs download --out ai-deck/kit-docs --bundle
python tools/agent_cli/coral_agent.py --host <HOST_URI> project new
python tools/agent_cli/coral_agent.py --host <HOST_URI> state
python tools/agent_cli/coral_agent.py --host <HOST_URI> files snapshot
python tools/agent_cli/coral_agent.py --host <HOST_URI> files sync --path assets/inputs/Logic.cs --from-file ./Logic.cs --message "update logic"
python tools/agent_cli/coral_agent.py --host <HOST_URI> build
python tools/agent_cli/coral_agent.py --host <HOST_URI> logic list
python tools/agent_cli/coral_agent.py --host <HOST_URI> node add-simulated --name "Sim Node"
python tools/agent_cli/coral_agent.py --host <HOST_URI> node info --uuid NODE_UUID
python tools/agent_cli/coral_agent.py --host <HOST_URI> node states
python tools/agent_cli/coral_agent.py --host <HOST_URI> node program --uuid NODE_UUID --logic LogicName
python tools/agent_cli/coral_agent.py --host <HOST_URI> root configure --logic RootLogicName
python tools/agent_cli/coral_agent.py --host <HOST_URI> root set-control --name joystickX --value 20
python tools/agent_cli/coral_agent.py --host <HOST_URI> start --require-all
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables flow
python tools/agent_cli/coral_agent.py --host <HOST_URI> logs build
python tools/agent_cli/coral_agent.py --host <HOST_URI> errors fatal
```

CLI 覆盖了大多数常用 API：文档下载、项目新建、状态聚合、文件 snapshot/sync、build、logic list、节点 add/probe/info/state/program/remove、Root configure/control、variables、logs、wiretap、fatal errors。

常用 workflow：

```text
python tools/workflows/deploy_logic.py --host <HOST_URI> --asset-path assets/inputs/Logic.cs --from-file ./Logic.cs --message "deploy logic" --program NODE_UUID=LogicName --root-logic RootLogicName
python tools/workflows/configure_root_and_controls.py --host <HOST_URI> --logic RootLogicName --set joystickX=20 --set joystickY=80
python tools/workflows/agv_three_sim_demo.py --host <HOST_URI>
```

如果你准备写 curl，请先确认：

- `tools/README.md` 没有现成 workflow。
- `coral_agent.py` 没有对应子命令。
- 任务确实需要本文中的底层 API。
- 如果缺少对应 Python CLI/workflow，导致必须用 curl 拼大量文本或 JSON，任务结束时必须按 [09-agent-workflows.md](09-agent-workflows.md) 的反馈协议记录给开发团队，说明缺少哪个命令、临时 curl 做了什么、建议新增的参数和返回字段。

## 文档 API

### 获取文档索引

```text
GET /api/docs/kit
GET /api/docs/kit/resources
GET /api/docs/kit/bundle.zip
```

用途：

- 获取文档版本。
- 获取 Markdown 和 HTML URL。
- 这是底层索引 API；Agent 默认启动流程仍以下载 bundle 和使用 Python 工具为先。
- `resources` 返回文档、examples、stubs、tools 的清单和推荐阅读顺序。
- `bundle.zip` 一次性下载 Markdown、示例、stub、Python CLI/workflows 和 `resources.json`，适合 Agent 本地检索。

Agent 默认应下载 `bundle.zip`，而不是逐个 curl 大量 Markdown。下载后先读 `resources.json` 和 `tools/README.md`，再决定使用哪个 CLI/workflow。

### 读取 Markdown 源文件

```text
GET /api/docs/kit/md/README.md
GET /api/docs/kit/md/09-agent-workflows.md
GET /api/docs/kit/md/10-agent-api.md
```

说明：

- Markdown 链接指向 `.md`。
- 响应包含 ETag 和 Last-Modified。
- 不能使用 bundle 时，Agent 应读取 Markdown 源文件而不是 HTML。

### 人类 HTML 页面

```text
GET /docs/kit/
GET /docs/kit/README.html
```

说明：

- HTML 链接指向 `.html`。
- URL 可带 `?v=版本号` 避免浏览器缓存旧页面。

## Agent 能力

### 能力清单

```text
GET /api/agent/capabilities
```

用途：

- 查询 Host 支持哪些 Agent API。
- 获取推荐轮询间隔。
- 获取文件、构建、节点、调试入口。

### 聚合状态

```text
GET /api/agent/state
POST /api/agent/refresh
```

返回内容：

- project。
- git。
- build 状态和 build log tail。
- session 状态。
- nodes info/state。
- root state。
- variables meta/value/flow。
- terminal/root log tail。
- fatal error。

Agent 在 build、program、start、stop 后都可以调用这个接口，减少遗漏刷新。

## 文件 API

### 新建/清空工程

```text
POST /api/project/new
GET /api/project
```

`POST /api/project/new` 会清空当前节点、输入文件、生成产物和项目状态。Agent 只有在用户明确要求“新建工程”时才调用。

建议流程：

1. `POST /api/project/new`
2. `GET /api/agent/state`
3. `GET /api/files/snapshot`

### 文件快照

```text
GET /api/files/snapshot
```

返回：

```json
{
  "ok": true,
  "snapshot": {
    "head": "git head",
    "shortHead": "abc1234",
    "isDirty": false,
    "dirtyFiles": [],
    "files": [
      {
        "path": "assets/inputs/Logic.cs",
        "kind": "text",
        "sizeBytes": 1234,
        "lastModifiedUtc": "2026-06-03T00:00:00Z",
        "sha256": "..."
      }
    ]
  }
}
```

用途：

- 判断 Host 文件是否变化。
- 获取 `baseHead` 和 `baseHash`。
- 决定是否需要读取文件内容。

### 读取文件

```text
GET /api/files/read?path=assets/inputs/Logic.cs
```

返回文本文件：

```json
{
  "path": "assets/inputs/Logic.cs",
  "kind": "text",
  "text": "using CartActivator;\n...",
  "sizeBytes": 1234
}
```

### 批量同步并提交

```text
POST /api/files/sync
```

Agent 推荐优先使用工具，避免 shell 转义错误：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> files sync --path assets/inputs/Chassis.cs --from-file ./Chassis.cs --message "update chassis logic"
```

请求：

```json
{
  "baseHead": "HEAD_FROM_SNAPSHOT",
  "commitMessage": "update chassis logic",
  "force": false,
  "changes": [
    {
      "path": "assets/inputs/Chassis.cs",
      "action": "write",
      "kind": "text",
      "text": "using CartActivator;\n...",
      "baseHash": "SHA256_FROM_SNAPSHOT"
    }
  ]
}
```

删除文件：

```json
{
  "baseHead": "HEAD_FROM_SNAPSHOT",
  "commitMessage": "remove old logic",
  "force": false,
  "changes": [
    {
      "path": "assets/inputs/OldLogic.cs",
      "action": "delete",
      "baseHash": "SHA256_FROM_SNAPSHOT"
    }
  ]
}
```

成功返回：

```json
{
  "ok": true,
  "headBefore": "...",
  "headAfter": "...",
  "committed": true,
  "changes": []
}
```

冲突返回 HTTP 409：

```json
{
  "ok": false,
  "conflict": true,
  "error": "Remote HEAD changed. Refresh, merge, then retry.",
  "conflicts": [
    {
      "path": "assets/inputs/Chassis.cs",
      "baseHash": "...",
      "currentHash": "...",
      "reason": "Remote file changed since baseHead."
    }
  ]
}
```

限制：

- 只允许写 `assets/inputs/*.cs`。
- 不允许写 `assets/generated`。
- Build 运行中会拒绝写入。
- 旧 `files[]` 格式会返回 HTTP 400。必须使用 `changes[]`。

## Git 历史 API

```text
GET /api/history/status
GET /api/history/log?path=assets/inputs/Logic.cs
GET /api/history/diff?from=HEAD~1&to=HEAD&path=assets/inputs/Logic.cs
GET /api/history/file?commit=HEAD&path=assets/inputs/Logic.cs
POST /api/history/checkout
POST /api/history/revert
```

Agent 常用：

- build 前调用 `/api/history/status`。
- 出错时调用 `/api/history/diff` 理解最近修改。
- 不要随意 checkout/revert，除非用户明确要求。

## Build 和运行 API

### 可靠刷新顺序

当 session 已经 running 时，Agent 不要交错 rebuild/program/start。推荐固定顺序：

```text
POST /api/stop
POST /api/files/sync
POST /api/build
POST /api/node/{uuid}/program
POST /api/root/configure
POST /api/start
POST /api/root/control/set
GET /api/agent/state
```

原因：

- Build 会改变 generated 产物。
- Program 会改变节点变量声明。
- Root configure 会改变 Root virtual node 和 control meta。
- Start 后再设置 control value，变量/flow/log 才能反映最终运行状态。

### Build

```text
POST /api/build
```

成功：

```json
{
  "ok": true,
  "buildId": "20260603-123456",
  "sourceCommit": "...",
  "artifacts": ["FrontLogic", "RearLogic"],
  "rootLogics": ["RootDrive"]
}
```

失败：

```json
{
  "ok": false,
  "error": "Build failed (exit 1).",
  "tail": ["compiler error ..."]
}
```

Build 后刷新：

- `GET /api/logs/build`
- `GET /api/logic/list`
- `GET /api/root/logics`
- `GET /api/files/tree`
- `GET /api/agent/state`

### Start

```text
POST /api/start
```

安全规则：

- 模拟节点任务可以由 Agent 直接调用 Start。
- 真实硬件、车辆运动或危险输出任务不应默认直接调用 Start。
- Agent 应先询问用户启动授权；如果用户选择人类手动 Start，Agent 只完成 build/program/root configure，并等待用户在网页上点击 Start。
- 如果用户允许 Agent 操作，Agent 每次 Start 或设置可能导致运动的 control/UpperIO 前仍应说明将要做什么。
- 启动授权有三种模式：人类手动 Start、每次确认后 Agent Start、本次会话授权 Agent 直接 Start。
- 授权模式必须记录到 `ai-deck/fact.md`；如果该文件不存在，按 bundle 中的 `runtime/fact-template.md` 创建。
- 没有明确授权时，默认人类手动 Start，Agent 不调用 `/api/start`。

成功标准：

- `ok = true`
- `status = "Started"`
- `successNodes = totalNodes`

部分失败：

```json
{
  "ok": false,
  "status": "PartialFailure",
  "sessionRunning": true,
  "totalNodes": 2,
  "successNodes": 1,
  "errors": [
    { "uuid": "...", "nodeName": "Rear Node", "error": "Connect failed" }
  ]
}
```

`sessionRunning = true` 只表示至少一个节点已经运行。Agent 必须把 `PartialFailure` 当失败处理，通常先 stop，再查失败节点。

如果已有节点状态中存在重复 `logicName`，Start 会返回 `400 BadRequest`。Agent 应先重新 program 节点，为每个 MCU 节点分配独立 Logic class。

Start 后刷新：

- `GET /api/session/state`
- `GET /api/nodes/state`
- `GET /api/variables`
- `GET /api/variables/flow`
- `GET /api/errors/fatal`

### Stop

```text
POST /api/stop
```

Stop 后刷新 session/nodes/logs。

## 节点 API

### 真实节点

```text
POST /api/node/probe
POST /api/node/add
POST /api/node/{uuid}/configure
POST /api/node/{uuid}/program
POST /api/node/{uuid}/remove
```

probe/add 请求：

```json
{ "mcuUri": "COM3" }
```

probe/add/node-info 响应除 `version`、`layout` 外，还包含 `abi`（MCU 内置 DIVER 运行时的程序二进制 ABI，SemVer `X.Y.Z`）：

```json
{
  "ok": true,
  "abi": { "hasDiverRuntime": true, "major": 2, "minor": 0, "patch": 0, "semVer": "2.0.0" }
}
```

`hasDiverRuntime=false`（或无 `abi`）表示较旧、不上报 ABI 的固件。主版本不匹配的程序无法在该 MCU 运行，详见 [07-node-management.md](07-node-management.md) 的「DIVER ABI 版本」。

configure 请求：

```json
{
  "nodeName": "Front Node",
  "portConfigs": [
    { "index": 0, "type": "CAN", "name": "CAN-1", "baud": 500000, "retryTimeMs": 10 }
  ],
  "extraInfo": { "x": 100, "y": 200 }
}
```

`portConfigs` 支持局部合并：

- 带 `index` 时按端口 index 更新。
- 不带 `index` 但带 `name` 时按端口名更新。
- 不带 `index/name` 时按数组顺序更新。
- Host 会保留未提交的现有端口配置，Agent 不需要为了改一个波特率回写整张表。

program 请求：

```json
{ "logicName": "FrontLogic" }
```

约束：

- 一个 Logic class 只能编程到一个 MCU 节点。
- 如果同一个 `logicName` 已绑定到其他节点，Host 返回 `400 BadRequest`，并在 `conflict` 字段中指出冲突节点。
- 多节点项目必须为每个 MCU 节点生成独立 `logicName`，即使控制逻辑结构相似，也要复制成不同 class 并使用不同 LowerIO 字段名。

### 模拟节点

```text
POST /api/node/add-simulated
```

请求：

```json
{ "name": "Sim Front Node" }
```

成功后节点 `mcuUri` 以 `sim://` 开头。模拟节点也使用 configure/program/start/stop/remove。

### 节点查询

```text
GET /api/node/{uuid}
GET /api/node/{uuid}/state
GET /api/nodes
GET /api/nodes/state
GET /api/nodes/export
POST /api/nodes/import
POST /api/nodes/clear
```

`POST /api/nodes/import` 会拒绝导入重复 `logicName` 的节点集合。每个 MCU 节点必须使用独立 Logic class。

节点状态中包含：

- 连接状态。
- 运行状态。
- 是否已编程。
- 端口 TX/RX frame/byte 统计。
- DI/DO 状态。

## Root Runtime API

```text
GET /api/root/logics
POST /api/root/configure
GET /api/root/state
GET /api/root/control/meta
POST /api/root/control/set
```

Root Logic 用于 Host 侧上层控制。它可以读 ControlItem，写 UpperIO。

Root 不是 MCU 节点。不要调用 `/api/node/{uuid}/program` 给 Root “编程”。Build 发现 Root Logic 并生成 metadata；`/api/root/configure` 只是选择和绑定 Host 侧 Root Logic。

配置 Root Logic 请求体：

```json
{ "logicName": "DiffDriveRoot" }
```

清空 Root Logic 请求体：

```json
{ "logicName": null }
```

注意：字段名必须是 `logicName`。如果写成其他字段，Host 会返回 400，不会清空现有 Root 配置。

读取 Root ControlItem：

```text
GET /api/root/control/meta
```

设置 Root ControlItem：

```json
POST /api/root/control/set
{ "name": "joystickX", "value": 0.25 }
```

工具示例：

```text
python tools/workflows/configure_root_and_controls.py --host <HOST_URI> --logic DiffDriveRoot --set joystickX=0.25 --set joystickY=0.8
```

## Variables API

```text
GET /api/variables/meta
GET /api/variables
GET /api/variables/flow
GET /api/variable/{name}
POST /api/variable/set
```

设置变量：

```json
{
  "name": "targetSpeed",
  "value": 100,
  "typeHint": "int"
}
```

Variables Flow 返回：

- nodes：Root 和节点实例。
- variables：变量名、类型、方向、值、sourceIds、readerIds、writerIds。

Agent 用 flow 判断：

- 哪个节点写变量。
- 哪个节点读变量。
- Root 是否参与变量流。
- 每个 `logicName` 是否只绑定到一个节点，避免同一 Logic 的 LowerIO 被多个节点同时写入。

## 日志 API

```text
GET /api/logs/terminal
GET /api/logs/build
GET /api/logs/root
GET /api/logs/nodes
GET /api/logs/node/{uuid}?afterSeq=0&maxCount=200
POST /api/logs/terminal/clear
POST /api/logs/root/clear
POST /api/logs/node/{uuid}/clear
POST /api/logs/clear
```

用途：

- Build 失败读 build log。
- Start/Stop 或节点操作失败读 terminal log。
- 逻辑 `Console.WriteLine` 输出读 node log。
- Root Logic 输出读 root log。

## WireTap API

```text
POST /api/node/{uuid}/wiretap
GET /api/node/{uuid}/wiretap
GET /api/wiretap/configs
GET /api/node/{uuid}/wiretap/logs
GET /api/wiretap/logs
GET /api/node/{uuid}/wiretap/export
```

开启监听：

```json
{
  "portIndex": 255,
  "flags": 3
}
```

说明：

- `portIndex = 255` 表示全部端口。
- WireTap 可查看串口和 CAN 的 TX/RX。
- CAN 日志包含 id、dlc、rtr、payload。
- Serial 日志包含 rawData。

## 错误监控 API

```text
GET /api/errors/fatal
POST /api/errors/fatal/clear
GET /api/runtime/diver-map/{logicName}
```

Agent 运行期间应定期读取 `/api/errors/fatal`。如果出现错误：

1. 找到 `uuid`。
2. 读取节点信息，确定 `logicName`。
3. 读取 `/api/runtime/diver-map/{logicName}`。
4. 将错误中的 offset 或源位置映射回用户代码。
5. 修改代码后重新 sync、build、program、start。

## 设备发现和产物 API

```text
GET /api/ports
GET /api/logic/list
GET /api/runtime/diver-map/{logicName}
```

`/api/logic/list` 用于多节点编程前列出可编程逻辑。
