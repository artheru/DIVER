# Coralinker 逻辑开发文档

Coralinker 是基于 DIVER（Dotnet Integrated Vehicle Embedded Runtime）的车辆嵌入式逻辑开发平台。你可以用 C# 编写控制逻辑，通过 CoralinkerHost Web 界面一键编译、下发到 MCU 节点运行，并实时监控变量、抓包调试、配置遥控界面。

## 文档索引

| 编号 | 文件 | 内容 |
|------|------|------|
| 01 | [01-quickstart.md](01-quickstart.md) | 5 分钟跑通 hello.cs 全链路 |
| 02 | [02-logic-api.md](02-logic-api.md) | Logic API 完整参考、支持类型、编码约束 |
| 03 | [03-build-and-deploy.md](03-build-and-deploy.md) | 编译、下发、启动、停止全流程 |
| 04 | [04-variables-and-io.md](04-variables-and-io.md) | 变量系统：UpperIO / LowerIO、类型、面板操作 |
| 05 | [05-serial-and-can.md](05-serial-and-can.md) | 串口 / CAN 通信 API 与 WireTap 抓包 |
| 06 | [06-remote-control.md](06-remote-control.md) | 遥控面板：Joystick / Gauge / Switch 配置 |
| 07 | [07-node-management.md](07-node-management.md) | 节点管理、Layout 自动发现、端口配置 |
| 08 | [08-faq.md](08-faq.md) | 常见问题与排查 |

## 类型定义（Stub）

[stubs/CartActivator.cs](stubs/CartActivator.cs) 包含客户逻辑代码的全部可用类型定义（`RunOnMCU`、`CANMessage`、`LadderLogic<T>`、`CartDefinition`、属性等）。此文件仅用于 AI 辅助编码和 IDE 类型提示，不参与实际编译（编译由 CoralinkerHost 内置链完成）。

## 示例代码

[examples/](examples/) 目录包含可直接运行的示例逻辑，建议按 hello → numeric → port_demo → car_demo → dual_node_skeleton 顺序学习。

## 运行时

[runtime/](runtime/) 目录存放 CoralinkerHost 发布包和 MCU 固件升级包。

## 推荐阅读顺序

1. 先读 **01-quickstart**，跑通一次完整闭环。
2. 读 **02-logic-api**，掌握全部可用 API 和类型约束。
3. 按需阅读 03~07，解决具体场景问题。
4. 遇到问题查 **08-faq**。

## 交付清单

交付给客户时，本目录下所有文件（README + 01~08 + examples/ + runtime/）作为完整包提供。
