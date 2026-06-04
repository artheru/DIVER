# Coralinker Kit 开发文档

Coralinker 是基于 DIVER（Dotnet Integrated Vehicle Embedded Runtime）的车辆嵌入式逻辑开发平台。用户用 C# 编写逻辑，CoralinkerHost 负责编译、节点编程、启动停止、变量同步、日志和 WireTap 调试。

本文档同时服务两类读者：

- 人类开发者：从 HTML 页面阅读概念、示例和排查方法。
- Cursor 或其他 Agents：从 Markdown 源文件读取系统结构、固定操作流程和 HTTP API。

Agent 默认入口是 `<HOST_URI>/api/docs/kit/md/README.md`。`<HOST_URI>` 表示当前 CoralinkerHost 地址，例如用户打开文档时的 `scheme://host:port`，不要写死为 localhost。HTML 入口是 `<HOST_URI>/docs/kit/`。

## Agent 启动后的强制步骤

无论用户任务是写代码、调试节点、查看变量还是排查日志，Agent 都应先完成这些步骤：

1. 确定 `<HOST_URI>`。如果用户只给了文档 URL，例如 `<HOST_URI>/api/docs/kit/md/README.md`，就取 URL 的 origin 作为 `<HOST_URI>`。
1. 如果还没有工具文件，先下载 `<HOST_URI>/api/docs/kit/bundle.zip` 并解压到 `ai-deck/kit-docs/`；如果已经有工具，运行 `python tools/agent_cli/coral_agent.py --host <HOST_URI> docs download --out ai-deck/kit-docs --bundle`。
1. 先阅读解压后的 `resources.json`、`README.md`、`09-agent-workflows.md`、`10-agent-api.md`、`tools/README.md`。
1. 涉及 Root/变量/遥控时，继续阅读 `04-variables-and-io.md` 和 `06-remote-control.md`。
1. 优先使用 bundle 中的 `tools/agent_cli/coral_agent.py` 和 `tools/workflows/*.py`。不要优先写 curl 或自己拼大段 Python HTTP 代码。
1. 开始修改前，向用户说明准备做什么、会改哪些文件、会调用哪些关键工具/API。
1. 每个关键阶段结束后，向用户说明已经做了什么、结果是什么、下一步做什么。
1. 任务结束时，说明验证证据、剩余风险、以及需要用户补充的信息。

除非下载 bundle 失败，否则不要逐个用 curl 读取大量 Markdown 文件。

## 如果不能下载 bundle

以下是 fallback，不是默认流程。只有 bundle 下载失败，才逐个读取 Markdown：

- 读取 `<HOST_URI>/api/docs/kit/resources`，获取资源清单。
- 把相对 Markdown 链接解析到 `<HOST_URI>/api/docs/kit/md/` 下。
- 例如 `[09-agent-workflows.md](09-agent-workflows.md)` 对应 `<HOST_URI>/api/docs/kit/md/09-agent-workflows.md`。
- `examples/`、`stubs/`、`tools/` 也按 `<HOST_URI>/api/docs/kit/md/...` 读取。
- 人类网页链接使用 `<HOST_URI>/docs/kit/{name}.html`，Agent 不要用 HTML 作为默认输入。

## Agent 本地目录规则

Agent 自己产生的临时文件必须放在 `ai-deck/` 下，不要散落到仓库根目录或 KitDocs 目录。推荐目录：

| 目录 | 用途 |
| --- | --- |
| `ai-deck/kit-docs/` | 下载并解压 Host 文档 bundle、Python CLI 和 workflow。 |
| `ai-deck/agent_work/<YYYYMMDD-HHMMSS-task>/` | 保存任务草稿、临时 `.cs` 文件、调试脚本、请求/响应样本和中间结果。 |
| `ai-deck/agent_feedback/` | 记录文档、API、CLI、workflow 使用中的问题、建议和复现步骤。 |
| `ai-deck/fact.md` | 记录已确认的现场硬件、车辆、协议、安全和应用事实；模板见 [runtime/fact-template.md](runtime/fact-template.md)。 |

每个 Agent 自建目录都应包含 `desc.md`，说明目录用途、任务目标、文件列表和哪些文件只是临时产物。最终用户代码必须通过 Host API 同步到 `assets/inputs/*.cs`，不要把 `ai-deck/agent_work` 当作正式源码目录。如果 `ai-deck/fact.md` 不存在，按 `runtime/fact-template.md` 创建。

