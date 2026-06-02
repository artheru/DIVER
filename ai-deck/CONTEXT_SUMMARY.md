# CONTEXT_SUMMARY — CoralinkerHost Variables Flow

> 该文件每次任务结束前会被刷新，记录当前工作上下文，便于下次继续。

## 最近一次支持

- 时间：2026-06-02 21:22 UTC+8
- 事项：修正 Variables Flow 配色与无消费者变量连线。
- 用户反馈：
  - Graph 中变量框/连线配色与最初规则不一致，应以 Variables 表格/图例为准。
  - 只有 Root 时，`left_diff_speed` 这类变量如果没有消费者，应只显示进入 Root 的线，不应再画从 Root/变量出去的线。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - VarFlow 线条和变量框颜色改为与 `VariablePanel.vue` 表格/图例一致：
      - `UpperIO`：绿色。
      - `LowerIO`：橙色。
      - `MutualIO`：紫色。
      - `ControlItem`：蓝色。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - `routeVariableLines()` 不再在 `readerIds` / `writerIds` 为空时自动 fallback 到 `sourceIds`。
    - 空 reader/writer 现在表示“没有消费者/生产者”，因此不会再因为声明者 `sourceIds` 被同时当成读写方而画出多余反向线。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue`、`variableFlowLayout.ts` 无错误。
  - `npm run build` 成功；仍有既有 Vite/Rollup warning。

## 上一次支持

- 时间：2026-06-02 21:18 UTC+8
- 事项：修复开发目录启动 Host 后 Build 找不到离线 NuGet 包，并让前端 Build 失败弹窗可见。
- 用户反馈：
  - 本地开发目录启动的后端调用 Build 报错：`Missing offline NuGet packages directory: ...\DiverCompilerPortable\bin\Debug\netstandard2.0\nuget-packages`。
  - 前端只在 Console/短通知里显示错误，用户看不到，需要弹窗。
  - 验证时如果 Host 正在运行锁住输出文件，应先停下来告诉用户，不再绕到临时输出目录。
- 已执行：
  - `3rd/CoralinkerHost/Services/DiverBuildService.cs`：
    - Build 写入 `LogicBuild.csproj` 后，NuGet source 不再固定只取 `compilerDir\nuget-packages`。
    - 新增开发模式 NuGet 包目录解析：
      - 优先使用发布/打包目录中的 `nuget-packages`。
      - Published layout 缺少该目录仍直接报错，避免发布包漏文件被掩盖。
      - Development layout fallback 到 `CORALINKER_NUGET_PACKAGES_DIR`、`NUGET_PACKAGES`、用户目录 `.nuget/packages`。
    - Build 日志输出实际使用的 NuGet package source。
    - 清理同文件中一个未使用异常变量 warning。
  - `3rd/CoralinkerHost/ClientApp/src/views/HomeView.vue`：
    - 新增 Build Failed modal，显示完整错误文本。
    - Build 返回 `ok=false` 或 `/api/build` 抛异常时都会打开弹窗。
    - 保留原有 toast，并把错误追加到 Build 日志。
    - 弹窗提供 `Open Build Log` 按钮切到 Build 日志面板。
- 验证：
  - `ReadLints` 检查 `DiverBuildService.cs`、`HomeView.vue` 无错误。
  - `npm run build` 成功；仍有既有 Vite/Rollup warning。
  - 第一次 `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Debug` 被正在运行的 Host 锁住输出文件；用户停止后直接重跑成功，0 error，仍有既有 nullable/field warning。

## 上一次支持

- 时间：2026-06-02 20:24 UTC+8
- 事项：实现固定 Variables Flow / Graph 层级布局计划（未修改计划文件）。
- 已执行：
  - `variableFlowLayout.ts` 改为确定性固定层级布局：
    - Root 居上，宽度随 ControlItem 数量增加。
    - ControlItem 变量在 Root 下方一字排开。
    - Upper/Lower/Mutual IO 在 Root 与 Nodes 中间一字排开。
    - Nodes 按顺序在下方排列，单 Node 居中，多 Node 从左到右。
    - `__` 开头变量过滤不显示、不进入排序。
    - 线改为低成本 slot-based orthogonal path，不做 A* 或候选评分。
  - `GraphCanvas.vue`：
    - VarFlow 默认常开，挂载后刷新 variables/metas。
    - 不再保存随机/自由 `x/y` 位置。
    - Root 不允许拖动。
    - Node 拖拽结束后按最近水平槽位移动顺序，并写入各节点 `extraInfo.order`。
    - 变量框支持拖拽重排，保存 `variableFlowOrder`。
  - 项目级状态新增 `variableFlowOrder`：
    - 前端 `ProjectState` / `projectStore` 支持读写。
    - 后端 `ProjectState` / `ProjectStore` 保存到 `project.json`，随项目导入导出保留。
  - `RootNodeView.vue`、`CoralNodeView.vue` 根元素补 `width: 100%`，让 VueFlow 固定宽度生效。
  - 追加修复普通 Node/Root 加载后不可见问题：
    - 后端 `/api/nodes` 确认仍有 MCU 节点，问题在前端显示/视口。
    - 固定布局不再把 IO 变量总宽度计入 Root/Node 布局中心，避免变量很多时把节点整体推到右侧不可见区域。
    - 移除 VueFlow `fit-view-on-init`，避免空图初始化时提前 fit；改为加载/新增节点完成后手动 `fitView()`。
    - 默认 viewport 改回 `{ x: 0, y: 0, zoom: 1 }`。
    - 临时增加 `[Graph] Fixed layout applied` 日志，输出 Root/Node 固定坐标，便于继续确认是否仍有显示问题。
  - 二次修复“刷新后节点闪现后消失”：
    - 判断后端字段不是主因，因为节点已先闪现，说明前端已经拿到并渲染过节点。
    - 移除 GraphCanvas 固定布局对 DOM 实测尺寸的依赖。
    - 删除 `measuredNodeRects` / `measureNodeRects()` / `getNodeRect()` 等测量反馈链路。
    - `flowCanvasSize` 改用固定布局输出的 Root/Node rect 和变量 item 计算。
    - `syncFlowTransform()` 只同步 VueFlow transform，不再触发布局测量。
  - UI 细节调整：
    - 普通 Node 固定宽度从 `320` 加到 `420`，减少端口/内容溢出。
    - 变量框宽度不再固定 210px，改为按 type/name/value 内容估算并应用到 DOM。
    - Variables Flow 连线从横平竖直折线改为三次贝塞尔曲线。
    - 连接锚点 slot margin 加大，并给多条线加入 lane offset，让连接位置更分散。
  - Root/ControlItem 位置修复：
    - Root 高度随 ControlItem 行增加，不再让 ControlItem 溢出到 Root 外部。
    - ControlItem 放入 Root 内部底部区域。
    - Root 连线锚点改为 Root 底边 slot，并使用更大 inset 分散位置。
    - `RootNodeView.vue` 根节点高度填满 VueFlow 指定高度，内容区增加底部 padding 给 ControlItem 留空间。
  - 继续修复 Root 内容和变量框：
    - `ROOT_SIZE.height` / `ROOT_BASE_HEIGHT` 增加到 190，并在有 ControlItem 时再增加固定 control 区域高度。
    - 变量框 CSS 显式两列两行：左侧 type 跨两行，右侧 name/value 分两行。
    - 变量框宽度估算改为 `type + max(name,value)`，匹配两排布局。
    - 内部变量过滤扩展到带节点前缀的 `__` 隐藏标记，例如 `Node-...__iteration` 不显示。
  - 修复 Root 选择逻辑后的视图漂移和 Project 保存覆盖：
    - 固定变量流布局使用 Root 中心作为图坐标 `X=0` 基准，不再根据变量行/节点行宽度重新计算整体中心。
    - 移除 `fitView()` 和固定布局调试日志调用；首次加载时只把视口对准 Root，新增变量/节点不再主动移动视图。
    - Root 逻辑选择成功后同步 `projectStore.rootLogicName` 并保存 Project，避免后续变量顺序/节点顺序保存用旧的 `rootLogicName` 覆盖后端配置。
  - 继续收敛 Variables Flow 视觉和废弃连接点：
    - 变量框类型显示与节点 Table 对齐，`Int32/Single` 等显示为 `i32/f32`。
    - 变量框缩窄，name/value 都右对齐。
    - 变量框上下连线锚点改为单线居中、多线均匀分布。
    - 变量流线条改为实线。
    - 删除 Root/Coral 节点上废弃的 VueFlow `Handle`、`in/out` 标签、蓝色连接点样式和连接事件处理。
  - 修复变量流“全连接”问题：
    - 变量流箭头/线条改为完全不透明。
    - SDK `CartFieldMeta` / `CartFieldValue` 增加 `sourceIds`、`readerIds`、`writerIds`，均使用 className/LogicName，而不是 UUID。
    - SDK 聚合每个变量的真实声明关系：
      - MCU `UpperIO`：当前 className 作为 reader。
      - MCU `LowerIO`：当前 className 作为 writer。
      - MCU `Mutual`：当前 className 同时作为 reader/writer。
      - Root cart `upper`：Root className 作为 writer。
      - Root cart `lower`：Root className 作为 reader。
      - Root cart `mutual`：Root className 同时作为 reader/writer。
    - Host SignalR 变量快照透传这些关系字段。
    - 前端 `runtimeStore` 保存关系字段，`GraphCanvas` 把节点 `logicName` 和 Root `rootLogicName` 传入 VarFlow。
    - `variableFlowLayout.ts` 按 reader/writer className 选择实际节点连线，不再把每个变量连到所有 Node/Root。
- 验证：
  - `ReadLints` 检查相关前端/后端文件无错误。
  - `npm run build` 成功；显示修复后已重跑通过，仅有既有 Vite/Rollup warning。
  - `dotnet build CoralinkerHost.csproj` 成功，仅有既有 nullable / Windows-only API warning。

## 上一次支持

- 时间：2026-06-02 19:55 UTC+8
- 事项：修订固定 Variables Flow 布局计划。
- 用户补充约束：
  - 变量交换顺序后，如果重新编译没有新增变量，顺序保持不变。
  - 如果重新编译后出现新变量，新变量插入到合适位置，已有变量相对顺序保持。
  - 变量名不可能重名，保存 key 可使用变量名。
  - `__` 开头变量不参与显示，例如 `__iteration`。
- 已执行：
  - 更新计划文件：`c:\Users\lvzhe\.cursor\plans\固定变量流布局_c57cb37a.plan.md`。
  - 计划中移除“变量名可能重名”的风险项。
  - 计划中新增 `__` 变量过滤和新变量插入策略。

## 上一次支持

- 时间：2026-06-02 19:36 UTC+8
- 事项：拆分 Variables Flow 布局/布线模块，并撤掉高开销 A* 路由。
- 用户反馈：
  - 问题不只是线避让，还包括变量位置安排，整体更像 PCB 的布局+布线。
  - `GraphCanvas.vue` 太长，需要把布局布线单独拆出文件。
  - 用户建议参考 ComfyUI；检索后确认 ComfyUI/LiteGraph 的连线渲染以 slot-based link rendering 为主，支持 Straight/Linear/Spline，不做每帧全局 maze routing。
- 已执行：
  - 新增 `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - 导出 `computeVariableFlowLayout()`。
    - 包含变量框候选位置评分、变量顺序候选、slot 锚点、正交 polyline 路径生成、几何评分工具。
    - 统一导出 `NodeRect`、`VariableFlowItem`、`FlowLine`、`ROOT_SIZE`、`NODE_SIZE` 等类型/常量。
  - `GraphCanvas.vue`：
    - 删除旧的大段布局/布线/几何/A* 函数。
    - 只保留节点 DOM 测量、VueFlow 渲染和调用 `computeVariableFlowLayout()`。
    - 撤掉高开销 A* 路由，改回低成本、Comfy/LiteGraph 风格的 slot-based orthogonal link rendering，避免拖动时卡顿。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue`、`variableFlowLayout.ts` 无错误。
  - `npm run build` 成功，只有既有 Vite/Rollup warning。

## 上一次支持

- 时间：2026-06-02 19:27 UTC+8
- 事项：修正 Variables Flow 线缠绕和锚点不在最近边的问题。
- 用户反馈：
  - 线仍然缠在一起。
  - 线没有落在最近的边缘上。
- 已执行：
  - `GraphCanvas.vue`：
    - `anchorToward()` 不再按上下/左右阈值粗略选边，改为比较目标点到矩形四边距离，选择最近边。
    - 最近边上的端点坐标使用“目标点投影 + slot 位置”的混合：
      - 保证端点贴近最近边。
      - 同一边多条线仍能轻微分散，不完全重合。
    - `curvePath()` 增加 lane 参数：
      - 横向曲线在 y 方向按 lane 分层。
      - 纵向曲线在 x 方向按 lane 分层。
      - 避免多条线在中间合成一束。
    - 实际绘制处把变量序号传入 `curvePath()`，让每个变量对应一条稳定 lane。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue` 无错误。
  - `npm run build` 成功，只有既有 Vite/Rollup warning。

