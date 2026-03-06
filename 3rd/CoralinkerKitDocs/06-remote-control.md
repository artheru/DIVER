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