## 必读顺序

1. [00-system-overview.md](00-system-overview.md)：系统对象、文件目录、真实节点和模拟节点。
2. [01-quickstart.md](01-quickstart.md)：跑通最小逻辑。
3. [09-agent-workflows.md](09-agent-workflows.md)：Agent 必须按这里的流程操作。
4. [10-agent-api.md](10-agent-api.md)：HTTP API 路径、请求和刷新顺序。
5. [tools/README.md](tools/README.md)：Agent Python CLI 和 workflow。
6. [02-logic-api.md](02-logic-api.md)：用户逻辑代码 API。
7. [04-variables-and-io.md](04-variables-and-io.md)：变量、Root、Variables Flow。
8. [06-remote-control.md](06-remote-control.md)：Root ControlItem 和遥控输入。
9. [11-multinode-system-design-reference.md](11-multinode-system-design-reference.md)：多节点 AGV/设备系统拆分、安全逻辑和用户追问清单。

## 文档索引

| 文件 | 内容 |
| --- | --- |
| [00-system-overview.md](00-system-overview.md) | Host、Project、文件树、节点、Root Runtime、Variables、WireTap、Git 历史 |
| [01-quickstart.md](01-quickstart.md) | 5 分钟跑通 hello.cs |
| [02-logic-api.md](02-logic-api.md) | Logic API、类型、编码约束、多逻辑文件 |
| [03-build-and-deploy.md](03-build-and-deploy.md) | 编译、编程、启动、停止和状态刷新 |
| [04-variables-and-io.md](04-variables-and-io.md) | UpperIO、LowerIO、Variables Flow、IO 状态 |
| [05-serial-and-can.md](05-serial-and-can.md) | 串口、CAN、Snapshot IO、WireTap |
| [06-remote-control.md](06-remote-control.md) | 遥控面板使用说明 |
| [07-node-management.md](07-node-management.md) | 真实节点、模拟节点、端口配置、多节点编程 |
| [08-faq.md](08-faq.md) | 常见问题与排查 |
| [09-agent-workflows.md](09-agent-workflows.md) | Agent 固定工作流和禁止事项 |
| [10-agent-api.md](10-agent-api.md) | Agent HTTP API 调用说明 |
| [11-multinode-system-design-reference.md](11-multinode-system-design-reference.md) | 多节点系统设计、Root/节点职责分解和安全确认清单 |
| [runtime/fact-template.md](runtime/fact-template.md) | `ai-deck/fact.md` 的现场事实模板 |

## 关键规则摘要

详细流程和禁止事项见 [09-agent-workflows.md](09-agent-workflows.md)，底层 HTTP 参考见 [10-agent-api.md](10-agent-api.md)。README 只保留入口规则：

- 用户代码只写在 `assets/inputs/*.cs`，不要手工修改 `assets/generated`。
- 默认下载 `<HOST_URI>/api/docs/kit/bundle.zip` 到 `ai-deck/kit-docs/`，使用包内 Python CLI/workflow。
- 常规任务不要手写 curl、shell 转义 sync JSON 或临时部署脚本。
- MCU 节点用 `node program`，Root 逻辑用 `root configure` 和 Root control API。
- 一个 Logic class 只能编程到一个 MCU 节点；多节点必须使用不同 `logicName` 和角色化 LowerIO 字段名。
- 多节点任务先参考 [11-multinode-system-design-reference.md](11-multinode-system-design-reference.md)，确认 Root、各节点、变量流和安全输入职责。
- 涉及真实硬件、车辆运动或危险输出时，Agent 不应默认自动 Start 或写入运动控制量；应先询问用户选择“人类手动 Start”还是“授权 Agent 直接操作”。
- Agent 向用户确认的硬件事实要整理到 `ai-deck/fact.md`，包括型号、接线、极性、端口、波特率、负载、安装位置、减速比、最大速度、控制协议、车辆构型、应用场景和节拍等。
- 信息不足时先追问；执行过程中向用户说明计划、关键结果和验证证据。

## Stub 与示例

[stubs/CartActivator.cs](stubs/CartActivator.cs) 包含用户逻辑可用的类型定义，可供 AI 辅助编码和 IDE 类型提示使用。它不参与实际编译，实际编译由 CoralinkerHost 内置链完成。

[examples/](examples/) 目录包含可运行示例，建议按 hello -> numeric -> port_demo -> car_demo -> dual_node_skeleton 阅读。
