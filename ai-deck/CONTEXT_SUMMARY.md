# CONTEXT_SUMMARY — CoralinkerHost Variables Flow

> 该文件每次任务结束前会被刷新，记录当前工作上下文，便于下次继续。

## 最近一次支持

- 时间：2026-06-03 12:34 UTC+8
- 事项：补充忽略 SimNode/MCURuntime native build 输出。
- 用户反馈：
  - `3rd/CoralinkerSimNodeHost/build/runtimes/*` 没有被 `.gitignore` 忽略。
- 已执行：
  - `.gitignore`：
    - 新增 `/3rd/CoralinkerSimNodeHost/build/`。
    - 新增 `/MCURuntime/build/`。
    - 保持 `build-native.ps1` 不受影响。
- 验证：
  - `git check-ignore -v 3rd/CoralinkerSimNodeHost/build/runtimes/linux-arm64/native/libsim_node_runtime.so MCURuntime/build` 命中新增规则。
  - `git status --short -- "3rd/CoralinkerSimNodeHost" "MCURuntime/build" ".gitignore"` 仅显示 `.gitignore` 修改和 SimNode 源码目录未跟踪，build 输出不再单独暴露。

## 上一次支持

- 时间：2026-06-03 12:31 UTC+8
- 事项：提交前 diff review，并修正残留变量问题的实现方向。
- 用户反馈：
  - `DiverCompiler/Processor.cs` 的 `cart_io_list` 改动风险较高，可能影响 MCU runtime program descriptor 和执行。
  - 未使用变量残留更可能是 `_variables` 全局存储在重新 Program 后没有清理。
- 已执行：
  - 撤回 `DiverCompiler/Processor.cs` 的 `cart_io_list` 过滤改动，恢复编译器原有输出逻辑。
  - `3rd/CoralinkerSDK/DIVERSession.cs`：
    - 新增 `PruneUndeclaredVariables()`。
    - `ProgramNode()` 在解析新 `MetaJson` 并初始化变量后，清理不再被任何节点/Root 声明的 `_variables` 项。
    - `RemoveNode()`、`UnregisterVirtualNode()` 后也调用清理，避免删除节点/Root 后变量残留。
- Review 结论：
  - `CORAL-NODE-V2.1` LaTeX 文件按用户要求忽略。
  - `3rd/CoralinkerSimNodeHost/` 目录是新工程，提交时需要只加入源码/项目/脚本/native shim，避免把 `build/runtimes/*` 产物一并加入。
  - `MCURuntime/build/` 是未跟踪生成目录，不应提交；当前 glob 未发现其中有文件。
  - `3rd/CoralinkerHost/CoralinkerHost - Backup.csproj` 被删除，若这是手工备份文件则可接受，否则提交前需确认。
- 验证：
  - `ReadLints` 检查 `DiverCompiler/Processor.cs`、`DIVERSession.cs` 无错误。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj` 成功。
  - `npm run build` 成功。
  - `git diff --check -- . ":(exclude)CORAL-NODE-V2.1/**"` 通过，仅有既有换行提示。

## 上一次支持

- 时间：2026-06-03 12:25 UTC+8
- 事项：修复 VarFlow 节点边缘出线锚点集中问题。
- 用户反馈：
  - 节点出线位置需要在对应边上均分，不能集中在一点。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - `routeVariableLines()` 先扫描所有 center flow 线，分别为 Root bottom 和每个节点 top 建立 slot 列表。
    - Root 的出入线使用 Root bottom 自己的 slotCount 均分。
    - 普通节点的出入线使用该节点 top 边自己的 slotCount 均分。
    - 不再用全局 `itemIndex/items.length` 计算节点边缘锚点，避免局部线条集中。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts` 无错误。
  - `npm run build` 成功。
  - 剩余 warning 为既有 Vite/Rollup chunk 与 SignalR annotation warning。

## 上一次支持

- 时间：2026-06-03 12:17 UTC+8
- 事项：修复未使用 Cart 字段仍进入变量表和 VarFlow 的问题。
- 用户反馈：
  - 节点第一轮代码使用变量 A/B/C/D。
  - 后续 Cart 定义仍保留 C/D，但 Operation 代码不再使用 C/D。
  - 重新编译后 C/D 仍出现在变量表，并且 VarFlow 仍然连到节点。
  - 实际未被任何地方使用的 Cart 字段不应存在于变量表。
