# 编译与部署

## 编译

逻辑代码在 CoralinkerHost Web 界面中点击编译按钮即可。编译器将 C# 源码转换为 MCU 字节码（`.bin`），同时生成字段映射（`.bin.json`）。这不是标准 .NET 编译——代码最终运行在 MCU 虚拟机上。

编译环境要求：Host 机器上需安装 .NET 8 SDK。

## 部署流程

1. **编译** — 点击编译，确认 `Build succeeded`。
2. **分配** — 在图形界面上为目标节点选择编译好的逻辑。
3. **启动** — 点击启动，所有已分配逻辑的节点开始运行。
4. **监控** — 通过变量面板查看 LowerIO、修改 UpperIO；通过日志面板查看 `Console.WriteLine` 输出。
5. **停止** — 点击停止，所有节点回到 idle 状态。

## 编译失败常见原因

| 现象 | 原因 |
|------|------|
| `CartActivator` 未找到 | 缺少 `using CartActivator;` |
| `Operation` 未实现 | 逻辑类没有 `public override void Operation(int iteration)` |
| 类型不支持 | CartDefinition 字段用了 `double`/`long`/`List<T>` 等不支持的类型（见 [02-logic-api.md](02-logic-api.md) 第 3 节） |

更多问题参见 [08-faq.md](08-faq.md)。
