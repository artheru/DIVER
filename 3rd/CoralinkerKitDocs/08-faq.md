# 常见问题

## Agent 常见阻塞

### sync 返回 400 或文件没有提交

不要使用旧 `files[]` 格式，也不要手写 shell 转义后的 JSON 字符串。使用：

```text
python tools/agent_cli/coral_agent.py files sync --path assets/inputs/Logic.cs --from-file ./Logic.cs --message "update logic"
```

直接调用 HTTP 时必须使用 `changes[]`。

### probe/add 失败

检查返回中的 `mcuUri` 和 `hint`。真实节点先 probe 再 add；模拟节点不要构造假串口 URI，使用 `/api/node/add-simulated` 或：

```text
python tools/agent_cli/coral_agent.py node add-simulated --name "Sim Node"
```

### Root 编程理解错误

Root 不是 MCU 节点，不使用 `/api/node/{uuid}/program`。优先使用：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> build
python tools/agent_cli/coral_agent.py --host <HOST_URI> root configure --logic RootLogicName
python tools/agent_cli/coral_agent.py --host <HOST_URI> root meta
python tools/agent_cli/coral_agent.py --host <HOST_URI> root set-control --name joystickX --value 20
```

如果请求体没有 `logicName` 字段，Host 会返回 400，不会误清空 Root 配置。

### start 返回 PartialFailure

`status = "PartialFailure"` 表示部分节点已经运行，部分节点失败。Agent 必须把它当失败处理：

1. `python tools/agent_cli/coral_agent.py --host <HOST_URI> stop --wait-idle --timeout 8`
1. `python tools/agent_cli/coral_agent.py --host <HOST_URI> logs terminal`
1. `python tools/agent_cli/coral_agent.py --host <HOST_URI> logs node --uuid NODE_UUID`
1. 检查失败节点是否已 program、端口配置、供电、接线和设备占用。

### stop 后立刻 build/program/start

建议使用 `tools/workflows/safe_stop_build_program_start.py`，或用 `coral_agent.py stop --wait-idle` 等待 Idle。不要在 running 状态下交错 build/program/start。

### portConfigs 局部修改

端口配置优先使用 CLI，只改一个端口时不要回写不确定的整张表：

```text
python tools/agent_cli/coral_agent.py --host <HOST_URI> node configure-port --uuid NODE_UUID --index 0 --baud 115200
```

### WireTap 没看到数据

先确认端口 index。`portIndex = 255` 表示全部端口。Serial 调试使用 `tools/workflows/debug_serial.py`，CAN 调试使用 `tools/workflows/debug_can_wiretap.py`。

## 编译

**Q: 编译报 `CartActivator` 未找到**
A: 文件顶部缺少 `using CartActivator;`。

**Q: 编译报 Operation 未实现**
A: MCU 逻辑类必须有 `public override void Operation(int iteration)`；Root 逻辑类必须有 `public override void Operation()`。不要把 MCU 和 Root 的签名混用。

**Q: 编译报类型不支持**
A: CartDefinition 字段用了不支持的类型。只能用 `bool`/`byte`/`sbyte`/`char`/`short`/`ushort`/`int`/`uint`/`float`/`string`/一维基础类型数组。不能用 `double`/`long`/`List<T>` 等。详见 [02-logic-api.md](02-logic-api.md) 第 3 节。

**Q: `string` 字段能不能用于 Agent 自动验证？**
A: 不建议。`string` 是底层协议支持类型，但 Host 变量表和 Agent API 对字符串字段的显示可能不完整。需要自动判断状态时，用 `int statusCode`、`float value` 等数值字段更可靠。

**Q: 编译超时**
A: 检查 Host 机器上 .NET 8 SDK 是否已安装（`dotnet --version`）。

**Q: 编译成功但逻辑行为不符合预期**
A: 注意这不是标准 .NET 运行时。避免依赖反射、LINQ 复杂操作、async/await 等高级特性。

## 运行

**Q: 节点状态一直是 disconnected**
A: 检查 USB 连接、串口是否被其他程序占用。

**Q: CAN 发出去了但设备没反应**
A: 检查 CAN 波特率是否与设备一致（在节点端口配置中修改）；检查 CAN ID、Payload 字节序是否正确。建议先用 WireTap 开启该 CAN 端口的 TX/RX 监听，确认报文确实发出且能收到设备回复。

**Q: 串口收不到数据**
A: 检查串口波特率配置；用 WireTap 开启 RX 监听确认是否有数据到达。

**Q: 变量面板中 LowerIO 不更新**
A: 确认逻辑正在运行（节点状态为 running）；确认 `Operation()` 中确实在给 `[AsLowerIO]` 字段赋值。

**Q: UpperIO 修改后 MCU 侧没生效**
A: UpperIO 在下一个扫描周期同步，延迟取决于 `scanInterval`。确认逻辑中读取了对应的 `cart.xxx` 字段。

## 编码

**Q: 可以用 `double` 吗？**
A: 不可以。MCU 虚拟机不支持 64 位类型。用 `float` 替代。

**Q: 可以用 `List<T>` 吗？**
A: 不可以。用一维数组替代（如 `int[]`）。

**Q: 可以定义辅助类和静态方法吗？**
A: 可以。可以在同一文件中定义 `static class` 封装 CAN/串口工具方法（参见 `examples/dual_node_skeleton.cs` 中的 `DemoCanIo`）。

**Q: Operation 里可以用循环和条件判断吗？**
A: 可以。`for`/`while`/`if`/`switch` 等基本控制流都支持。避免无限循环阻塞扫描周期。

**Q: 可以保存跨周期状态吗？**
A: 可以。在逻辑类中定义私有字段即可（如 `private int stage = 0;`），它们在整个运行期间保持。