- 已执行：
  - `DiverCompiler/Processor.cs`：
    - `SI.cart_io_list` 不再从 `SI.sfield_offset.field_offset` 中所有 CartDefinition 字段生成。
    - 改为从 `SI.referenced_typefield` 中实际被 IL 字段访问引用到的 Cart 字段生成。
    - 保留按字段 offset 排序，保证 `.bin.json` 和 VM program descriptor 的 IO 顺序一致。
    - 使用兼容 netstandard 的 `new HashSet<string>(..., StringComparer.Ordinal)`，避免 `ToHashSet()` 不可用。
- 影响：
  - 后续 Build 生成的 `.bin.json` 将只包含代码实际引用的 Cart 字段。
  - 保留在 CartDefinition 但未被 Operation/相关被编译方法使用的字段，不会再进入 Host 变量表和 VarFlow。
  - 已经编译/编程过的节点需要重新 Build 并 Program，才能清掉旧产物中的 C/D。
- 验证：
  - `ReadLints` 检查 `DiverCompiler/Processor.cs` 无错误。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj` 成功。
  - 剩余 warning 为既有 `Processor.cs` unreachable code warning。

## 上一次支持

- 时间：2026-06-03 12:14 UTC+8
- 事项：修复 VarFlow 侧边贝塞尔曲线回折。
- 用户反馈：
  - 侧边贝塞尔控制点设置不合理，短距离连接时出现回折。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - `sideBezierPath()` 不再使用固定最小 `54px` 控制点距离。
    - 控制点距离改为基于实际 `dx` 计算，并限制在端点水平范围内。
    - lane 偏移也按 `dx` 限制，避免控制点越界导致曲线折返。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts` 无错误。
  - `npm run build` 成功。
  - 剩余 warning 为既有 Vite/Rollup chunk 与 SignalR annotation warning。

## 上一次支持

- 时间：2026-06-03 12:12 UTC+8
- 事项：修正 Root 右侧变量排列方向。
- 用户反馈：
  - Root 右侧变量不是从 Root 下方继续向下排。
  - 应从 Root 右侧开始向上排列。
  - 例如 5 个变量时，第 5 个在最下面，然后向上依次是 4、3、2、1。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - `layoutGapVariables()` 计算 Root 右侧变量栈总高度并传入 `gapVariableY()`。
    - `ROOT_RIGHT_GAP` 的 Y 起点改为 `rootRect.y + rootRect.height - stackHeight`。
    - 保持变量原始顺序从上到下排列，因此视觉上从下往上是最后一个、倒数第二个、...、第一个。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts` 无错误。
  - `npm run build` 成功。
  - 剩余 warning 为既有 Vite/Rollup chunk 与 SignalR annotation warning。

## 上一次支持

- 时间：2026-06-03 12:10 UTC+8
- 事项：调整 VarFlow 变量宽度估算、Root 侧变量起始位置、侧边曲线间距。
- 用户反馈：
  - 自动计算的变量占据长度偏大，类型和名字之间空隙过多，需要更贴近实际控件宽度。
  - PC/Root 出入的侧边变量不应从 Root 侧面上方开始排，应该从 Root 下方开始计算。
  - 侧边弧线仍有概率重叠，可以把节点和变量表之间的间隙调大。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - `FLOW_ITEM_MAX_WIDTH` 从 `420` 收紧到 `320`。
    - 变量名宽度估算从 `name.length * 7.2` 调整为 `name.length * 6.2`，上限从 `300` 调整为 `240`。
    - 变量框额外宽度从 `38` 调整为 `24`，减少类型和名称之间的无效空隙。
    - `ROOT_RIGHT_GAP` 的变量 Y 起点改为 `rootRect.y + rootRect.height + SIDE_VARIABLE_STACK_GAP`，即从 Root 下方开始排列。
    - `SIDE_VARIABLE_GAP` 从 `24` 增加到 `40`，拉大节点/Root 与侧边变量列之间的水平间隙，降低弧线重叠概率。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts` 无错误。
  - `npm run build` 成功。
  - 剩余 warning 为既有 Vite/Rollup chunk 与 SignalR annotation warning。

## 上一次支持

