# 遥控面板

CoralinkerHost 提供可视化遥控面板，将 UI 控件绑定到变量实现实时控制与监控。

## 控件类型

| 控件 | 可绑定变量 | 方向 | 说明 |
|------|-----------|------|------|
| Joystick | UpperIO 数值型 x2 | Host -> MCU | 双轴（X/Y），支持自动回中、键盘映射（WASD/方向键）、min/max |
| Slider | UpperIO 数值型 x1 | Host -> MCU | 单轴滑块 |
| Switch | UpperIO 数值型 x1 | Host -> MCU | 开关 |
| Gauge | 任意数值型 x1 | 只读显示 | 样式：数字/文本/横条/竖条/仪表盘，可设范围和单位 |

"数值型"指 `int`、`float`、`short`、`ushort` 等非数组、非字符串的基础类型。

## 配合逻辑代码

以 `examples/car_demo.cs` 为例：

```csharp
public class CarCart : CartDefinition
{
    [AsUpperIO] public int joystickX;    // 绑定 Joystick X 轴
    [AsUpperIO] public int joystickY;    // 绑定 Joystick Y 轴
    [AsLowerIO] public int leftRPM;      // 绑定 Gauge 显示
    [AsLowerIO] public int rightRPM;     // 绑定 Gauge 显示
    [AsLowerIO] public int speed;
    [AsLowerIO] public int steerAngle;
}
```

在遥控面板中添加 Joystick 控件并将 X/Y 轴分别绑定到 `joystickX` / `joystickY`，添加 Gauge 控件绑定到 `leftRPM` / `rightRPM`。用户操作 Joystick 时，值实时写入 UpperIO，MCU 逻辑据此计算输出。

## Root Logic 下的绑定规则

如果项目启用了 Root Logic 或后续由 Medulla 等上层控制器接管变量，遥控面板不应该直接绑定被 Root cart 声明的 UpperIO。应绑定 Root `[AsControlItem]`，再由 Root/Medulla 逻辑计算输出。

推荐：

```csharp
[LogicRunOnRoot]
public class RootDrive : RootLogic<RootCart>
{
    [AsControlItem] public int joystickX;   // Joystick X 绑定这里
    [AsControlItem] public int joystickY;   // Joystick Y 绑定这里

    public override void Operation()
    {
        cart.left_diff_speed = joystickY + joystickX;
        cart.right_diff_speed = joystickY - joystickX;
    }
}
```

不推荐把 Joystick 直接绑定到 `left_diff_speed` / `right_diff_speed`。这些字段是 Root cart 输出，由 Root/上层逻辑写入，MCU 子节点读取。

DIVERSession 会统一返回变量的 `controllable` 状态：普通 MCU UpperIO/MutualIO 可直接绑定；Root cart 接管的变量不可直接绑定；Root `ControlItem` 可绑定。