## 上一次支持

- 时间：2026-06-02 19:18 UTC+8
- 事项：增强 Variables Flow 的 J/effort 优化：端点分散、变量顺序参与评分、上下/左右曲线自适应。
- 用户反馈：
  - 进出线端点不要挤在一起，应该分开一点。
  - 线可以像示意图一样按弧线排列。
  - 多节点情况下变量的位置和顺序都可以调整，目标是让总体 J/effort 最小。
- 已执行：
  - `GraphCanvas.vue`：
    - 新增 `FlowLayoutResult`，布局结果包含变量组原点和优化后的变量顺序。
    - 变量数量 `<= 6` 时对变量顺序做全排列参与评分；更多变量时使用原顺序、反序、按方向启发式排序等候选。
    - 真实绘制和布局评分都使用同一套 slot 锚点：
      - Root/Node 边上的端点按变量序号分散。
      - 变量框边上的端点按 root/target slot 分散。
    - `anchorToward()` 改为可按上下/左右方向自动选边：
      - 另一端主要在上/下方时，锚到 top/bottom 边。
      - 另一端主要在左/右方时，锚到 left/right 边。
      - 同一边上按 slot 分配 0..1 的位置。
    - `curvePath()` 改为根据端点关系选择控制点：
      - 上下关系使用竖向控制点。
      - 左右关系使用横向控制点。
    - 评分函数也使用 slot 后的虚拟线段，避免评分认为可行、实际绘制又挤在一起。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue` 无错误。
  - `npm run build` 成功，只有既有 Vite/Rollup warning。

## 上一次支持

- 时间：2026-06-02 19:08 UTC+8
- 事项：将 Variables Flow 变量布局改为轻量自动占位/推挤评分算法。
- 用户反馈：
  - 横向排列时变量放中间是好的。
  - Root/Node 竖向排列时，变量框继续横向卡在中间会挡住节点和线。
  - 希望变量框、连线都参与自动推挤；前端运算频次不高，可以多算一点。
- 已执行：
  - `GraphCanvas.vue` 中重写外部变量布局：
    - 变量框不再“先选一列再上下推”。
    - 改为把所有外部变量先视作一个 group/block。
    - 生成中间、上、下、左、右、四角和网格候选位置。
    - 对候选位置打分，评分包含：
      - 变量组是否压 Root/Node。
      - 连线线段是否穿过 Root/Node 占用区。
      - 总线长。
      - 与 Root/Node 中心区域的距离。
    - 增加竖向拓扑偏置：
      - 当 Root 和 Node 主要是上下关系时，惩罚停留在节点包围盒的中间高度带。
      - 优先把变量组推到节点组外侧；Root 在上、Node 在下时更倾向放到下方。
  - 新增几何工具函数：
    - `uniquePoints()`
    - `distance()`
    - `overlapArea()`
    - `segmentIntersectsRect()`
    - `segmentsIntersect()`
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue` 无错误。
  - `npm run build` 成功，只有既有 Vite/Rollup warning。

