# 2026-06-03 Kit 文档内嵌 Host 与 Agent API 写作要求

本目录记录写作原则和事实，不随 Host 发布，不出现在网页文档中。

## 发布边界

- Kit 文档随 CoralinkerHost 一起发布。
- 发布两种形式：
  - Markdown 源文件：给 Cursor 和其他 Agents 读取，默认入口。
  - HTML 文件：给人阅读，发布时由 Markdown 生成。
- Markdown 内部链接保持 `.md`。
- HTML 内部链接改为 `.html`。
- 浏览器缓存需要用版本号、ETag、Last-Modified、Cache-Control 处理。
- 本轮不处理 product 说明。
- 本轮不处理 LaTeX。
- SDK 开发说明不随网页发布；网页只写“如何使用 SDK/Stub 辅助写用户逻辑”。

## Agent 写作原则

- 假设客户 Agent 不聪明，不能只给 API 列表。
- 必须写清系统对象：
  - Host
  - Project
  - `assets/inputs`
  - `assets/generated`
  - 真实节点
  - 模拟节点
  - Root Runtime
  - Variables
  - Variables Flow
  - WireTap
  - Fatal Error
  - Git 历史
- 每个任务必须写成固定流程：
  - 先调用什么。
  - 再调用什么。
  - 成功看哪个字段。
  - 失败读哪个日志或错误接口。
  - 操作完成后需要刷新哪些状态。
- 必须明确禁止事项：
  - 不直接改 `assets/generated`。
  - 不在 build 进行中写文件。
  - 不绕过 Host Git 历史。
  - 不靠类名猜测多节点编程目标。
  - 不依赖 SignalR 判断 Agent 任务完成。

## 文件同步要求

- Cursor/Agent 不操作浏览器编辑器。
- 推荐流程：
  - `GET /api/files/snapshot`
  - 按需 `GET /api/files/read`
  - 本地修改
  - `POST /api/files/sync`
- `sync` 支持多文件一次提交。
- `sync` 支持 `baseHead`、文件 hash、`commitMessage`、`force`。
- `sync` 应返回 `headBefore`、`headAfter`、`committed`、逐文件结果、冲突列表。
- 保留现有单文件接口供网页使用。

## 节点与模拟节点事实

- 真实节点和模拟节点在 Agent 文档中使用统一生命周期。
- 真实节点：
  - Probe -> Add -> Configure -> Program -> Start -> Observe -> Stop -> Remove。
- 模拟节点：
  - Add Simulated -> Configure -> Program -> Start -> Observe -> Stop -> Remove。
  - URI 形式为 `sim://{uuid}`。
  - 每个模拟节点由独立 `CoralinkerSimNodeHost` 子进程隔离。
  - 默认 layout：32 DI、32 DO、RS485-A、RS485-B、RS232、CAN。
- Agent 应该能添加/删除节点、修改节点参数、创建模拟节点。

## 多节点编程要求

- Build 可能生成多个 logic artifact。
- Agent 必须按 `logicName` 编程节点。
- 一个 Logic class 只能编程到一个 MCU 节点；多节点必须使用不同 Logic class 和角色化 LowerIO 字段名。
- Variables Flow 关系按节点实例 UUID 区分，不按 ClassName 区分。
- 编程前检查节点 layout 和 port configs 是否符合逻辑代码使用的端口。
- 编程后刷新节点信息、节点状态、variables meta、variables values、variables flow。
- 删除节点或更换程序后重新读取 variables/flow。

## 状态刷新要求

- Build 后刷新：
  - `/api/logic/list`
  - `/api/root/logics`
  - `/api/files/tree`
  - `/api/logs/build`
- Program 后刷新：
  - `/api/node/{uuid}`
  - `/api/node/{uuid}/state`
  - `/api/variables/meta`
  - `/api/variables`
  - `/api/variables/flow`
- Start 后刷新：
  - `/api/session/state`
  - `/api/nodes/state`
  - `/api/variables`
  - `/api/logs/terminal`
  - `/api/errors/fatal`
- Agent 优先使用 `/api/agent/state` 做聚合刷新。

## SignalR 取舍

- 网页继续使用 SignalR 做实时同步。
- Agents 默认使用 HTTP command + snapshot + cursor/polling。
- SignalR 对 Agent 是可选高级能力，不作为基础能力依赖。
