# CONTEXT_SUMMARY — CoralinkerHost Variables Flow

> 该文件每次任务结束前会被刷新，记录当前工作上下文，便于下次继续。

## 最近一次支持

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
