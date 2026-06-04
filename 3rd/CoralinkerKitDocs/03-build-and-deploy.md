# 编译与部署

本文同时描述网页操作和 Agent 工具流程。Agent 应按 [09-agent-workflows.md](09-agent-workflows.md) 的固定流程执行，并优先使用 `tools/agent_cli/coral_agent.py` 或 `tools/workflows/*.py`。底层 GET/POST API 参考见 [10-agent-api.md](10-agent-api.md)。

## 编译

逻辑代码在 CoralinkerHost Web 界面中点击编译按钮即可。编译器将 C# 源码转换为 MCU 字节码（`.bin`），同时生成字段映射（`.bin.json`）。这不是标准 .NET 编译——代码最终运行在 MCU 虚拟机上。

编译环境要求：Host 机器上需安装 .NET 8 SDK。

Agent 编译前必须确保输入文件已经通过 `coral_agent.py files sync` 保存并提交。Build 会拒绝 dirty input。

## 部署流程

1. **编译** — 点击编译，确认 `Build succeeded`。
2. **分配** — 在图形界面上为目标节点选择编译好的逻辑。
3. **启动** — 点击启动，所有已分配逻辑的节点开始运行。
4. **监控** — 通过变量面板查看 LowerIO、修改 UpperIO；通过日志面板查看 `Console.WriteLine` 输出。
5. **停止** — 点击停止，所有节点回到 idle 状态。

## Agent 工具流程

### 1. 保存并提交文件

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> files snapshot
python tools/agent_cli/coral_agent.py --host <HOST_URI> files sync --path assets/inputs/Logic.cs --from-file ./Logic.cs --message "update logic"
```

`files sync` 会自动读取 snapshot、填入 `baseHead/baseHash` 并提交，避免手写 JSON。

### 2. 编译

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> build
python tools/agent_cli/coral_agent.py --host <HOST_URI> logs build
python tools/agent_cli/coral_agent.py --host <HOST_URI> logic list
python tools/agent_cli/coral_agent.py --host <HOST_URI> state
```

Build 成功返回：

- `buildId`
- `sourceCommit`
- `artifacts`
- `rootLogics`

`artifacts` 是可编程到节点的 logic 名称。多节点项目不要靠 C# 文件名猜测，应使用这里的 `logicName`。一个 `logicName` 只能分配给一个 MCU 节点；多个节点必须使用不同 Logic class。

### 3. 编程节点

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> node program --uuid NODE_UUID --logic FrontLogic
```

编程成功后必须刷新：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> node info --uuid NODE_UUID
python tools/agent_cli/coral_agent.py --host <HOST_URI> node state --uuid NODE_UUID
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables meta
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables values
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables flow
```

原因：新程序可能声明了不同的 Cart 字段，变量表和 Variables Flow 会变化。

### 4. 启动

模拟节点任务可以由 Agent 自动 start。真实硬件、车辆运动或危险输出任务不要默认自动 start。Agent 应先询问用户启动授权；如果用户选择人类手动启动，Agent 只完成 build/program/root configure，然后等待用户在网页上点击 Start。

启动授权模式应记录到 `ai-deck/fact.md`。如果该文件不存在，按文档包中的 `runtime/fact-template.md` 创建。没有明确授权时，默认人类手动 Start。

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> start --require-all
python tools/agent_cli/coral_agent.py --host <HOST_URI> state
python tools/agent_cli/coral_agent.py --host <HOST_URI> node states
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables values
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables flow
python tools/agent_cli/coral_agent.py --host <HOST_URI> errors fatal
```

Start 成功标准：

- `ok = true`
- `successNodes = totalNodes`
- `state` 返回的 session 中 `isRunning = true`

### 5. 停止

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> stop --wait-idle --timeout 8
python tools/agent_cli/coral_agent.py --host <HOST_URI> state
python tools/agent_cli/coral_agent.py --host <HOST_URI> node states
```

停止后不要清掉日志，先保留日志用于排查。

## 编译失败常见原因

| 现象 | 原因 |
| --- | --- |
| `CartActivator` 未找到 | 缺少 `using CartActivator;` |
| `Operation` 未实现 | MCU 逻辑类没有 `public override void Operation(int iteration)`，或 Root 逻辑误写成带参数签名；Root 应使用 `public override void Operation()` |
| 类型不支持 | CartDefinition 字段用了 `double`/`long`/`List<T>` 等不支持的类型（见 [02-logic-api.md](02-logic-api.md) 第 3 节） |
| API 返回 dirty input | 修改后没有通过 Host 提交，先运行 `coral_agent.py files sync` |
| Program 后变量表不对 | 没有刷新 `variables meta`、`variables values`、`variables flow` |

更多问题参见 [08-faq.md](08-faq.md)。
