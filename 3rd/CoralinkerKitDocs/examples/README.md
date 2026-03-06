# 示例逻辑

| 文件 | 内容 | 学习顺序 |
|------|------|----------|
| `hello.cs` | 最小逻辑，日志输出 | 1 |
| `numeric.cs` | UpperIO / LowerIO 变量读写 | 2 |
| `port_demo.cs` | 串口（Modbus RTU）+ CAN 报文发送 | 3 |
| `car_demo.cs` | 遥控界面变量绑定（Joystick/Gauge） | 4 |
| `dual_node_skeleton.cs` | 双节点（前/后）逻辑骨架，含 CAN 工具封装 | 5 |

建议先跑 `hello.cs` 验证编译链路，再按顺序逐个学习。以 `dual_node_skeleton.cs` 为模板搭建实际多节点逻辑。
