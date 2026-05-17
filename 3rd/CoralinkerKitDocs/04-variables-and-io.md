# 变量系统

## UpperIO 与 LowerIO

DIVER 的变量系统基于 `CartDefinition`，每个字段标注方向：

| 标注 | 方向 | 谁写 | 谁读 | 典型用途 |
|------|------|------|------|----------|
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

## 多节点与变量共享

**所有 CartDefinition 中同名字段在运行时是同一个变量。** 例如 `FrontCart.targetSpeed` 和 `RearCart.targetSpeed` 如果字段名都叫 `targetSpeed`，在 Host 变量面板中它们是同一个值，修改一个两个节点都会收到。

利用这一点可以实现跨节点数据共享（如统一的目标速度）。如果需要独立控制，请使用不同字段名（如 `targetSpeed_front` / `targetSpeed_rear`）。

**建议：不要让两个节点同时写同一个 LowerIO 字段。** 后到的值会覆盖先到的，导致数据混乱。

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
