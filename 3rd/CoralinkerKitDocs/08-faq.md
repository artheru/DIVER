# 常见问题

## 编译

**Q: 编译报 `CartActivator` 未找到**
A: 文件顶部缺少 `using CartActivator;`。

**Q: 编译报 Operation 未实现**
A: 逻辑类必须有 `public override void Operation(int iteration)`，签名不能改。

**Q: 编译报类型不支持**
A: CartDefinition 字段用了不支持的类型。只能用 `bool`/`byte`/`sbyte`/`char`/`short`/`ushort`/`int`/`uint`/`float`/`string`/一维基础类型数组。不能用 `double`/`long`/`List<T>` 等。详见 [02-logic-api.md](02-logic-api.md) 第 3 节。

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
