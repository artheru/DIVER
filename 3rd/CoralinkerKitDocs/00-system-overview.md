# 系统概览

本文是给人和 Agent 的系统地图。Agent 在写代码或调用 API 前，应先读本页。

## 主要对象

| 对象 | 含义 | Agent 应如何使用 |
| --- | --- | --- |
| Host | CoralinkerHost 后端和网页前端 | 所有文件、编译、节点、变量、日志操作都走 Host API |
| Project | 当前用户工程状态 | 包含选中文件、节点配置、Root Logic、遥控布局 |
| `assets/inputs` | 用户 C# 逻辑源文件 | Agent 只能在这里创建、修改、删除用户代码 |
| `assets/generated` | Build 生成的 `.bin`、`.bin.json`、`.root.json` 等产物 | 只读。不要手工修改 |
| Git 历史 | Host 在 data 目录内部维护的输入文件历史 | Agent 修改文件后必须提交，build 只接受干净输入 |
| 真实节点 | 通过串口/设备 URI 连接的 MCU 节点 | 需要 probe、add、configure、program、start |
| 模拟节点 | Host 内置的虚拟 MCU 节点 | 用 `sim://{uuid}` 标识，每个模拟节点一个独立子进程 |
| Root Runtime | 运行在 Host 本机的上层逻辑 | 可读遥控输入，写 UpperIO 给 MCU 节点 |
| Variables | Host 和节点共享的变量表 | Agent 可读取变量列表、变量值、修改可控变量 |
| Variables Flow | 变量在 Root、节点、变量之间的流向 | Agent 用它判断谁写变量、谁读变量 |
| WireTap | 节点串口/CAN 收发记录 | Agent 用它排查通信和协议问题 |
| Fatal Error | MCU 或模拟节点上报的致命异常 | Agent 用它定位 HardFault、ASSERT 和源代码行 |

## 文件树

Agent 看到的工程文件主要在 Host data 目录下：

```text
data/
  project.json
  assets/
    inputs/
      UserLogic.cs
    generated/
      LogicName.bin
      LogicName.bin.json
      LogicName.diver.map.json
      RootLogic.root.json
  .git/
```

规则：

- 只修改 `assets/inputs/*.cs`。
- 不修改 `assets/generated/*`。
- 不直接操作 `.git`。
- 通过 `python tools/agent_cli/coral_agent.py files sync ...` 批量推送文件并提交。
- 通过 `python tools/agent_cli/coral_agent.py files snapshot` 判断 Host 端文件是否变化。

## 节点类型

### 真实节点

真实节点连接物理 MCU。标准流程：

```text
Probe -> Add -> Configure -> Build -> Program -> Start -> Observe -> Stop -> Remove
```

真实节点的 layout 来自 MCU 固件上报。端口 index、端口类型、DI/DO 数量以 `coral_agent.py node info --uuid ...` 和 `coral_agent.py node states` 为准。

### 模拟节点

模拟节点由 Host 创建，不需要物理硬件。标准流程：

```text
Add Simulated -> Configure -> Build -> Program -> Start -> Observe -> Stop -> Remove
```

模拟节点事实：

- URI 形式是 `sim://{uuid}`。
- 每个模拟节点使用独立 `CoralinkerSimNodeHost` 子进程。
- 默认 layout：
  - 32 路 DI。
  - 32 路 DO。
  - RS485-A。
  - RS485-B。
  - RS232。
  - CAN。
- 可用于无硬件验证逻辑、Variables、WireTap、日志和 IO 状态。

## 变量方向

| 方向 | 谁写 | 谁读 | 典型用途 |
| --- | --- | --- | --- |
| UpperIO | Host 或 Root Runtime | MCU 节点 | 目标速度、使能、控制命令 |
| LowerIO | MCU 节点 | Host 或 Root Runtime | 实际速度、状态码、传感器值 |
| ControlItem | UI 或 Agent | Root Runtime | 摇杆、开关、上层控制输入 |

同名变量在运行时是同一个变量。一个 Logic class 只能编程到一个 MCU 节点；多节点应使用不同 `logicName` 和角色化 LowerIO 字段名。Variables Flow 使用节点实例 UUID 区分来源和读写关系，但不能消除多个节点写同名 LowerIO 的冲突。

## Agent 默认通信模型

网页使用 SignalR 做实时同步。Agent 默认不要依赖 SignalR。

推荐模型：

```text
HTTP command -> HTTP state refresh -> inspect logs/errors -> next command
```

常用聚合刷新：

- `python tools/agent_cli/coral_agent.py --host <HOST_URI> state`
- `python tools/agent_cli/coral_agent.py --host <HOST_URI> variables flow`
- `python tools/agent_cli/coral_agent.py --host <HOST_URI> errors fatal`

SignalR 只作为高级实时订阅能力，不作为 Agent 完成任务的必要条件。