## 上一次支持

- 时间：2026-06-02 18:40 UTC+8
- 事项：修复 Variables Flow 箭头方向语义错误。
- 用户反馈：
  - 截图显示方向“好像都很不对”。
- 根因/判断：
  - 上一版把 `upper` / `lower` 的语义反了。
  - 正确语义应为：
    - `upper` / `AsUpperIO`：Root/上位机 -> 变量 -> MCU 节点。
    - `lower` / `AsLowerIO`：MCU 节点 -> 变量 -> Root/上位机。
  - 变量框可能被布局到节点左侧、右侧或中间，因此不能固定使用变量框左/右端点。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - `upper` 路径改为 Root -> variable -> Node。
    - `lower` 路径改为 Node -> variable -> Root。
    - `mutual` 临时绘制双向路径，避免单箭头误导。
    - 新增 `itemToRect()`，让变量框也走矩形锚点计算。
    - 每段路径的源/目标端点都用 `anchorToward(rect, point)` 动态选择边，避免变量框位置变化后箭头方向反掉。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue` 无错误。
  - `npm run build` 成功，只有既有 Vite/Rollup warning。

## 上一次支持

- 时间：2026-06-02 18:31 UTC+8
- 事项：修复 Variables Flow 连线端点缩放/偏移和变量框拥挤遮挡问题。
- 已执行：
  - SVG `viewBox`、`width`、`height`、CSS 尺寸保持一致，避免 SVG 内部二次缩放。
  - 连线锚点改为 `anchorToward(rect, point)`。
  - 外部变量框改为自动布局：候选列评分、Root/Node padding 后碰撞检测、变量框纵向避让。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue` 无错误。
  - `npm run build` 成功。
