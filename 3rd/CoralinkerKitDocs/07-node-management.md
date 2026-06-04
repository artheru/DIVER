# 节点管理

## 节点生命周期

```text
探测(Probe) -> 添加(Add) -> 配置(Configure) -> 编程(Program) -> 启动(Start) -> 运行(Running) -> 停止(Stop)
```

- **探测**：DIVER 通过串口连接 MCU，读取版本和硬件布局。
- **添加**：将节点纳入管理，自动初始化端口配置。
- **配置**：设置端口参数（CAN 波特率、串口波特率等）。
- **编程**：将编译好的逻辑字节码下发到节点。
- **启动/停止**：批量控制所有节点的运行状态。

Agent 应把真实节点和模拟节点视为同一种 Node，只是创建方式不同。

## 真实节点

真实节点连接物理 MCU。Agent 优先使用 Python CLI：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> node probe --uri COM3
python tools/agent_cli/coral_agent.py --host <HOST_URI> node add --uri COM3
python tools/agent_cli/coral_agent.py --host <HOST_URI> node configure-port --uuid NODE_UUID --index 0 --baud 500000
python tools/agent_cli/coral_agent.py --host <HOST_URI> node info --uuid NODE_UUID
python tools/agent_cli/coral_agent.py --host <HOST_URI> node states
```

只有 CLI 不覆盖的字段才直接调用底层 API。Probe/Add 的底层请求是：

```json
{ "mcuUri": "COM3" }
```

Configure 请求：

```json
{
  "nodeName": "Front Node",
  "portConfigs": [
    { "type": "CAN", "name": "CAN-1", "baud": 500000, "retryTimeMs": 10 }
  ],
  "extraInfo": { "x": 100, "y": 200 }
}
```

## 模拟节点

模拟节点不需要物理硬件。它由 Host 创建，每个模拟节点由独立 `CoralinkerSimNodeHost` 子进程隔离运行。

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> node add-simulated --name "Sim Front Node"
```

底层请求：

```json
{ "name": "Sim Front Node" }
```

成功后：

- `mcuUri` 以 `sim://` 开头。
- 默认 layout 为 32 DI、32 DO、RS485-A、RS485-B、RS232、CAN。
- 后续仍使用 configure、program、start、stop、remove。

模拟节点适合：

- 没有硬件时验证用户逻辑。
- 验证 Variables 和 Variables Flow。
- 验证 Snapshot IO。
- 验证 WireTap 和日志链路。

## Layout 自动发现

添加节点时，DIVER 向 MCU 发送 `GetLayout` 命令，MCU 返回硬件布局信息：

- **端口列表**：每个端口的类型（Serial / CAN）和名称（如 "RS485-1"、"CAN-1"）
- **数字 IO 数量**：DigitalInputCount、DigitalOutputCount

Host 据此自动生成端口配置。端口 index 由 Layout 返回的顺序决定（从 0 开始），这就是逻辑代码中 `RunOnMCU.WriteCANMessage(port, ...)` 的 `port` 参数。

不同硬件的 Layout 不同，端口数量和顺序因板而异。**不要硬编码端口 index 的含义**，以 Web 界面上的端口名称显示为准。

## 端口配置

| 端口类型 | 可配参数 |
| --- | --- |
| CAN | 波特率（默认 500000 或 1000000）、重发间隔（ms） |
| Serial | 波特率（默认 9600 或 115200）、帧间隔（ms） |

## 节点状态

| 状态 | 含义 |
| --- | --- |
| `idle` | 已添加但未运行 |
| `running` | 正在执行逻辑 |
| `disconnected` | 连接断开 |
| `error` | 致命错误 |

## 多节点

每个节点独立运行各自的逻辑，拥有独立的变量集和端口。一个逻辑文件可包含多个 `LadderLogic<T>` 类，分配到不同节点（见 [02-logic-api.md](02-logic-api.md) 第 1 节"单文件多逻辑"）。

多节点编程规则：

- Build 后先读取 `artifacts` 或运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> logic list`。
- 为每个节点明确选择 `logicName`。
- 同一个 `logicName` 只能编程到一个 MCU 节点；如果多个节点逻辑结构相似，也要创建不同 Logic class。
- 不要只靠 C# 类名或文件名猜测目标节点。
- 编程前检查节点 layout 和 port config 是否符合逻辑代码使用的端口 index。
- 编程后刷新：
  - `python tools/agent_cli/coral_agent.py --host <HOST_URI> node info --uuid NODE_UUID`
  - `python tools/agent_cli/coral_agent.py --host <HOST_URI> node state --uuid NODE_UUID`
  - `python tools/agent_cli/coral_agent.py --host <HOST_URI> variables meta`
  - `python tools/agent_cli/coral_agent.py --host <HOST_URI> variables values`
  - `python tools/agent_cli/coral_agent.py --host <HOST_URI> variables flow`

Variables Flow 已按节点实例 UUID 区分关系，但它不能消除同一 Logic 多节点写同名 LowerIO 的冲突。多节点必须使用不同 `logicName` 和角色化 LowerIO 字段名。

## 删除节点

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> node remove --uuid NODE_UUID
```

删除节点后必须刷新：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> node list
python tools/agent_cli/coral_agent.py --host <HOST_URI> node states
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables meta
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables values
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables flow
```

原因：节点删除后，Host 会清理不再被任何当前节点或 Root 声明的变量。