- 时间：2026-06-03 12:03 UTC+8
- 事项：修复 VarFlow 长变量名重叠与节点间 gap 自适应。
- 用户反馈：
  - 超长变量名情况下变量控件内部发生重叠。
  - 此类变量放在节点侧边/节点间时，节点间空隙应随变量框宽度自适应变大。
  - 需要检查 `DEV_LOG.md`，之前的修改可能没有成功写入。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - `FLOW_ITEM_MAX_WIDTH` 从 `260` 放宽到 `420`。
    - 变量名宽度估算从 `name.length * 6.8` 放宽到 `name.length * 7.2`，上限从 `150` 放宽到 `300`。
    - 由于节点间 gap 通过 `maxVariableWidth()` 计算，长变量名会自动撑大节点间空隙。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - `.var-name/.var-value` 增加 `display: block`、`width: 100%`、`min-width: 0`、`max-width: 100%`。
    - 修复 CSS grid 中长文本按内容撑开并压到类型列导致的重叠。
  - 检查 `DEV_LOG.md`：
    - 此前 `varflow-sim-stats` 和 `varflow-instance-routing` 记录均已存在。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts`、`GraphCanvas.vue` 无错误。
  - `npm run build` 成功。
  - 剩余 warning 为既有 Vite/Rollup chunk 与 SignalR annotation warning。

## 上一次支持

- 时间：2026-06-03 11:41 UTC+8
- 事项：修正 VarFlow 侧边变量直线连接的 Y 坐标。
- 用户反馈：
  - 侧边变量未超过节点高度时，不应从节点侧边中点连接。
  - 需要画横线，并且连线 Y 坐标应与变量框一致。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - `anchorSide()` 在侧边变量堆叠高度未超过节点/Root 高度时，使用变量框中心 Y。
    - 超过高度时仍按节点/Root 侧边等距 slot 连接。
    - 因此 Root/节点侧边变量的短连接会成为与变量同 Y 的水平线。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts` 无错误。
  - `npm run build` 成功。
  - 剩余 warning 为既有 Vite/Rollup chunk 与 SignalR annotation warning。

## 上一次支持

- 时间：2026-06-03 11:29-11:40 UTC+8
- 事项：补齐 VarFlow Root 只发布变量、节点实例级关系判定、侧边连线规则。
- 用户反馈：
  - Root 也要检查是否有“只发布变量”，有则放到 Root 右侧。
  - 同一个 Logic/Class 可能被多个节点使用，VarFlow 关系不能只靠 Class 判断，必须按节点实例判断。
  - 放在节点左/右侧的变量，连线应从节点左右侧走。
  - 侧边变量总高度不超过节点高度时用直线；超过时按节点侧边等距 slot，用弧线连接。
- 已执行：
  - `3rd/CoralinkerSDK/DIVERSession.cs`：
    - VarFlow 的 `SourceIds/ReaderIds/WriterIds` 从 LogicName/ClassName 改为实例 ID。
    - 普通节点使用 `entry.UUID`。
    - Root 虚拟节点使用 `VirtualNodeEntry.SourceId`，即当前 Root runtime 的固定 source id。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - 普通节点布局 sourceId 改为 VueFlow node id / uuid。
    - Root sourceId 改为固定 `root-runtime`，不再用 `rootLogicName`。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - 新增 Root 右侧 gap，用于 Root 只发布且无消费者的变量。
    - gap item 增加 `placement/gapIndex/sideStackHeight/sideSlotIndex/sideSlotCount` 元数据。
    - `root-side` / `node-side` 变量使用左右侧锚点。
    - 侧边变量堆叠总高不超过目标节点/Root 高度时使用直线。
    - 超过高度时在侧边按变量序号等距分配 slot，并使用横向贝塞尔弧线。
