# Agent 固定工作流

本文给 Cursor 和其他 Agents 使用。不要跳步，不要靠猜测操作 Host。

`<HOST_URI>` 表示当前 CoralinkerHost 地址，例如用户提供的文档 URL 的 origin。不要把示例中的 `<HOST_URI>` 固定替换成 localhost，除非用户确认 Host 就运行在本机 4499 端口。

## 总规则

- 先用 `python tools/agent_cli/coral_agent.py --host <HOST_URI> docs download --out ai-deck/kit-docs --bundle` 下载并解压文档包。
- 解压后先读 `resources.json`、`README.md`、`09-agent-workflows.md`、`10-agent-api.md`、`tools/README.md`。
- 只有 bundle 下载失败时，才逐个读取 `/api/docs/kit/md/...`。不要默认用大量 curl 逐个读取章节。
- 先运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> state` 读取聚合状态。
- 优先使用 bundle 中的 `tools/agent_cli/coral_agent.py` 和 `tools/workflows/*.py`；不要为常见流程手写临时 Python 或 curl 串。
- 先同步文件状态，再修改代码。
- Agent 自己产生的临时文件只放在 `ai-deck/` 下：教程和工具放 `ai-deck/kit-docs/`，任务草稿放 `ai-deck/agent_work/<YYYYMMDD-HHMMSS-task>/`，反馈放 `ai-deck/agent_feedback/`。
- 每个 Agent 自建目录都应有 `desc.md`，说明目录用途、任务目标和文件用途。
- 修改代码只写 `assets/inputs/*.cs`。
- 修改完成后用 `python tools/agent_cli/coral_agent.py ... files sync` 推送并提交。
- 不要手写 shell 转义后的 sync JSON 字符串；优先使用 `tools/agent_cli/coral_agent.py files sync` 或用语言内置 JSON 序列化。
- build 前必须确认 Git status 干净。
- build、program、start 后必须刷新状态。
- 失败时先读对应日志，再改代码或配置。
- 不要直接修改网页编辑器。
- 不要直接修改 `assets/generated`。
- 不要依赖 SignalR 判断任务完成。
- 用户在 Prompt 或后续消息中补充的实际任务和硬件连接信息优先于示例代码。
- 涉及真实硬件时，不要猜测灯、按键、驱动器、传感器、端口、协议、电平、方向和安全限制。
- 如果用户补充信息不足以安全编程、编程节点或验证结果，先向用户追问，再继续操作。
- 涉及真实硬件、车辆运动或危险输出时，不要默认自动 Start 或写入会造成运动的 control/UpperIO。先询问用户希望“人类手动 Start”还是“授权 Agent 直接操作”。
- 向用户确认过的硬件事实必须整理到 `ai-deck/fact.md`，后续 Agent 应优先读取该文件，不要重复追问已确认事实。

## Agent 对用户的透明度要求

用户可能不了解 Host、Root、MCU 节点、变量和 WireTap 的关系。Agent 不能把关键动作藏起来。

每次任务都必须向用户说明：

- 准备做什么：计划、会读取哪些文档、会使用哪些工具/API。
- 正在做什么：新建工程、同步文件、build、program、root configure、start、set control、读取日志等关键阶段。
- 做完了什么：成功/失败结果、关键返回字段、验证证据。
- 接下来做什么：下一步动作，或者需要用户补充的硬件/协议/安全限制。

不要只在最后给结论。遇到失败时，说明当前证据和排查方向。

## 工具优先规则

Agent 应把 Host 文档包视为工具包，而不只是文字说明。

推荐启动命令：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> docs download --out ai-deck/kit-docs --bundle
```

下载后在 `ai-deck/kit-docs/` 中读取文档和执行工具。工具位置是固定的：

| 工具 | 用途 | 何时使用 |
| --- | --- | --- |
| `tools/agent_cli/coral_agent.py` | 通用 CLI，封装 docs、files sync、build、node program、root configure、logs、wiretap | 默认优先使用 |
| `tools/workflows/deploy_logic.py` | 同步一个 C# 文件、build、program 多节点、可选 Root、start | 用户给了本地逻辑文件时 |
| `tools/workflows/safe_stop_build_program_start.py` | stop -> build -> program -> root configure -> start，或 `--manual-start` 等待人类点击 Start | 工程文件已同步，只需要安全部署时 |
| `tools/workflows/add_nodes_and_program.py` | 添加真实/模拟节点，并可选 program | 节点管理和批量编程 |
| `tools/workflows/configure_root_and_controls.py` | 配置 Root Logic，读取/设置 Root ControlItem | Root、joystick、遥控任务 |
| `tools/workflows/agv_three_sim_demo.py` | 官方三模拟节点 AGV smoke test | 验证 Agent 编程系统或演示完整流程 |
| `tools/workflows/debug_serial.py` | Serial WireTap 调试，覆盖 RS232/RS485 | 串口调试 |
| `tools/workflows/debug_can_wiretap.py` | CAN WireTap 调试和基础解码 | CAN 调试 |

常见命令：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> state
python tools/agent_cli/coral_agent.py --host <HOST_URI> files sync --path assets/inputs/Logic.cs --from-file ./Logic.cs --message "update logic"
python tools/workflows/agv_three_sim_demo.py --host <HOST_URI>
```

常见任务优先顺序：

1. 先看 `tools/README.md`。
2. 有现成 workflow 就用 workflow。
3. 没有 workflow 时，用 `agent_cli/coral_agent.py` 子命令组合。
4. 只有 CLI 缺少能力时，才直接调用 HTTP API。
5. 只有非常特殊的任务，才写临时脚本。

禁止行为：

- 为 sync 手写 shell 转义 JSON。
- 明明有 `files sync`，仍用 curl 拼接大段 `text`。
- 明明有 workflow，仍从零手写完整部署脚本。
- 逐个 curl 大量 Markdown，而不是下载 bundle。

## Agent 反馈协议

Agent 不只是执行任务，也要帮助开发团队发现文档、工具和 API 的问题。每次复杂任务结束时，都要主动输出并记录反馈。

需要主动发现：

- 文档是否让你先下载 bundle、先用工具，而不是诱导你写 curl。
- `tools/README.md` 是否足以让你找到正确 workflow。
- 是否有常用流程缺少 workflow。
- Root 与 MCU program 的边界是否清楚。
- API 错误信息是否足以定位问题。
- Build warning 是否容易被误判为失败。
- 状态刷新顺序是否明确。
- 用户是否能理解你准备做什么、已经做了什么、下一步是什么。

如果运行环境有本地工作目录，反馈应写入：

```text
ai-deck/agent_feedback/YYYYMMDD-HHMMSS-brief-topic.md
```

## Agent 临时工作区

如果任务需要暂存代码、生成脚本、保存 API 返回样本或记录中间分析，使用：

```text
ai-deck/agent_work/YYYYMMDD-HHMMSS-brief-task/
```

该目录必须包含：

```text
desc.md
```

`desc.md` 应说明：

- 本目录服务哪个用户任务。
- 每个文件的用途。
- 哪些文件只是临时草稿，不应同步到 Host。
- 最终同步到 `assets/inputs/*.cs` 的正式文件路径。

不要把临时代码直接放在仓库根目录。不要把 `ai-deck/agent_work` 当作 Host 正式源码目录。

如果该目录不存在，先创建：

```text
ai-deck/agent_feedback/desc.md
```

`desc.md` 内容应说明本目录用途：

```text
# agent_feedback

记录 Agent 在使用 CoralinkerHost 文档、API、CLI、workflow 时发现的问题、建议和复现步骤。
```

反馈文件模板：

```markdown
# Agent Feedback - <task name>

## Task
用户要求和目标。

## What Worked
成功完成的流程、使用的工具、关键证据。

## Confusing Points
不清楚、容易误解、需要多处文档拼起来理解的地方。

## Tool/API Issues
具体 API、CLI、workflow 的问题，包含 URL/命令、返回、预期、实际。

## Suggestions
建议新增或修改的文档、工具、workflow、错误提示。

## Reproduction
最短复现步骤。
```

反馈给开发团队的方式：

- 在最终回复中列出反馈文件路径。
- 同时在最终回复中总结最重要的 3-5 个问题和建议。
- 如果问题阻塞任务，先向用户说明阻塞和临时绕过方案。
- 不要只把问题写在本地文件里而不告诉用户。

## 用户补充输入和硬件信息

Agent 通常只从文档入口知道系统能力，不知道客户现场实际硬件。用户后续补充的内容是任务的一部分，必须纳入实现。

如果任务只使用模拟节点，Agent 可以按文档自动 start 并验证，因为不会驱动真实设备。只要任务涉及真实节点、真实驱动器、车辆运动、执行器动作或危险输出，Agent 必须先确认启动授权。

启动授权问题建议使用：

```text
后续调试如果涉及真实硬件启动、车辆运动或危险输出，你希望我采用哪种方式？
1. 我只完成 build/program/root configure，然后等待你在网页上手动点击 Start；我不直接启动。
2. 每次需要 Start 或写入可能导致运动的控制量前，我先说明风险并询问你确认，得到确认后再操作。
3. 本次会话你授权我直接 Start 和设置控制量；我仍会在操作前说明准备做什么，并在操作后报告结果。
```

如果用户没有回答，默认使用选项 1。

启动授权规则：

- 只使用模拟节点、不会驱动真实设备时，Agent 可以自动调用 `/api/start` 并设置 control 值进行验证。
- 涉及真实节点、真实驱动器、车辆运动、执行器动作、继电器、灯光、蜂鸣器、夹具、升降、转向或其他危险输出时，必须先确认启动授权。
- 选项 1（人类手动 Start）：Agent 只能执行到 build、program、root configure；不得调用 `/api/start`，不得写入会导致运动的 control/UpperIO；完成后提示用户在网页点击 Start，并等待用户反馈或等待指定秒数后读取状态。
- 选项 2（每次确认）：每次调用 `/api/start`、`root set-control`、`variable set` 或任何可能导致运动/输出变化的操作前，Agent 都要说明将要写入的值、风险和停止方式，用户确认后才能执行。
- 选项 3（本次授权）：Agent 可以在本次会话中直接执行 Start 和控制量写入，但每次操作前仍要简短说明计划，操作后报告结果；遇到异常、超限或安全状态变化时立即停止并报告。
- 启动授权模式是现场事实，确认后必须写入 `ai-deck/fact.md`。
- 如果 `ai-deck/fact.md` 已记录启动授权模式，后续 Agent 应按该模式执行；用户新的明确指令可以覆盖旧记录，但必须更新 `fact.md`。
- 如果启动授权不明确，默认选项 1。

真实硬件任务至少确认：

- 连接了哪些设备，例如灯、按键、电机驱动器、传感器。
- 每个设备接在哪个节点、哪个端口、哪个通道。
- 串口/CAN/IO 参数，例如波特率、CAN ID、电平有效方向、payload 格式。
- 设备型号、控制协议、报文格式、寄存器/对象字典、字节序和缩放系数。
- 负载重量、安装位置、减速比、轮径、最大速度、最大加速度、最大舵角、机械限位。
- 车辆构型、应用场景、运行节拍、作业流程、人机协作边界。
- 控制变量和反馈变量的命名。
- 安全限制，例如速度范围、急停条件、默认输出、上电状态。
- 验证方式，例如看哪个变量、日志、IO 状态或 WireTap 记录。

确认后的事实必须写入：

```text
ai-deck/fact.md
```

`fact.md` 应只记录已确认事实和待确认问题，不写猜测。建议按设备、节点、端口、协议、安全、运动学、验证方式分节整理。更新后在回复中说明新增或修改了哪些事实。

如果 `ai-deck/fact.md` 不存在，先用 bundle 中的 `runtime/fact-template.md` 创建，再填入确认内容。

如果这些信息缺失，不要自行编造。应向用户提出具体问题，例如：

```text
请补充电机驱动器连接在哪个节点和端口？CAN ID、波特率、左右轮速度 payload 格式是什么？速度范围和急停条件是什么？
```

## 工作流：读取系统和文件

1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> docs download --out ai-deck/kit-docs --bundle`。
1. 读取 `resources.json` 的 `recommendedReadOrder`。
1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> state`。
1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> files snapshot`。
1. 只对需要的文件读取本地工作副本；必须读 Host 文件时才使用底层 files read API。

成功标准：

- `snapshot.head` 有值，或当前工程还没有提交历史。
- `snapshot.files` 中能看到 `assets/inputs` 和 `assets/generated`。

失败处理：

- 如果读不到文档，检查 Host 地址和 `/api/ping`。
- 如果文件不存在，先创建输入文件，不要写 generated。

## 工作流：修改代码并提交

推荐使用工具，不要用手工字符串拼接生成 JSON：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> files sync --path assets/inputs/MotorLogic.cs --from-file ./MotorLogic.cs --message "update motor control logic"
```

工具会自动读取 snapshot、填入 `baseHead` 和 `baseHash`，并用 Python JSON 序列化发送请求。

如果必须直接调用 HTTP：

1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> files snapshot`，保存 `head` 和每个文件 `sha256`。
2. 读取要修改的文件。
3. 本地生成修改。
4. 用 JSON 序列化库生成请求体，调用底层 sync API。优先不要走这条路，除非 CLI 无法满足任务。

示例：

```json
{
  "baseHead": "HEAD_HASH_FROM_SNAPSHOT",
  "commitMessage": "update motor control logic",
  "force": false,
  "changes": [
    {
      "path": "assets/inputs/MotorLogic.cs",
      "action": "write",
      "kind": "text",
      "text": "using CartActivator;\n...",
      "baseHash": "SHA256_FROM_SNAPSHOT"
    }
  ]
}
```

成功标准：

- `ok = true`。
- `committed = true` 或文件内容没有变化。
- `headAfter` 是新的提交。

冲突处理：

- 如果返回 409，读取 `conflicts`。
- 重新运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> files snapshot`。
- 重新读取冲突文件。
- 合并后再次运行 `python tools/agent_cli/coral_agent.py ... files sync`。
- 只有用户明确要求覆盖时才设置 `force = true`。
- 如果返回 400 且提示 `legacy files[]` 或 `Unsupported sync format`，说明用了旧格式；改用 `changes[]` 或 Python CLI。

## 工作流：Build

1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> files snapshot` 确认输入文件状态。
1. 如果有未同步文件，先用 `files sync` 保存并提交。
1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> build`。
1. Build 返回后调用：
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> logs build`
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> logic list`
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> state`

成功标准：

- `ok = true`。
- `artifacts` 中包含需要编程到节点的 `logicName`。
- 如果有 Root Logic，`rootLogics` 中包含需要配置的 Root logic。

失败处理：

- 读取 build 返回的 `tail`。
- 再运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> logs build`。
- 根据错误修改 `assets/inputs/*.cs`，重新 sync 和 build。

如果当前 session 已经 running，使用更保守的顺序：

```text
python tools/workflows/deploy_logic.py --host <HOST_URI> --asset-path assets/inputs/Logic.cs --from-file ./Logic.cs --message "deploy logic" --program NODE_UUID=LogicName --root-logic RootLogicName --require-all
python tools/agent_cli/coral_agent.py --host <HOST_URI> root set-control --name joystickX --value 0
python tools/agent_cli/coral_agent.py --host <HOST_URI> state
```

不要在 running 状态下交错 rebuild、program、start，否则 Root/control meta 或 Variables Flow 可能短时间不完整。

## 工作流：添加真实节点

1. 运行：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> node probe --uri COM3
```

1. 如果 probe 成功，运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> node add --uri COM3`。
1. 如需配置端口，运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> node configure-port --uuid NODE_UUID --index 0 --baud 500000`。
1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> node info --uuid NODE_UUID` 和 `python tools/agent_cli/coral_agent.py --host <HOST_URI> node states`。

成功标准：

- 节点有 `uuid`。
- `layout` 存在。
- `ports` 显示端口 index、类型和波特率。

## 工作流：添加模拟节点

1. 运行：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> node add-simulated --name "Sim Front Node"
```

1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> node info --uuid NODE_UUID`。
1. 如需修改端口参数，运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> node configure-port --uuid NODE_UUID --index 0 --baud 115200`。

成功标准：

- `mcuUri` 以 `sim://` 开头。
- layout 为默认模拟节点 layout。

## 工作流：多节点编程

多节点任务先读 [11-multinode-system-design-reference.md](11-multinode-system-design-reference.md)，明确 Root、各 MCU 节点、变量流、安全输入和用户追问清单，再开始写代码。

1. Build 成功后运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> logic list`。
1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> node list`，得到每个节点 UUID、名称、layout、port configs。
1. 为每个节点明确选择 `logicName`。
1. 对每个节点调用：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> node program --uuid NODE_UUID --logic FrontLogic
```

1. 每次 program 后刷新：
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> node info --uuid NODE_UUID`
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> node state --uuid NODE_UUID`
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> variables meta`
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> variables values`
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> variables flow`

注意：

- 不要只靠 C# 类名猜测目标节点。
- 同一个 Logic class 只能编程到一个 MCU 节点；多节点必须使用不同 `logicName`。
- 同一个文件可生成多个 Logic。
- 编程前检查端口 index 是否和节点 layout 匹配。
- 多节点变量关系按节点实例 UUID 区分，但同一 Logic 多节点写同名 LowerIO 仍会冲突。

## 工作流：Root 配置和控制

Root Logic 运行在 Host 本机 .NET Runtime 上。它不是 MCU 节点，不连接串口/CAN，不使用 `/api/node/{uuid}/program`。

MCU 节点编程：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> node program --uuid NODE_UUID --logic WheelNodeLogic
```

Root 配置：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> root configure --logic DiffDriveRoot
```

Root 控制输入：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> root meta
python tools/agent_cli/coral_agent.py --host <HOST_URI> root set-control --name joystickX --value 0.5
```

工具示例：

```text
python tools/workflows/configure_root_and_controls.py --host <HOST_URI> --logic DiffDriveRoot --set joystickX=0.2 --set joystickY=0.8
```

注意：

- Build 发现 Root Logic，并生成 Root metadata。
- `root configure` 只是选择和绑定 Host 侧 Root Logic。
- Root `[AsControlItem]` 是 UI/Agent 输入，Root 再计算并写出 `[AsUpperIO]` 给 MCU 节点读取。
- 如果字段名不是 `logicName`，Host 会返回 400，避免误清空 Root 配置。

## 工作流：Start 和 Stop

Start：

1. 确认所有目标节点已 Program。
1. 如果有 Root Logic，先运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> root configure --logic RootLogicName`。
1. 如果是模拟节点任务，可运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> start --require-all`。
1. 如果是真实硬件、车辆运动或危险输出任务，先按“用户补充输入和硬件信息”中的启动授权问题询问用户。
1. 如果用户选择人类手动 Start，Agent 不调用 `/api/start`，只提示用户在网页点击 Start；也可运行 `python tools/workflows/safe_stop_build_program_start.py --host <HOST_URI> --manual-start --wait-before-state 30` 完成部署后等待状态。
1. 如果用户授权 Agent 启动，运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> start --require-all`。
1. 如需设置 joystick/control 变量，运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> root set-control --name joystickX --value 20`。
1. 调用：
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> state`
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> node states`
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> variables values`
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> variables flow`
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> logs terminal`
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> errors fatal`

Stop：

1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> stop --wait-idle --timeout 8`。
1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> state`。
1. 保留日志用于排错。

成功标准：

- Start 返回 `ok = true` 且 `status = "Started"`。
- `successNodes = totalNodes`。
- `session.isRunning = true`。

失败处理：

- 读取 `errors`。
- 读取 terminal log 和 node log。
- 如果 `status = "PartialFailure"`，说明部分节点已运行、部分失败；先 stop，再检查失败节点的 program/port/power/wiring。
- 如果有 Fatal Error，运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> errors fatal`，必要时再查底层 diver map。

## 工作流：调试变量和 IO

1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> variables meta` 看变量类型和方向。
1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> variables values` 看当前值。
1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> variables flow` 判断变量来源和读者。
1. 修改可控变量：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables set --name targetSpeed --value 100 --type int
```

1. 运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> node states` 看 DI/DO 和端口 TX/RX 统计。

## 工作流：WireTap 和日志

1. 开启节点监听：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> wiretap enable --uuid NODE_UUID --port 255 --tx --rx
```

`portIndex = 255` 表示全部端口。`flags` 的含义由 Host WireTap 配置定义。

1. 读取：
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> wiretap logs --uuid NODE_UUID`
   - `python tools/agent_cli/coral_agent.py --host <HOST_URI> logs node --uuid NODE_UUID`

1. 调试完成后关闭或调整监听。

## 工作流：错误监控

1. 运行期间定期调用 `python tools/agent_cli/coral_agent.py --host <HOST_URI> errors fatal`。
2. 如果存在 fatal error：
   - 读取 `uuid`。
   - 找到该节点 `logicName`。
   - 必要时调用底层 diver-map API 获取源码映射。
   - 根据错误数据中的 IL offset 或源位置定位代码。
3. 修改代码后重新 sync、build、program、start。
