# 变量系统

## UpperIO 与 LowerIO

DIVER 的变量系统基于 `CartDefinition`，每个字段标注方向：

| 标注 | 方向 | 谁写 | 谁读 | 典型用途 |
| --- | --- | --- | --- | --- |
| `[AsUpperIO]` | Host -> MCU | Host（变量面板/遥控面板） | MCU 逻辑 | 目标速度、目标角度、开关量 |
| `[AsLowerIO]` | MCU -> Host | MCU 逻辑 | Host（变量面板） | 实际速度、传感器值、状态码 |

示例：

```csharp
public class MotorCart : CartDefinition
{
    [AsUpperIO] public int targetRPM;       // Host 设定目标转速
    [AsUpperIO] public bool enableMotor;    // Host 控制使能开关
    [AsLowerIO] public int actualRPM;       // MCU 上报实际转速
    [AsLowerIO] public int errorCode;       // MCU 上报错误码
}
```

## 支持的类型

完整类型表见 [02-logic-api.md](02-logic-api.md) 第 3 节。常用的：

- 控制量：`int`、`float`、`bool`
- 字节缓冲：`byte[]`
- 状态码/枚举值：`int`、`ushort`

## 变量更新机制

- UpperIO：Host 修改后，在下一个扫描周期之前同步到 MCU。
- LowerIO：每个扫描周期结束后，MCU 将最新值上报给 Host。
- 更新频率取决于 `scanInterval`。

Agent 读取变量优先使用 Python CLI：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables meta
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables values
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables flow
```

Agent 修改变量优先使用 Python CLI：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables set --name targetSpeed --value 100 --type int
```

只有可控变量能被 Host 或 Agent 修改。不可控变量通常由 MCU 节点或 Root Runtime 管理。

## 多节点与变量共享

**所有 CartDefinition 中同名字段在运行时是同一个变量。** 例如 `FrontCart.targetSpeed` 和 `RearCart.targetSpeed` 如果字段名都叫 `targetSpeed`，在 Host 变量面板中它们是同一个值，修改一个两个节点都会收到。

利用这一点可以实现跨节点数据共享（如统一的目标速度）。如果需要独立控制，请使用不同字段名（如 `targetSpeed_front` / `targetSpeed_rear`）。

**硬规则：一个 Logic class 只能编程到一个 MCU 节点。** 同一个 Logic class 会产生同一组 LowerIO 字段；如果多个节点运行同一个 Logic，它们会上报同名 LowerIO，后到的值会覆盖先到的，导致数据混乱。

## Variables Flow

Variables Flow 描述变量在 Root、节点和变量之间的流向。Agent 应使用它理解多节点系统，而不是靠变量名猜测。

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> variables flow
```

返回内容：

- `nodes`：Root Runtime 和每个节点实例。
- `variables`：每个变量的名称、类型、方向、当前值、sourceIds、readerIds、writerIds。

字段含义：

- `sourceIds`：声明或拥有变量来源的节点实例。
- `readerIds`：读取该变量的节点实例。
- `writerIds`：写入该变量的节点实例。
- `root-runtime`：Root Runtime 的固定 ID。

多节点注意：

- 每个 `logicName` 只能绑定到一个 MCU 节点。
- Flow 使用节点 UUID 区分实例，但不能解决同一 Logic 多节点写同名 LowerIO 的冲突。
- 不要按 ClassName 推断变量属于哪个节点。
- Program、Remove Node、切换 Root Logic 后都要重新读取 flow。

## IO 状态

节点状态中包含 DI/DO 和端口统计：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> node states
python tools/agent_cli/coral_agent.py --host <HOST_URI> node state --uuid NODE_UUID
```

常用字段：

- `stats.digitalInputs`
- `stats.digitalOutputs`
- `stats.ports[].txFrames`
- `stats.ports[].rxFrames`
- `stats.ports[].txBytes`
- `stats.ports[].rxBytes`

模拟节点默认提供 32 路 DI 和 32 路 DO，可用于无硬件验证 `RunOnMCU.ReadSnapshot()` 和 `RunOnMCU.WriteSnapshot()`。

模拟验证安全逻辑时，不要把同一个低位 bit 同时当作安全 DI 和状态 DO 使用。建议把急停、触边等安全 DI 放在低位，把示例状态 DO 放在高位，避免模拟环境中输出状态影响输入判断，造成持续自触发故障。

## Root/上层控制器接管变量

Root Logic、Medulla 等运行在主机侧的上层控制器，也会作为 DIVERSession 的“虚拟节点”声明变量。这样变量类型、方向和可控性都由 DIVERSession 统一决定，而不是由 CoralinkerHost 前端补丁修正。

典型差速控制结构：

```csharp
public class RootCart : CartDefinition
{
    [AsUpperIO] public int left_diff_speed;   // Root 写，MCU 子节点读
    [AsUpperIO] public int right_diff_speed;
}

[LogicRunOnRoot]
public class RootDrive : RootLogic<RootCart>
{
    [AsControlItem] public int joystickX;     // 遥控器/UI 写
    [AsControlItem] public int joystickY;

    public override void Operation()
    {
        cart.left_diff_speed = joystickY + joystickX;
        cart.right_diff_speed = joystickY - joystickX;
    }
}
```

规则：

- 同名变量仍然共享。例如 MCU 子节点也声明 `left_diff_speed` 为 `[AsUpperIO]` 时，它读取的是 Root 写出的同一个变量值。
- Root cart 的 `[AsUpperIO]` 表示“Root/上层逻辑写，MCU 节点读”，不表示遥控面板可以直接写。
- 遥控面板应绑定 `[AsControlItem]` 字段；Root/Medulla 根据控制输入计算后，再发布到 cart UpperIO。
- 如果同名变量在不同节点声明了不同类型，DIVERSession 会记录类型冲突；不要依赖 `Unknown` 作为兼容行为。