- 验证：
  - `ReadLints` 检查相关文件无错误。
  - `npm run build` 成功。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj` 成功。
  - 剩余 warning 均为既有 Vite chunk/annotation warning 与既有 C# nullable / Windows-only API / unused event warning。
- 注意：
  - 仍需在 UI 中用“两个节点使用同一个 Logic”的场景手动确认线条是否按节点实例分开。

## 上一次支持

- 时间：2026-06-03 11:20-11:55 UTC+8
- 事项：完成 VarFlow 相邻节点 gap 布局规则，并修复模拟节点端口 TX/RX 统计显示链路。
- 用户反馈：
  - VarFlow 需要处理“有消费者但无生产者”的变量：放到消费节点左侧，并加宽左侧空隙。
  - 如果左节点有只发布变量、右节点有只接收变量，应合并放到同一个节点间空隙，不要撑两个空隙。
  - 如果变量只用于相邻节点通信，且 Root 不参与，无论箭头方向，都放到两个相邻节点中间，不再放 Root 下方横排。
  - 之前 VarFlow 和 TX/RX 统计都未完全处理，需要继续完成。
- 已执行：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - 节点宽度保持为 `380`，约比原 `420` 缩窄 10%。
    - 将此前未完成的 `sideVariableGroups` 改为 `gapVariableGroups`：
      - gap `-1` 表示第一个节点左侧。
      - gap `i` 表示第 `i` 个节点和第 `i+1` 个节点之间。
      - gap `last` 表示最后一个节点右侧。
    - 变量分类规则：
      - 只发布、无消费者：放到发布节点右侧 gap。
      - 只消费、无生产者：放到消费节点左侧 gap。
      - 左右相邻节点互通且 Root 不读写：放到这两个节点中间 gap。
      - Root 参与读/写的变量仍保留 Root 下方横排。
    - 同一个 gap 内的变量统一纵向排列，只加宽一次节点间距。
  - `3rd/CoralinkerSDK/DIVERSession.cs`：
    - `BuildNodeStateSnapshot()` 生成快照前会对运行中的已连接节点调用 `RefreshStats()`，再同步 `entry.Stats`，避免 SignalR 推送拿到旧统计。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/PortStatsView.vue`：
    - 端口统计从只显示 `TX frames / RX frames` 改为显示 `TX frames/bytes` 和 `RX frames/bytes`。
