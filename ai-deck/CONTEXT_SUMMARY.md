# CONTEXT_SUMMARY — 双节点 RS232 Agent Prompt

> 该文件每次任务结束前会被刷新，记录当前工作上下文，便于下次继续。

## 最近一次支持

- 时间：2026-06-04 18:35 UTC+8
- 事项：发布包含最新前端交互改动的新 Host 包。
- 用户要求：
  - 现在打包一个新的 publish。
- 已执行：
  - 在 `3rd/CoralinkerHost` 执行：
    - `.\publish-host.ps1 -SkipNativeBuild`
  - 说明：
    - 本轮主要是前端/Host 层交互修改，未改 native runtime，因此使用 `-SkipNativeBuild` 复用已有 SimNode native 产物。
- 输出：
  - `3rd/CoralinkerHost/Publish/CoralinkerHost_c7f234b_20260604-183507`
- 验证：
  - 发布命令退出码为 0。
  - `publish-info.json`：
    - `configuration=Release`
    - `tag=c7f234b-dirty`
    - `commit=c7f234b`
    - `skipNativeBuild=true`
    - `nativeBridgeRuntimes=win-x64, linux-x64, linux-arm64`
    - `nativeSimNodeRuntimes=win-x64, linux-x64, linux-arm64`
  - 已确认关键文件存在：
    - `publish-info.json`
    - `CoralinkerHost.dll`
    - `wwwroot/index.html`
    - `wwwroot/assets`
    - `res/docs/kit/md/README.md`
    - `wwwroot/docs/kit/README.html`
    - `simnode/CoralinkerSimNodeHost.dll`
- 注意：
  - 发布过程中 Vite warning 仍为既有提示：SignalR pure annotation、`device.ts` dynamic/static import、chunk size 偏大。
  - dotnet publish 仍有既有 nullable/unused field warnings，未阻塞发布。

## 上一次支持

- 时间：2026-06-04 17:42 UTC+8
- 事项：修正 Graph 变量拾取模式的遮挡层级与缩放控件可用性。
- 用户要求：
  - 进入拾取模式后 ControlPanel 仍在前台遮住 Graph，应该全面隐藏 ControlPanel 和 Configure Panel。
  - Graph 内除了变量框以外的东西加一层蒙影。
  - 左下角 `+/-/[]` 缩放控件在拾取模式下需要仍可用。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/components/control/ControlWindow.vue`
    - ControlWindow 根节点改为 `v-if="visible && !graphVariablePickActive"`，拾取期间完整不渲染 ControlPanel 和配置弹窗。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`
    - 新增 `graph-pick-dim` 视觉蒙影层，位于 VueFlow 内容上方、变量层下方。
    - 蒙影层 `pointer-events: none`，不阻挡缩放控件。
    - Variables Flow layer 在拾取模式下保持 `pointer-events: none`，只让变量框自身接收点击。
    - Graph wrapper 使用 `@click.capture` 处理空白取消。
    - 点击变量框不取消，由变量框自身完成绑定。
    - 点击 `.vue-flow__controls` 或 `.vue-flow__minimap` 不取消，保证缩放控件可用。
- 验证：
  - `ReadLints` 检查 `ControlWindow.vue`、`GraphCanvas.vue` 无错误。
  - `npx vue-tsc -b` 通过。
  - `npx vite build` 通过。
  - `git diff --check` 通过，仅有 Windows LF/CRLF 提示。

## 更早一次支持

- 时间：2026-06-04 17:18 UTC+8
- 事项：ControlPanel 变量绑定支持从 Graph 变量框拾取。
- 用户要求：
  - 遥控器绑定变量时，变量列表太长，需要支持从 Graph 里的变量框点击选择。
  - 变量列表右侧加 `Select From Graph` 按钮。
  - 点击后 ControlPanel 更透明，焦点来到 Graph。
  - 能绑定的变量保持彩色，不能绑定的变量变灰。
  - Hover 时颜色加深。
  - 单击变量绑定成功，点击别的地方绑定失败/取消。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/stores/ui.ts`
    - 新增 `graphVariablePickRequest` / `graphVariablePickResult` 全局状态。
    - 新增 `startGraphVariablePick()`、`finishGraphVariablePick()`、`cancelGraphVariablePick()`、`clearGraphVariablePickResult()`。
  - `3rd/CoralinkerHost/ClientApp/src/components/control/ControlWindow.vue`
    - 在 joystick X/Y、slider、switch、gauge、lamp 的变量选择框右侧增加 `Select From Graph`。
    - 发起拾取时传入允许绑定的变量名：
      - joystick/slider/switch：`controllableVarList`。
      - gauge/lamp：`allVarList`。
    - 拾取期间 ControlWindow 与配置弹窗半透明。
    - Graph 返回变量名后写回对应字段，并对 joystick/slider 自动刷新默认范围。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`
    - 响应 Graph 变量拾取模式。
    - 可绑定变量保持彩色，hover 增亮并轻微上移。
    - 不可绑定变量置灰，hover 只轻微增亮。
    - 点击可绑定变量成功回传；点击不可绑定变量或空白区域取消。