- 验证：
  - `ReadLints` 检查相关文件无错误。
  - `npm run build` 成功。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj` 成功。
  - 构建仍有既有 warning：
    - Vite/Rollup chunk size 与 SignalR pure annotation warning。
    - C# 既有 nullable / Windows-only API / unused event warning。
- 注意：
  - 仍需用户在 UI 中实测确认 VarFlow 视觉位置与 TX/RX 数值递增是否符合实际逻辑。

## 上一次支持

- 时间：2026-06-03 10:34-10:54 UTC+8
- 事项：修复模拟节点运行节拍、IO/WireTap 统计、native 结构归属，并重新输出 publish 包。
- 用户反馈：
  - 模拟节点执行过快，应严格按 Logic `scanInterval` 执行，例如 100ms 就 100ms 一次，其余时间睡眠。
  - 串口收发、写 IO 网页上看不到状态和日志，需要追查链路。
  - 不希望 `mcu_runtime.c` 放大量模拟节点 `sim_*` 状态和函数；模拟相关代码应移到 SimNode 工程，`MCURuntime` 只作为 VM 库和保留原调试路径。
  - `MCURuntime/build-native.ps1` 不应放在 `MCURuntime` 下，因为产物是 sim node runtime；应由 `CoralinkerSimNodeHost` 编译。
- 已执行：
  - `CoralinkerSimNodeHost/Program.cs`：
    - 使用 `sim_load_program` 返回的 interval 作为 `_scanIntervalMs`。
    - run loop 使用 `Stopwatch` 和 `Task.Delay` 按 interval 调度，不再固定 10ms 高频执行。
    - `sim_step` 改为传入当前模拟时间戳。
    - 新增 `snapshot` 事件处理链路。
  - `SimulatedMcuNode.cs`：
    - 处理 `snapshot` 事件，更新 `RuntimeStats.DigitalOutputs` 和 `DigitalInputs`，实现虚拟 DO→DI 前端显示。
    - Wire 事件更新端口 `TxFrames/RxFrames/TxBytes/RxBytes`。
    - WireTap 事件按当前端口 TX/RX 标志过滤后再发给 Host 日志聚合。
    - `SetWireTap(0xFF, flags)` 会应用到 0..15 端口。
  - native 结构调整：
    - 删除放错位置的 `MCURuntime/build-native.ps1`。
    - 新增 `3rd/CoralinkerSimNodeHost/build-native.ps1`，输出：
      - `3rd/CoralinkerSimNodeHost/build/runtimes/win-x64/native/sim_node_runtime.dll`
      - `3rd/CoralinkerSimNodeHost/build/runtimes/linux-x64/native/libsim_node_runtime.so`
      - `3rd/CoralinkerSimNodeHost/build/runtimes/linux-arm64/native/libsim_node_runtime.so`
    - 新增 `3rd/CoralinkerSimNodeHost/native/sim_node_runtime.c`，把 `sim_*`、模拟状态、snapshot/stream/event callbacks 从 `mcu_runtime.c` 移出。
    - `McuRuntimeNative.cs` / `NativeMcuRuntimeResolver.cs` 改为加载 `sim_node_runtime`。
    - `CoralinkerHost.csproj` 改为把 native 资产放进 `simnode/runtimes/...`。
    - `publish-host.ps1` 改为构建 `3rd/CoralinkerSimNodeHost/build-native.ps1`，不再构建 `MCURuntime/build-native.ps1`。
  - `MCURuntime/mcu_runtime.c`：
    - 删除模拟节点 `sim_*` 和模拟状态。
    - 保留 `_DEBUG && !IS_MCU && !SIM_NODE_HOST` 的 legacy `test/put_upper/set_lowerio_cb/set_error_report_cb` debug harness。
    - 保留此前为跨平台编译做的少量 C 兼容修正。
- 验证：
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Debug` 成功。
  - `3rd\CoralinkerSimNodeHost\build-native.ps1 -Target windows -Configuration Debug` 成功。
  - `3rd\CoralinkerSimNodeHost\build-native.ps1 -Target linux-x64/linux-arm64 -Configuration Release` 成功。
  - `publish-host.ps1 -Configuration Release` 成功。
  - 新发布包：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_4eeb413_20260603-105422`
  - 已确认包内关键文件：
    - `simnode/CoralinkerSimNodeHost.dll`
    - `simnode/runtimes/win-x64/native/sim_node_runtime.dll`
    - `simnode/runtimes/linux-x64/native/libsim_node_runtime.so`
    - `simnode/runtimes/linux-arm64/native/libsim_node_runtime.so`
    - `package-manifest.sha256`
    - 根目录旧 `runtimes/win-x64/native/mcu_runtime.dll` 不存在。
  - `git diff --check` 无 whitespace 错误，仅有 Windows 换行提示。
- 注意：
  - 仍需用户在 UI 中实际确认：scanInterval 是否符合预期、IO 灯和端口 TX/RX 统计是否随逻辑行为更新。

## 上一次支持

- 时间：2026-06-03 10:22 UTC+8
- 事项：调整模拟节点前端显示。
- 用户反馈：
  - Add Simulated Node 时 URI Preview 不需要显示。
  - 已添加的虚拟节点希望前端用不同颜色区分，橙色底色。
  - 判断方式应为前端直接看 URL 是否以 `sim://` 开头，不需要额外 sim 字段。
  - 询问多个 sim 节点 URL 是否一样、通讯是否会错位。
- 已执行：
  - `AddNodeDialog.vue`：
    - Simulated 模式隐藏 URI Preview。
  - `CoralNodeView.vue`：
    - 新增 `isSimulatedNode` computed，依据 `mcuUri.toLowerCase().startsWith('sim://')` 判断。
    - 模拟节点加 `simulated` class，使用橙色渐变底色和橙色边框。
    - 模拟节点隐藏固件升级按钮。
    - URI 显示从 `Not Set` 改为 `Virtual`，并使用橙色文字。
- 设计确认：
  - 后端 `AddSimulatedNode()` 每次生成唯一 `sim://{uuid}`，多个 sim 节点 URL 不一样。
  - SDK 每个 `SimulatedMcuNode` 会按节点启动独立 `CoralinkerSimNodeHost` 子进程，父进程按节点 handle 管理 IPC，因此不会因为 URI 共用导致通讯错位。
- 验证：
  - `ReadLints` 检查 `AddNodeDialog.vue`、`CoralNodeView.vue` 无错误。
  - `npm run build` 成功。
  - `git diff --check` 无 whitespace 错误，仅有 Windows 换行提示。

## 上一次支持