- 验证：
  - `ReadLints` 检查 `ui.ts`、`ControlWindow.vue`、`GraphCanvas.vue` 无错误。
  - `npx vue-tsc -b` 通过。
  - `npx vite build` 通过。
  - `git diff --check` 通过，仅有 Windows LF/CRLF 提示。
- 注意：
  - Vite 仍有既有 warning：SignalR pure annotation、`device.ts` dynamic/static import、chunk size 偏大。

## 更早一次支持

- 时间：2026-06-04 17:12 UTC+8
- 事项：为 ControlPanel 绑定变量增加 VarFlow 特殊样式，并统一变量框尺寸。
- 用户要求：
  - 被遥控器面板（ControlPanel）绑定的变量需要特殊表示，能直观看到绑定关系。
  - 可以用样式表示，例如变量框背景带条纹渐变。
  - 变量框最小宽度加宽，避免 float 值导致宽度频繁调整。
  - ControlItem 和普通变量的高度/间距 CSS 不一致，需要修正。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`
    - `FLOW_ITEM_WIDTH` 从 `210` 调整为 `220`。
    - `CONTROL_ITEM_MIN_WIDTH` 从 `124` 调整为 `168`。
    - `CONTROL_ITEM_MAX_WIDTH` 从 `220` 调整为 `260`。
    - `FLOW_ITEM_MIN_WIDTH` 从 `132` 调整为 `168`。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`
    - 从 `projectStore.controlLayout.widgets` 收集 ControlPanel 绑定变量名：
      - `config.variable`
      - `config.variableX`
      - `config.variableY`
    - 对绑定变量框添加 `control-panel-bound` class。
    - 新增斜向条纹覆盖层和更亮描边，保留 upper/lower/control 原有颜色。
    - 移除 `.control-io` 更小高度/字号/行高覆盖，让 ControlItem 与普通变量使用一致网格高度和字体。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue`、`variableFlowLayout.ts` 无错误。
  - `npx vue-tsc -b` 通过。
  - `npx vite build` 通过。
  - `git diff --check` 通过，仅有 Windows LF/CRLF 提示。

## 更早一次支持

- 时间：2026-06-04 17:03 UTC+8
- 事项：取消 Graph 节点自由拖动和节点顺序持久化。
- 用户要求：
  - 取消 Node 可以自由拖动的特性。
  - Project 不再保存和解析节点顺序。
  - 直接按照创建顺序渲染。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`
    - `VueFlow` 的 `nodes-draggable` 固定为 `false`。
    - 删除 `@node-drag-stop`。
    - Coral 节点在 `applyFixedLayout()` 中固定 `draggable: false`。
    - 删除 `readNodeOrder()`、`currentNodeOrder()`、`saveNodeOrder()`、`targetNodeOrderIndex()`、`applyNodeOrderLocally()`。
    - 新增节点不再写入 `extraInfo.order`。
    - 加载后端节点时不再解析/保留 `extraInfo` 作为排序依据。
    - 删除添加/删除节点后的顺序保存调用。
    - 固定布局传入 `nodeOrder: []`，让节点按 `coralNodes.value` 的创建/返回顺序渲染。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue` 无错误。
  - `npx vue-tsc -b` 通过。
  - `npx vite build` 通过。
  - `git diff --check -- GraphCanvas.vue` 通过，仅有 Windows LF/CRLF 提示。
- 注意：
  - Vite 仍有既有 warning：SignalR pure annotation、`device.ts` dynamic/static import、chunk size 偏大。

## 更早一次支持

- 时间：2026-06-04 17:00 UTC+8
- 事项：修复 VarFlow 分组后 Root 出线槽位顺序与视觉顺序不一致导致的交叉。
- 用户反馈：
  - 截图中 `ToA7` 与 `FromA1..FromA5` 聚合线发生交叉。
  - 用户指出 `ToA7` 和 `FromA1..FromA5` 的聚合顺序不对。
- 根因：
  - `routeVariableLines()` 中主连线目标使用 `groups + 未分组 items` 的数组顺序分配 Root 底边/节点顶边槽位。
  - 当同关系变量超过 6 个被拆成“分组框 + 单变量列”（例如 `ToA1..ToA6` 和 `ToA7`）时，视觉 X 顺序与槽位分配顺序可能不一致，导致 Root 侧锚点错位并交叉。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`
    - `routeItems` 改为按屏幕横向中心点 `x + width / 2` 排序。
    - 新增 `compareRouteItemPosition()`，同 X 时按 Y 排序。
    - Root bottom slots 与 node top slots 现在按视觉顺序分配。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts` 无错误。
  - `npx vue-tsc -b` 通过。

## 更早一次支持

- 时间：2026-06-04 16:53 UTC+8
- 事项：按 Kit Docs Python CLI 在 `http://localhost:4499` 创建 VarFlow 双模拟节点测试工程。
- 用户要求：
  - 使用 `3rd/CoralinkerKitDocs` 里的 Python API/CLI。
  - 新建工程，模拟两个节点，每个节点多弄几个变量，用于查看 VarFlow。