- 时间：2026-06-03 10:16 UTC+8
- 事项：按用户要求直接生成 `CoralinkerHost` publish 包。
- 已执行：
  - 运行 `3rd/CoralinkerHost/publish-host.ps1 -Configuration Release`。
  - 发布脚本完成：
    - `MCUSerialBridge` 三平台 native runtime assets 构建。
    - `MCURuntime` 三平台 native runtime assets 构建。
    - 前端 `npm run build`。
    - `dotnet publish`。
    - 离线 NuGet 包收集。
    - `package-manifest.sha256` 生成。
- 输出目录：
  - `3rd/CoralinkerHost/Publish/CoralinkerHost_4eeb413_20260603-101633`
- 已确认关键文件存在：
  - `simnode/CoralinkerSimNodeHost.dll`
  - `runtimes/win-x64/native/mcu_runtime.dll`
  - `runtimes/linux-x64/native/libmcu_runtime.so`
  - `runtimes/linux-arm64/native/libmcu_runtime.so`
  - `package-manifest.sha256`
  - `publish-info.json`
- 注意：
  - 发布过程有既有 C# nullable / Windows-only API warning，以及 Vite chunk size warning；发布命令退出码为 0。
  - 曾有一次文件确认命令的 PowerShell 变量被外层 shell 展开导致命令错误，已手动结束该错误进程，后续用字面量路径确认成功。

## 上一次支持

- 时间：2026-06-03 10:03 UTC+8
- 事项：继续完成模拟节点快速方案，保留 `DiverTest` / `MCURuntime.test()` 原调试路径。
- 已实现：
  - SDK：
    - 新增 `IRuntimeNode`，让真实 `MCUNode` 和模拟 `SimulatedMcuNode` 走同一运行时生命周期。
    - `MCUNode` 显式实现接口，保留原有 public API 面。
    - `DIVERSession` 根据 `sim://` 创建 `SimulatedMcuNode`，并新增 `AddSimulatedNode()`。
    - 模拟节点 layout：32 DI、32 DO、2 个 RS485、1 个 RS232、1 个 CAN。
  - SimNodeHost：
    - 新增 `3rd/CoralinkerSimNodeHost` 子进程项目。
    - SDK `SimulatedMcuNode` 通过 stdin/stdout NDJSON 管理子进程。
    - 子进程内 P/Invoke `mcu_runtime`，处理 `hello/configure/program/start/stop/upper/wiretap/shutdown`。
    - 端口回环：RS485-A/B 互连，RS232 自环，CAN 自环。
  - MCURuntime：
    - 新增 `sim_set_callbacks`、`sim_load_program`、`sim_put_upper`、`sim_put_port_input`、`sim_step`、`sim_destroy`。
    - PC host 函数改为非 MCU 构建可用，不再只限 `_DEBUG`，但保留 legacy `test/put_upper/set_lowerio_cb/set_error_report_cb`。
    - `write_snapshot` 现在更新虚拟 snapshot，并在下一次 `sim_step` 前喂回 VM，用于 32 路 DO→DI 回环。
    - 修正少量 clang/zig 不兼容 C 写法：release 分支误留 `VAL_OUT(ptr)`、C++ 风格 `auto`、非标准 `itoa`、`-1` 指针初始化。
  - Host/API/UI：
    - 新增 `/api/node/add-simulated`。
    - `AddNodeDialog.vue` 增加 `Simulated` 模式，不显示固件 Upgrade。
    - API 类型 `AddNodeResult` 增加 `mcuUri`，前端使用后端生成的唯一 `sim://uuid`。
  - 构建/发布：
    - 新增 `MCURuntime/build-native.ps1`，输出 `runtimes/win-x64/native/mcu_runtime.dll`、`runtimes/linux-x64/native/libmcu_runtime.so`、`runtimes/linux-arm64/native/libmcu_runtime.so`。
    - `CoralinkerHost.csproj` 构建/发布时收集 `mcu_runtime` native assets，并构建/拷贝 `simnode` 子进程。
    - `publish-host.ps1` 同时构建 `MCUSerialBridge` 与 `MCURuntime` native assets。
    - `DIVER.sln` 加入 `CoralinkerSimNodeHost`。
- 验证：
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Debug` 成功。
  - `npm run build` 成功。
  - `MCURuntime/build-native.ps1 -Target windows -Configuration Debug` 成功。
  - `MCURuntime/build-native.ps1 -Target linux-x64/linux-arm64 -Configuration Release` 成功；仍有大量既有/类型指针 warning，但无 error。
  - 确认 Host Debug 输出包含 `simnode/CoralinkerSimNodeHost.dll` 和 `runtimes/win-x64/native/mcu_runtime.dll`。
  - `ReadLints` 无新增错误。
  - `git diff --check` 无 whitespace 错误，只有 Windows 换行提示。
- 注意：
  - 尚未做真实 UI 手动联调：添加模拟节点、编程、Start 后观察 LowerIO/console/wiretap 行为。
  - Linux native 编译已通过，但运行时行为仍需在目标 Linux 机器上实际启动验证。

## 上一次支持

- 时间：2026-06-03 10:00 UTC+8
- 事项：暂停模拟节点实现，补充 `MCURuntime` 原始 VM 调试链路说明，避免破坏原作者/同事的本地调试体验。
- 用户问题：
  - `MCURuntime` 的虚拟机原本如何实现？
  - 同事原来如何调试这个 runtime？
  - 模拟节点改造不能破坏原有调试体验，至少需要文档说明后续开发/调试方式。
- 调查结论：
  - `MCURuntime/mcu_runtime.c` 是 VM 内核，不是完整设备模拟器。
  - 原始调试入口是 `DiverTest`：
    - `DiverTest/DiverTest.csproj` PreBuild 重建 `DiverCompiler`、执行 `DiverCompiler.exe -g`、再跑 `DiverTest/build_cpp.bat`。
    - `build_cpp.bat` 使用 Visual Studio `vcvars64.bat` 和 MSVC `cl /LD /MDd /Zi /DEBUG` 编译 `MCURuntime.dll`，支持 native 断点/PDB 调试。
    - `DiverTest/DIVER/DIVERInterface.cs` 通过 P/Invoke 调 `set_lowerio_cb`、`set_error_report_cb`、`put_upper`、`test`。
    - `test()` 是 legacy debug harness：加载 VM program，循环执行若干次 `vm_run(i)`，注入 snapshot/event，并通过 lower IO callback 返回结果。
- 已执行：
  - 新增 `MCURuntime/DEBUGGING.md`：
    - 说明 VM 核心 API：`vm_set_program`、`vm_run`、`vm_put_upper_memory`、`vm_get_lower_memory`、`vm_put_snapshot_buffer`、`vm_put_stream_buffer`、`vm_put_event_buffer`。
    - 说明 `DiverTest` / `build_cpp.bat` / PDB 的原始调试流程。
    - 明确模拟节点新增 `sim_*` 导出不能替代 `test()`。
    - 明确必须保留 `test`、`put_upper`、`set_lowerio_cb`、`set_error_report_cb`，并继续保留 VS / `DiverTest` 调试路径。
  - 更新 `MCURuntime/MCURuntime.vcxproj`，把 `DEBUGGING.md` 加入 VS 项目文件列表。
- 当前状态：
  - 模拟节点实现还未完成；已暂停在 SDK/SimNodeHost/native API 骨架阶段。
  - 继续实现前需要确保 `MCURuntime` 的 legacy debug exports 与 `DiverTest` 调试路径保持兼容。

## 上一次支持

- 时间：2026-06-03 09:13 UTC+8
- 事项：修订模拟节点快速方案：支持多个模拟节点，每节点独立进程隔离，并随 Host 发布。
- 用户确认：
  - 快速方案要求支持多个模拟节点。
  - 每个模拟节点使用不同进程隔离，规避 `MCURuntime` 全局状态。
  - 需要随 Host 发布，补动态库构建。
  - Wrapper 由实现方案自行处理。
- 调查补充：
  - `MCURuntime` 现有导出只适合 Debug harness：
    - `test()` 固定执行测试循环。
    - `put_upper()` 只写 UpperIO。
    - `set_lowerio_cb()` / `set_error_report_cb()` 只有 LowerIO/Error，缺少 step、snapshot、stream/event/console callback 的通用导出。
  - `DiverTest/DIVER/DIVERInterface.cs` 当前仅作为旧 Debug 调用示例，不适合直接放进 Host。
  - `MCUSerialBridge/build-native.ps1` 已有 Windows + Zig cross compile 的三平台 native 构建模式，可仿照它新增 `MCURuntime/build-native.ps1` 或扩展统一 native build。
  - `CoralinkerHost.csproj` / `CoralinkerSDK.csproj` 当前只把 `mcu_serial_bridge` native assets 放进 `runtimes/*/native`，需要加入 `mcu_runtime` native assets。
- 最新实现倾向：
  - 新增 `Coralinker.SimNodeHost` 子进程程序，作为每个模拟节点的 worker。
  - 子进程内部 P/Invoke `mcu_runtime` 动态库并独占一个 VM 全局状态；多模拟节点通过多进程自然隔离。
  - Host/SDK 不直接 P/Invoke `mcu_runtime`，而是通过 `SimulatedMcuNode` wrapper 管理子进程和 IPC。
  - IPC 建议用 stdin/stdout NDJSON 或 named pipe，消息包括 `configure`、`program`、`start`、`stop`、`upperIO`、`setWireTap`、`state`、`stats`，事件包括 `lowerIO`、`console`、`fatal`、`wiretapSerial`、`wiretapCan`。
  - native runtime 需要新增通用导出：`sim_create/load_program/step/put_upper/put_snapshot/put_stream/put_event/get_lower/register_callbacks/destroy`，或在子进程内封装现有 VM API，至少不能依赖 `test()`。

## 上一次支持

- 时间：2026-06-02 21:47 UTC+8
- 事项：调查“模拟节点 / 虚拟 MCU 节点”大功能的实现方案。
- 用户需求：
  - Host 目前只能添加真实节点，没有节点时无法调试/演示。
  - 需要支持添加虚拟节点：
    - 32 个 Input 和 32 个 Output，I/O 对应连接。
    - 2 个虚拟 RS485 接口，互相连接。
    - 1 个虚拟 RS232 接口，TX/RX 自环。
    - 1 个虚拟 CAN 接口，发出的消息回收。
    - 节点代码 VM 使用 `MCURuntime` 执行。
- 调查结论：
  - 当前真实节点链路在 `DIVERSession` 中硬编码 `new MCUNode(entry.UUID, entry.McuUri)`，`MCUNode` 封装 `MCUSerialBridge` 真实硬件通信。
  - `MCUNode` 公共面集中：`Connect/Configure/Program/Start/Stop/SendUpperIO/SetWireTap/RegisterSerialPortCallback/RegisterCANPortCallback/RefreshState/RefreshStats` 和 LowerIO/Console/Fatal/Error 回调。
  - `MCURuntime` 已有 Host-side C API：`vm_set_program`、`vm_run`、`vm_put_upper_memory`、`vm_get_lower_memory`、`vm_put_snapshot_buffer`、`vm_put_stream_buffer`、`vm_put_event_buffer`，但当前 Host/SDK 没有 C# wrapper 使用它。
  - 现有 `LayoutInfo` 可以表达 32 DI / 32 DO 和最多 16 个端口，模拟节点无需改硬件布局协议。
  - 前端 Add Node 当前只有 COM/VIDPID 探测真实节点，模拟节点应新增专用 UI 分支/API，而不是伪装为串口。
- 实现倾向：
  - 不建议伪造虚拟串口或在 `MCUSerialBridge` 协议层模拟；那会把问题带到 native transport 层，调试成本高。
  - 建议在 SDK 层引入节点运行时抽象，例如 `IRuntimeNode` / `INodeRuntimeHandle`，让真实 `MCUNode` 和新 `SimulatedMcuNode` 实现同一接口。
  - `DIVERSession.StartNode()` 按节点类型创建真实或模拟 handle，其余上层 Start/Stop/变量/日志/WireTap 流程尽量复用。
  - `SimulatedMcuNode` 内部通过新的 `MCURuntimeNative` wrapper 调 `MCURuntime` 动态库，定时执行 VM loop。
- 风险：
  - `MCURuntime` 目前 Visual Studio 工程仅 x64 Debug 是 DynamicLibrary，跨平台/发布包需要新增便携 native build 与打包。
  - `mcu_runtime.c` 的 PC 版需要实现/导出 host callbacks（`write_snapshot/write_stream/write_event/report_error/print_line` 等），并确认多节点实例隔离；当前 C 全局状态看起来不是天然多实例。
  - 多模拟节点若共用一个进程内 native 全局 VM，可能互相污染；更稳妥需要每节点独立 runtime context，或先限制单模拟节点/进程隔离。

## 上一次支持

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