- 已执行：
  - `python 3rd/CoralinkerKitDocs/tools/agent_cli/coral_agent.py --host http://localhost:4499 docs download --out ai-deck/kit-docs --bundle`
  - `python ai-deck/kit-docs/tools/agent_cli/coral_agent.py --host http://localhost:4499 state`
  - 新建临时目录：`ai-deck/agent_work/20260604-1653-varflow-two-sim/`
    - `desc.md`
    - `VarFlowTwoSim.cs`
  - `project new`
  - `files sync --path assets/inputs/VarFlowTwoSim.cs`
  - `build`
  - 添加两个模拟节点：
    - A：`04b7b024-8a6e-49f1-ad99-0602956df4ef`，`VarFlow Sim A`
    - B：`2832b8fb-f371-46f6-a744-996b47ca93ee`，`VarFlow Sim B`
  - 编程：
    - A -> `VarFlowNodeALogic`
    - B -> `VarFlowNodeBLogic`
  - Root 配置：
    - `VarFlowRootLogic`
  - 启动：
    - `start --require-all` 成功，`successNodes=2/2`。
- 验证：
  - Build 成功，产物：
    - MCU：`VarFlowNodeALogic`、`VarFlowNodeBLogic`
    - Root：`VarFlowRootLogic`
  - `variables flow` 返回 Root、A、B 和多组关系变量。
  - `node states` 返回两个模拟节点均 `running`、`isProgrammed=true`、`isConfigured=true`。
- 注意：
  - `/api/about` 显示 `localhost:4499` 当前运行的是 `Published` 包，buildTime `2026-06-04T12:14:53`，早于 16:48 的 VarFlow 前端修改。
  - 后端工程和变量关系已创建成功；要在该端口网页看到最新分组框前端，需要重新发布/重启到包含最新前端构建的 Host。

## 更早一次支持

- 时间：2026-06-04 16:48 UTC+8
- 事项：继续完成前端 VarFlow 自动布局与合并分组框修改。
- 用户要求：
  - 取消变量拖动排序，用户不需要手动排变量。
  - 新变量出现后自动重新排序和重新计算布局。
  - Root 与节点之间横排变量按关系优化，例如 `ToA FromA ToB FromB`，减少交叉。
  - 同方向、同来源、同去向的多个变量允许竖向堆叠；外面加大框；每组最多 6 个变量；有合并变量时 Root 与节点竖向间距要相应变大。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`
    - 增加 `VariableFlowGroup`，横排变量可按最多 6 个一组生成竖向分组框。
    - 横排变量改为基于关系自动排序，不再依赖项目内保存的 `variableFlowOrder`。
    - 分组框参与连线路由，已合并变量不再逐个从 Root/Node 拉主关系线，而是连到分组框。
    - 分组高度参与 `nodesY` 计算，堆叠变量会自动加大 Root 与节点之间的竖向间距。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`
    - 渲染 `.variable-flow-group` 大框。
    - 移除变量卡片 drag/drop、拖拽样式和变量顺序保存 watcher。
    - 画布尺寸计算纳入分组框边界。
- 验证：
  - `npx vue-tsc -b` 通过。
  - `npx vite build` 通过。
  - `git diff --check` 检查 VarFlow 两个前端文件通过，仅有 Windows LF/CRLF 提示。
- 注意：
  - Vite 输出仍有既有 warning：SignalR pure annotation、`device.ts` dynamic/static import、chunk size 偏大。

## 更早一次支持

- 时间：2026-06-03 15:52 UTC+8
- 事项：编写简单的双节点 + RS232 调试 Agent Prompt，供复制给外部 Agent 使用。
- 用户要求：
  - 写 Prompt，不写 `DEV_LOG.md`。
- 已执行：
  - 新增 `ai-deck/AGENT_PROMPT_DUAL_NODE_RS232.md`：
    - 文档入口 + Host API 闭环。
    - 双节点分工：`Rs232TxNode` / `Rs232RxNode`，LowerIO 计数与解析状态。
    - 可选 Root ControlItem。
    - RS232 调试：确认端口/波特率/线序、WireTap TX/RX、变量对照、RX=0 排查顺序。
    - 信息不足先追问；文末留「用户补充」填空区（节点类型、URI、端口、接线等）。
- 未执行：
  - 未改 Host 代码、未 publish、未写 `DEV_LOG.md`。

## 更早一次支持

- 时间：2026-06-03 15:50 UTC+8
- 事项：发布包含缩短 Agent Prompt 和硬件补充追问规则的新 Host 包。
- 输出目录：`3rd/CoralinkerHost/Publish/CoralinkerHost_c7f234b_20260603-155042`
