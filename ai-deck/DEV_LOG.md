# DEV_LOG — CORAL-NODE-V2.1 BSP 适配

> 仅追加，不修改。每次重要动作记录一条。

---

## 2026-05-13

### [setup] 工作目录与上下文创建
- 建立 `ai-deck/` 用作 AI 工作沉淀
- 写入 `CONTEXT_SUMMARY.md`：列出任务背景、V2.1 vs V2.0 差异、用户决策与 DMA 计划
- 写入本 `DEV_LOG.md`

### [survey] 代码勘察结论
- BSP 选板机制：`scons PDN=<dir>`，由 `MCUSerialBridge/mcu/SConscript` 处理（行 8–46）
- `bsp_config.py` 必须暴露 `CHIP_NAME`，可选 `CPP_DEFINES`
- 已有 BSP：`CORAL-NODE-V2.0`、`FRLD-DIVERBK-V2`
- HAL 配置结构在 `mcu/unilib/midware/include/hal/*.h`：
  - USART：含 tx/rx GPIO + DE GPIO + tx/rx DMA
  - DirectCAN：tx/rx GPIO（无 DMA，使用中断队列）
  - SPI：tx/rx DMA 可选，未填则 sync 模式
  - TIM + TIM OC：OC 配置内嵌 DMA（用于灯带 TIM1_CH1）
- `init_bsp()` 仅调 `bsp_init_digital_io()`；端口与 uplink 配置在 `init_threads()` / `init_upload()` 内被读取

### [clarify] 与用户对齐 V2.1 硬件冲突
- 用户原描述中 PB0 同时挂 24V 检测和 F1 急停，经询问更正为：
  - PB0 = 24V 检测
  - PB1 = F2 触边
  - PB2 = F1 急停
- TIM1_CH1 灯带 → PA8（STM32F405RG LQFP64 唯一映射）
- LED 与蜂鸣器解耦：LED 改纯 GPIO 低电平点亮；蜂鸣器独占 TIM2_CH1=PA15
- DMA 表格落入 sch.md 后由用户审阅确认

### [todo] 待执行
1. 建 `mcu/bsp/CORAL-NODE-V2.1/`，骨架文件
2. 撰写 `mcu/bsp/include/bsp/pin.md` 替代或新增 `mcu/bsp/CORAL-NODE-V2.1/sch.md`
3. 等用户确认 DMA
4. 写 5 个源文件
5. 加版本助记词 `lighthouse`

### [correction] 撤销误加助记词
- 用户明确指出不需要助记词，也不需要 `CORAL_NODE_V2_1` / `BSP_VERSION_MNEMONIC` 等额外宏。
- 已将 `mcu/bsp/CORAL-NODE-V2.1/bsp_config.py` 恢复为与 V2.0 相同风格，仅保留 `USE_CCM_RAM`、`HSE_VALUE`、`FORCE_PLL_M`。
- 已从 `sch.md` 删除版本助记词章节。

### [confirm] 第二轮硬件确认
- F1 急停：PB2，下拉输入，HIGH=急停未按下、允许运行，bitmap bit 25。
- F2 触边：PB1，下拉输入，HIGH=触边被按下、禁止运行，bitmap bit 26。
- 24V 检测：PB0，下拉输入，HIGH=24V 存在，bitmap bit 24；若 24V 不存在，上层软件应判断输出无效。
- 输入移位寄存器：3 级级联，共 24 路，bit 0..23 按顺序进入 bitmap。
- 输出移位寄存器：当前 20 路，后续用户会重整。
- 输入为 NPN，需要软件整体取反。
- 485 默认 115200，CAN 默认 500 kbps，Uplink 默认 1 Mbps。

### [implement] V2.1 BSP 源码
- 新建 `mcu/bsp/CORAL-NODE-V2.1/bsp.c`，入口与 V2.0 一致，调用 `bsp_init_digital_io()`。
- 新建 `bsp_config.py`，芯片为 `STM32F405RG`，时钟宏同 V2.0。
- 新建 `uplink.c`，USART2 PA2/PA3，DMA1 S6/S5 C4。
- 新建 `ports.c`，3 路 RS485 + 2 路 CAN：
  - RS485-1 USART6 PC6/PC7/PC8，DMA2 S6/S2 C5
  - RS485-2 USART1 PA9/PA10/PA11，DMA2 S7/S5 C4
  - RS485-3 UART4 PC10/PC11/PC12，DMA1 S4/S2 C4
  - CAN1 PB9/PB8，CAN2 PB6/PB5
- 新建/修正 `digital_io.c`：
  - SPI2 PB13/PB14/PB15，控制脚 PB10/PB11/PB12
  - SPI 同步扫描 3 字节
  - 24 个移位输入 + 3 个固定输入 = 27 个输入
  - 输出数量 20

### [verify] 构建与检查
- 首次执行 `scons BUILD_MCU=1 PDN=CORAL-NODE-V2.1 -j 12 debug=1` 时，BSP 文件已编译通过，但 `ENABLE_DIVER_RUNTIME=0` 导致 `vm_glue.c` 中 `uchar` 类型不可用，构建失败；这是构建参数问题，不是 V2.1 BSP 源码错误。
- 按项目 `dev.bat` 风格执行 `scons BUILD_MCU=1 PDN=CORAL-NODE-V2.1 ENABLE_DIVER_RUNTIME=1 -j 12 debug=1` 成功。
- 生成产物：
  - `MCUSerialBridge/mcu/build/firmware.elf`
  - `MCUSerialBridge/mcu/build/firmware.hex`
  - `MCUSerialBridge/mcu/build/firmware.bin`
- 固件尺寸：text=220196, data=2204, bss=114188。
- `ReadLints` 检查 `MCUSerialBridge/mcu/bsp/CORAL-NODE-V2.1` 无 linter 错误。

### [package] 生成 V2.1 UPG
- 执行 `scons BUILD_MCU=1 PDN=CORAL-NODE-V2.1 ENABLE_DIVER_RUNTIME=1 -j 12 debug=1 upg` 成功。
- UPG 输出：`MCUSerialBridge/build/MCUSerialBridge_CORAL-NODE-V2.1_f1d8f16__20260513_171759.upg`
- 元数据：
  - PDN: `CORAL-NODE-V2.1`
  - COMMIT: `f1d8f16`
  - BUILD_TIME: `2026-05-13 17:17:59`
  - LENGTH: `222416`
  - ADDRESS: `0x00010000`
  - CRC32: `0xA36AA64B`
- 生成后清理了 `mcu/bsp/CORAL-NODE-V2.1/` 下的 `.o` 与 `__pycache__` 中间产物。

### [support] 处理 4499 端口占用
- 用户反馈 ASP.NET Core Host 启动失败：`Failed to bind to address http://0.0.0.0:4499: address already in use`。
- 给出 Windows PowerShell 处理方式：先用 `Get-NetTCPConnection -LocalPort 4499` 定位 PID，再用 `Stop-Process` 或 `taskkill` 结束占用进程。
- 用户反馈查询结果为 `192.168.0.24 4499 TimeWait 0`；说明该条目没有用户态进程可杀，后续建议过滤 `Listen` 状态或等待系统释放 TIME_WAIT。

### [debug] CoralinkerHost Release 崩溃排查
- 用户反馈 `CoralinkerHost` 在 VS Debug 模式运行正常，但切换到 Release 后运行一段时间或某些操作后挂掉。
- 排查路径：
  - 查看 `3rd/CoralinkerHost/CoralinkerHost.csproj`、`3rd/CoralinkerSDK/CoralinkerSDK.csproj`，确认 Host 通过 SDK 引用 `MCUSerialBridge/wrapper/MCUSerialBridgeCLR.cs` 并复制 `MCUSerialBridge/build/mcu_serial_bridge.dll`。
  - 查看 `DIVERSession`、`MCUNode`、`MCUSerialBridgeCLR` 的后台线程、native 回调、Dispose 路径。
  - 对照 `MCUSerialBridge/c_core/include/msb_bridge.h` 和 `MCUSerialBridge/c_core/src/msb_bridge.c` 的 native 线程关闭逻辑。
- 确认根因：
  - `msb_close()` 中 `if (handle->parse_thread)` 分支错误执行 `WaitForSingleObject(handle->send_thread, INFINITE)`，随后 `CloseHandle(handle->parse_thread)`。
  - 结果是 parse 线程可能还在运行，但线程句柄已关闭，随后 `msb_handle_deinit(handle)` 释放 handle，造成 parse 线程访问已释放内存。
  - 该 use-after-free 在 Debug 下可能因时序慢不易暴露，Release 下更容易在 Stop、断开、重连或 fatal error 清理路径触发。
- 修复：
  - 将等待对象改为 `handle->parse_thread`。
  - 关闭 `recv_thread`、`parse_thread`、`send_thread` 句柄后分别置空，避免后续误用。
- 验证：
  - 执行 `scons -j 12 build/mcu_serial_bridge.dll` 成功，重新生成 `MCUSerialBridge/build/mcu_serial_bridge.dll`。
  - 首次执行 `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release --no-restore` 失败，因为旧 Release Host 进程 PID 55332 锁定了输出目录中的 DLL。
  - 执行 `Stop-Process -Id 55332 -Force` 后重新构建 Release Host 成功。
  - `ReadLints` 对 `msb_bridge.c` 报告 `windows.h` 等 clangd 环境诊断，属于 Windows SDK/clangd include 配置问题；MSVC/SCons 实际构建通过。

### [fix] Stop 偶发卡在 Stopping
- 用户反馈网页点击 Stop 后偶发显示 `Stopping` 后卡住，要求先解释原因和方案，确认后再修改。
- Stop 调用链：
  - 前端 `/api/stop`
  - `RuntimeSessionService.StopAsync()` 先推送 `[session] ========== Stopping ==========`。
  - 同步执行 `DIVERSession.Stop()`。
  - Stop 内部先 `StopBackgroundWorkers()`，再对每个节点执行 `entry.Handle?.Stop()`、`Disconnect()`、`Dispose()`。
  - 只有 `_session.Stop()` 返回后才推送 `[session] ========== Stopped ==========`。
- 判断：
  - 若前端停在 `Stopping`，说明后端 Stop 请求没有返回，卡在 `_session.Stop()` 的同步 native 调用链中。
  - `MCUNode.Stop()` 会通过 `bridge.Reset()` 调用 `msb_reset()`，最终进入 `mcu_send_packet_and_wait()` 等 MCU 响应。
  - `Disconnect/Dispose` 会进入 `msb_close()`，等待 native `recv/parse/send` 线程退出。
- 修复 1：`MCUSerialBridge/c_core/src/msb_packet.c`
  - 原逻辑先 `SleepConditionVariableCS(..., timeout_ms)`，如果被唤醒但 `done_flag` 仍未置位，会进入 `SleepConditionVariableCS(..., INFINITE)`。
  - Windows 条件变量允许虚假唤醒，且异常路径也可能唤醒后未完成；这会让 Reset 等待无限卡住。
  - 改为用 `GetTickCount64()` 计算总截止时间，循环中每次只等待剩余时间；超时后释放 waiter 并返回 `MSB_Error_Proto_Timeout`。
- 修复 2：`MCUSerialBridge/c_core/src/msb_bridge.c`
  - 新增 `MSB_CLOSE_THREAD_TIMEOUT_MS = 2000` 和 `msb_wait_and_close_thread()`。
  - `msb_close()` 设置 `is_open=false` 后，先对串口句柄执行 `CancelIoEx(hComm, NULL)` 和 `PurgeComm(...PURGE_RXABORT/TXABORT/RXCLEAR/TXCLEAR)`，让阻塞中的 I/O 尽快醒来。
  - 对 `recv_thread`、`parse_thread`、`send_thread` 分别限时等待 2 秒并关闭句柄；任何线程未退出则返回 `MSB_Error_Win_ResourceBusy`，不再永久等待。
  - 保留前一次修复：`parse_thread` 分支等待自身，而不是错误等待 `send_thread`。
- 验证：
  - `scons -j 12 build/mcu_serial_bridge.dll` 成功。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release --no-restore` 成功。
  - `ReadLints` 仍报告 clangd 找不到 `windows.h` 等 Windows SDK 诊断，和本次修改无关；MSVC/SCons 构建通过。

### [analysis] mcu_serial_bridge.dll 复制策略风险
- 用户指出当前每次依赖拷贝 `mcu_serial_bridge.dll`，如果 MSB DLL 更新但 `DIVER` / `CoralinkerHost` 没有托管代码变化，项目可能被判定 up-to-date，从而不触发拷贝。
- 检查结果：
  - `3rd/CoralinkerHost/CoralinkerHost.csproj` 使用 `Target Name="CopyMcuSerialBridgeDll" AfterTargets="Build"` 手工复制 `MCUSerialBridge/build/mcu_serial_bridge.dll` 到 `$(OutDir)`。
  - `3rd/CoralinkerSDK/CoralinkerSDK.csproj` 使用 `Target Name="CopyNativeDll" AfterTargets="Build"` 手工复制同一 DLL 到 `$(OutputPath)`。
  - `MCUSerialBridge/wrapper/MCUSerialBridgeWrapper.csproj`、`TestDIVER.csproj`、`TestCS.csproj`、`TestBL.csproj` 没有把 `mcu_serial_bridge.dll` 声明为 MSBuild 内容项。
- 结论：
  - 用户担心成立。`AfterTargets="Build"` 只有在 Build target 真正运行时才执行；如果 VS Fast Up-To-Date Check 直接跳过项目，或增量构建没有进入该项目，DLL 更新不会必然触发复制。
  - 更稳妥的做法是将 native DLL 声明为 `None`/`Content` item，并设置 `CopyToOutputDirectory=PreserveNewest` / `CopyToPublishDirectory=PreserveNewest`，让 MSBuild/VS 把它纳入输入输出跟踪。
  - 对 VS up-to-date 行为更敏感的项目，可额外声明 `UpToDateCheckInput Include="..\..\MCUSerialBridge\build\mcu_serial_bridge.dll"`。

### [fix] mcu_serial_bridge.dll MSBuild 依赖跟踪
- 用户确认按建议修改。
- 修改 `3rd/CoralinkerHost/CoralinkerHost.csproj`：
  - 移除 `CopyMcuSerialBridgeDll` 的 `AfterTargets="Build"` 手工 Copy target。
  - 添加 `None Include="$(MSBuildThisFileDirectory)..\..\MCUSerialBridge\build\mcu_serial_bridge.dll"`，设置 `Link="mcu_serial_bridge.dll"`、`CopyToOutputDirectory="PreserveNewest"`、`CopyToPublishDirectory="PreserveNewest"`。
  - 添加同一路径的 `UpToDateCheckInput`。
- 修改 `3rd/CoralinkerSDK/CoralinkerSDK.csproj`：
  - 移除 `CopyNativeDll` 的 `AfterTargets="Build"` 手工 Copy target。
  - 添加同样的 `None` 内容项复制配置和 `UpToDateCheckInput`。
- 修改 `MCUSerialBridge/wrapper/MCUSerialBridgeWrapper.csproj`、`TestDIVER.csproj`、`TestCS.csproj`、`TestBL.csproj`：
  - 添加 `UpToDateCheckInput Include="$(MSBuildThisFileDirectory)..\build\mcu_serial_bridge.dll"`。
  - 未添加 `CopyToOutputDirectory`，因为这些项目的 `OutputPath` 本身就是 `..\build\`，源 DLL 与目标目录相同，避免自拷贝。
- 验证：
  - `scons -j 12 build/mcu_serial_bridge.dll` 成功，结果为 up-to-date。
  - `dotnet build 3rd\CoralinkerSDK\CoralinkerSDK.csproj -c Debug --no-restore` 成功，有既有 nullable / CA1416 警告。
  - 并行构建 `CoralinkerSDK` 与 `CoralinkerHost` 时曾因两个构建同时写 `3rd/CoralinkerSDK/obj/Debug/net8.0/CoralinkerSDK.dll` 触发文件锁；改为顺序构建后 `CoralinkerHost` 成功。
  - `dotnet build` wrapper 4 个项目成功；首次 `MCUSerialBridgeWrapper --no-restore` 因缺少 NuGet restore 失败，允许 restore 后成功。
  - 确认存在：
    - `3rd/CoralinkerHost/bin/Debug/net8.0/mcu_serial_bridge.dll`
    - `3rd/CoralinkerSDK/bin/Debug/net8.0/mcu_serial_bridge.dll`
    - `MCUSerialBridge/build/mcu_serial_bridge.dll`

### [verify] CoralinkerHost 脱离 VS 运行
- 用户询问是否可以脱离 VS 运行。
- 确认 Debug 输出目录已存在：
  - `3rd/CoralinkerHost/bin/Debug/net8.0/CoralinkerHost.exe`
  - `3rd/CoralinkerHost/bin/Debug/net8.0/mcu_serial_bridge.dll`
- 未重新构建前端，按用户要求只尝试运行。
- 用户随后确认当前 Host 已开着并且网页能访问。

### [plan] 输入源文件 Git 历史与 Build 版本信息
- 用户提出前端编辑/构建/运行页面需要引入源文件历史追踪：
  - 保存输入源文件时自动形成 Git 记录。
  - 编辑页查看总体日志、单文件日志、左右 diff、临时 checkout、永久 revert。
  - 多人编辑时轮询 HEAD 并提示后端已有新版本。
  - Build 前有未保存内容必须保存，否则不能编译。
  - Build 和运行需要携带版本号和时间。
- 已与用户确认：
  - 使用 `3rd/CoralinkerHost/data/.git` 独立仓库。
  - 只跟踪 `data/assets/inputs/*.cs`。
- 计划文件：`input_git_history_3ba33a1a.plan.md`。
- 追加细化：
  - History UI 为右侧抽屉/Modal，左侧 commit 列表，右侧 Monaco DiffEditor，支持 `All Changes` / `Current File`。
  - 前端每 10 秒调用 `/api/history/status` 刷新 HEAD。
  - Build 期间编辑器只读，并禁用保存、新建、删除、上传输入源文件；后端写接口也需拒绝 Build 中写入。
  - Build 产物文件名保持现状，但 `.bin.json` 等生成元信息写入 `sourceCommit`、`sourceCommitShort`、`sourceCommitTime`、`buildTime`、`buildId`。
  - Node 运行加载最新一次成功 Build 的生成产物；Graph 节点另起一行显示 Commit 和 Build time。

### [implement] 输入源文件 Git 历史
- 新增后端 `GitHistoryService`：
  - 在 `3rd/CoralinkerHost/data/.git` 初始化独立 Git 仓库。
  - 只允许操作 `assets/inputs/*.cs`。
  - 使用 git 命令实现 status/log/diff/show/checkout/revert/commit。
  - 提交时通过环境变量设置 author/committer，未修改全局 git config。
- 后端接入：
  - `Program.cs` 注册 `GitHistoryService`。
  - `/api/files/write` 写入 input 源文件后自动 commit，返回 `headBefore/headAfter/committed`。
  - `/api/files/write` 支持 `baseHead` 和 `force`；HEAD 不一致且非 force 时返回 409。
  - `/api/files/newInput` 和 `/api/files/delete` 也会提交 inputs 变更。
  - Build 中 `/api/files/write`、new/delete 会返回 409，避免编译期间修改输入。
  - 新增 `/api/history/status`、`/api/history/log`、`/api/history/diff`、`/api/history/file`、`/api/history/checkout`、`/api/history/revert`。
- Build/Run 版本信息：
  - `DiverBuildService` 注入 `GitHistoryService`，Build 前检查 inputs 是否有未提交变更、是否存在 HEAD。
  - Build 成功后生成 `<logic>.build.json`，包含 `sourceCommit`、`sourceCommitShort`、`sourceCommitTime`、`buildTime`、`buildId`。
  - `BuildResult` 返回 commit/time 信息并写入 Build 日志。
  - `RuntimeSessionService.ProgramNodeAsync` 与 `DIVERSession.ProgramNode` 接收 `buildInfo`。
  - `NodeEntry` / `NodeFullInfo` / `NodeExportData` 保存 `BuildInfo`，Graph 节点可展示。
  - `/api/start` 返回 `runStartedAt`、当前 source commit 和 commit time。
- 前端接入：
  - 新增 `api/history.ts`、`stores/history.ts`，支持 status/log/diff/file/checkout/revert 和 10 秒 HEAD 轮询。
  - 新增 `components/history/HistoryPanel.vue`，右侧抽屉展示总体/当前文件提交列表、Monaco DiffEditor、临时 checkout、永久 revert。
  - `stores/files.ts` 的 `EditorTab` 增加 `baseHead`；保存时传 `baseHead`，保存成功后刷新 HEAD。
  - `HomeView.vue` 增加 History 按钮、远端 HEAD 变化 warning bar、保存冲突覆盖确认。
  - Build 前检查 dirty tabs，提示 `Save All and Build`；Build 中编辑器只读，Ctrl+S/保存按钮/新建/上传/删除被禁用。
  - Build 成功后显示 commit/time，Start 成功后显示 run commit/time。
  - `CoralNodeView.vue` 增加 Build 版本行，显示 `Commit: <short> Build: <time>`。
- 验证：
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release --no-restore` 成功，只有既有 warning。
  - `npm run build` 成功，只有 Vite chunk/动态导入 warning。
  - `ReadLints` 检查新增/修改关键文件无 linter 错误。
  - Debug 构建仍被旧 `CoralinkerHost (24968)` 锁定 Debug 输出目录；Release 构建已验证代码通过。

### [verify] CoralinkerHost 后端/前端构建
- 用户要求构建后端和前端，并提供运行命令。
- 执行后端 Release 构建：
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release --no-restore`
  - 结果：成功，0 warning / 0 error。
- 执行前端构建：
  - `npm run build`（工作目录 `3rd/CoralinkerHost/ClientApp`）
  - 结果：成功；Vite 仅报告既有 chunk size / 动态导入 warning。
- 运行建议：
  - 后端构建产物：`3rd/CoralinkerHost/bin/Release/net8.0/CoralinkerHost.exe`
  - 前端构建产物：`3rd/CoralinkerHost/wwwroot`

### [fix] History UI 显示与 Diff 展示
- 用户反馈 `http://localhost:4499/` 上没有明显看到前端修改，且 diff 展示不好看，希望左右两栏方式。
- 修改：
  - `HomeView.vue`：将 History 入口移到主 Tab 栏，紧邻 `Graph`，不再依赖当前是否有打开文件；按钮文字直接显示 `History`。
  - `HistoryPanel.vue`：移除 Monaco DiffEditor 依赖，改为固定左右两栏文本对比。
    - 左栏 Old，右栏 New。
    - 删除行红色背景，新增行绿色背景，hunk 行蓝色背景。
- 验证：
  - `npm run build` 成功。
  - 构建产物包含新 UI：`wwwroot/assets/HomeView-BkE_xc_y.js` 中可搜索到 `Input History` / `All Changes`。
- 说明：
  - 如果浏览器仍看不到变化，优先 `Ctrl+F5` 强刷；新版前端代码已经进入 `wwwroot`。
- 二次修正：
  - 用户明确要求 diff 按 VSCode 方式展示：左右联动、行号、不要显示 `+/-`。
  - `HistoryPanel.vue` 改回真正的 Monaco DiffEditor，配置 `renderSideBySide=true`、`lineNumbers=on`、`automaticLayout=true`、`enableSplitViewResizing=true`。
  - `All Changes` 模式点击 commit 后，自动选择该 commit 的第一个变更文件；也可通过文件下拉框切换文件。
  - 重新执行 `npm run build` 成功。
  - 确认新产物 `wwwroot/assets/HomeView-Hr8o3NF_.js` 包含 diff editor 相关实现。
- 三次修正：
  - History 面板的 `All Changes` / `Current File` 改为更明显的 Scope 胶囊按钮，并显示当前范围提示。
  - 从 Graph 进入时若当前有打开文件，仍默认 Current File，但用户可以稳定切换到 All Changes。
  - 增加两个 commit 间对比：选中目标 commit 后，可通过 base commit 下拉选择任意基准 commit；默认仍为 previous commit。
  - 重新执行 `npm run build` 成功，新产物为 `wwwroot/assets/HomeView-DkvcpmLD.js`。
- 四次修正：
  - 用户认为 From/To 不应从右侧下拉选择，应都从左侧 commit 列表选择。
  - History 左侧每条 commit 增加 `From` / `To` 按钮，列表项用左侧色条标出当前 From/To。
  - 右侧 toolbar 改为显示 `From <hash> -> To <hash>`，只保留文件选择和操作按钮。
  - 面板字体统一为 Inter，diff editor 字体显式设置为 `JetBrains Mono/Fira Code/Consolas`，字号 13、行高 20。
  - 重新执行 `npm run build` 成功，新产物为 `wwwroot/assets/HomeView-CKS3r0y2.js`。
- 五次修正：
  - 用户指出正常 Save 会立刻落盘并 commit，因此和磁盘 dirty 比较意义不大；真正需要比较的是前端编辑器内存 dirty 内容。
  - `HomeView.vue` 向 `HistoryPanel` 传入当前 active tab 的 `currentContent` 和 `currentDirty`。
  - `HistoryPanel.vue` 增加 `Compare With Unsaved` 按钮，仅当前文件有未保存内容且选中文件匹配时启用。
  - 该对比不落盘、不提交；左侧为选中 commit 的文件内容，右侧为前端内存中的未保存文本。
  - 重新执行 `npm run build` 成功，新产物为 `wwwroot/assets/HomeView-BR5RweYO.js`。
- 六次修正：
  - 用户询问删除文件是否会 commit，以及新建文件后 10 秒内编辑会误报“后端有新版本”。
  - 删除 `.cs` 后端已会 commit：`/api/files/delete` 对 `assets/inputs/*.cs` 调用 `CommitInputsIfChanged`。
  - 修复新建后的前端 HEAD 同步：
    - `historyStore` 新增 `markHeadKnown(head)`。
    - `filesStore.createNewInput()` 使用后端 `/api/files/newInput` 返回的 `head` 立即标记为已知 HEAD，并设置新 tab 的 `baseHead`。
    - `filesStore.deleteFile()` 使用删除 API 返回的 `head` 标记为已知 HEAD，避免删除后误报远端更新。
  - 重新执行 `npm run build` 成功，新产物为 `wwwroot/assets/HomeView-D_IMbK53.js`。

### [fix] Build 后 Graph 节点 BuildTime 不刷新
- 用户指出 Logic 名字来自编译产物，一个源文件可以生成多个 Logic；Build 后实际执行版本已更新，但 Graph 节点上的 BuildTime 不更新，必须重新选择 Logic 才刷新。
- 结论：
  - Logic 名字来自 `/api/logic/list`，后端扫描 `data/assets/generated/*.bin`，因此一个源文件编译出多个 Logic 时会出现多个 Logic 下拉项。
  - Build 后 `HomeView.reprogramAllNodes()` 会调用 `programNode()` 重新下发最新生成产物，后端 `DIVERSession` 的 `buildInfo` 已更新。
  - 但前端只执行了 `runtimeStore.refreshNodes()`，没有刷新 `GraphCanvas` 的本地 node data，所以节点卡片仍显示旧 buildInfo。
- 修复：
  - `HomeView.vue` 的 `reprogramAllNodes()` 在重新编程并 `runtimeStore.refreshNodes()` 后，额外调用 `graphCanvasRef.value?.refreshNodes()`。
  - 该方法会从后端 `DIVERSession.GetNodeInfo()` 重新加载节点数据，包括 `buildInfo`，从而刷新 Graph 节点 BuildTime。
- 验证：
  - `npm run build` 成功，新产物为 `wwwroot/assets/HomeView-BuU-0mr-.js`。

### [fix] Git 中文路径 diff 崩溃
- 用户新建 `小黄瓜屁股大.cs` 后开始编辑，前端提示后端有新版本，点击 View Diff 后后端崩溃：
  - `GitHistoryService.NormalizeInputPath`
  - `Path must be under assets/inputs and end with .cs`
- 判断：
  - 不是中文文件名本身不能用。
  - 更可能是 Git 默认 `core.quotepath=true`，`git diff-tree --name-only` 等命令会把非 ASCII 路径转成带引号/反斜杠转义的形式。
  - 前端把这个被转义的路径传回 `/api/history/diff`，后端校验不再识别为 `assets/inputs/*.cs`。
- 修复：
  - `GitHistoryService.RunGitAllowFailure` 中，所有 git 命令统一加参数：
    - `-c core.quotepath=false`
  - 这样中文路径以 UTF-8 原样输出，前端拿到的仍是 `assets/inputs/小黄瓜屁股大.cs`。
- 验证：
  - `npm run build` 成功。
  - 后端 Release 构建尝试时被当前运行的 `CoralinkerHost (27416)` 锁住 `bin/Release/net8.0/CoralinkerHost.exe`；需停止当前 Host 后再构建。此前同代码线 Release 已通过，当前修改点很小且为 git 参数注入。

### [plan] PEAM Lite 客户说明书方向修正
- 用户指出上一版 CORAL-NODE-V2.1 用户手册规划偏离意图：
  - 客户不能看到电路图、MCU 引脚、DMA、BSP、`scons` 等内部细节。
  - 客户手册不采用 Markdown，应复用现有 TeX 工程 `CORAL-NODE-V2.1/Coralink_Node_Manual/` 与 `macro.tex`。
  - 对外产品名为 `PEAM Lite (DIVER Node V0.1)`；`CORAL-NODE-V2.1` 仅为内部硬件标识号。
  - 核心能力应为 DIVER Runtime、动态加载、多节点联合控制、神经末梢结构、变量表编程；DI/DO/RS485/CAN 仅作为对外接口能力。
  - CoralinkerHost 面向客户是使用工具，不是开发对象。
- 已更新计划文件 `coral_node_manual_48a4499c.plan.md`：
  - 改为基于 TeX 工程重构。
  - 删除新增 Markdown 手册方案。
  - 将 `sch.md` 限定为内部校对来源，不在客户正文展开。
  - 重排大纲为产品命名、DIVER Runtime、适用边界、接口能力、开箱上电、Host 用户操作、多节点、维护排障、客户可见规格。

### [plan] PEAM Lite 正面接口补充
- 用户提供 PEAM Lite (DIVER Node V0.1) 正面接口照片与口头说明。
- 已补充到计划文件 `coral_node_manual_48a4499c.plan.md` 的“已收到的硬件接口补充”：
  - 黑色接口多为 `2EDGKS` 系列，适配 `15EDGKNH`。
  - 接口顺序：急停、电源、单输入 x4、单输出 x4、多输入 x2、多输出 x1、4 输入 4 输出 x2、485 x3、CAN x2、USB、100M 网口、灯带。
  - 颜色标识：紫色急停、红色 24V、蓝色 0V、绿色输入、黄色输出；485/CAN 的草绿与橙黄色深浅区分 H/A 与 L/B；灯带白色为信号。
  - 产品价值表达：每个传感器、执行器、开关、继电器等尽量独占一个接口，同一接口内带电源、地和信号，使线束清爽、接线直观、便于排障。

### [plan] PEAM Lite 前面板接口补充
- 用户提供 PEAM Lite (DIVER Node V0.1) 前面板照片与口头说明。
- 已补充到计划文件 `coral_node_manual_48a4499c.plan.md`：
  - `POWER IN` 为 XT30 24V 电源接口，允许电压 `18-28VDC`。
  - `UPLINK` 用于连接上一节点；实际使用网线中的 USB 差分对，其他 3 对走急停和触边信号，只能使用标准 `T568B` 直通网线。
  - `TYPE-C` 用于连接根节点；`TYPE-C` 与 `UPLINK` 的 USB Data 是同一路，不能同时连接。
  - 拨码开关用于急停设置：`First` 第一个节点、`Last` 最后一个节点、`Short` 短接当前节点急停（当前节点没有接急停开关时使用）。
  - 红灯为触边灯，触边触发时亮起；绿灯为急停灯，急停未按下且全链路导通时亮起。

### [plan] PEAM Lite 后面板接口补充
- 用户提供 PEAM Lite (DIVER Node V0.1) 后面板照片与口头说明。
- 已补充到计划文件 `coral_node_manual_48a4499c.plan.md`：
  - `DOWNLINK` 用于连接下一节节点。
  - `POWER OUT` 直接从 `POWER IN` 连出，可用于电源级联。
  - `Upgrade` 用于进入固件升级模式；具体方法不在接口一览中展开，应放入维护/升级章节。
  - `Reset` 用于重启节点，并可辅助 `Upgrade` 完成升级操作。
  - `UPLINK` 与 `DOWNLINK` 是节点级联接口，不是普通以太网口，不要连接交换机、路由器、电脑网口等其他网络设备。

### [draft] 新建 PEAM Lite TeX 手册草稿
- 用户要求在 `CORAL-NODE-V2.1` 下新建文件夹开始落盘，每个文件先写，后面再补全。
- 新建目录：`CORAL-NODE-V2.1/PEAM_Lite_DIVER_Node_Manual/`。
- 新建 TeX 文件：
  - `main.tex`
  - `macro.tex`（复用 `../Coralink_Node_Manual/macro`）
  - `section1_product.tex`
  - `section2_runtime.tex`
  - `section3_interfaces.tex`
  - `section4_power_on.tex`
  - `section5_host.tex`
  - `section6_maintenance.tex`
  - `glossary.tex`
- 初稿已覆盖：
  - 对外产品名 `PEAM Lite (DIVER Node V0.1)` 与内部硬件标识 `CORAL-NODE-V2.1` 的区分。
  - DIVER Runtime、动态加载、多节点、变量表、神经末梢/脊髓式架构。
  - 正面、前面板、后面板客户可见接口信息。
  - 开箱上电检查、CoralinkerHost 客户使用流程、维护与常见故障排查。

### [implement] Root Remote Runtime 第一版
- 用户确认 Root Remote Runtime 方案并要求实现。
- 公共 API：
  - `DiverTest/RunOnMCU.cs` 新增 `LogicRunOnRootAttribute`、`AsControlItem`、`RootLogic<T>`。
  - `3rd/CoralinkerKitDocs/stubs/CartActivator.cs` 同步 stub。
  - `AsControlItem` 只标记 Root 控制输入字段，字段类型限制为基础类型：bool、byte/sbyte、short/ushort、int/uint、float。
- Build 扩展：
  - `DiverBuildService` 扫描编译出的 .NET assembly，查找 `[LogicRunOnRoot]` 且继承 `RootLogic<TCart>` 的类型。
  - 解析 cart fields 与 control fields。
  - 生成 `assets/generated/<RootLogicName>.root.json`，包含 Root logic 类型、assembly path、scanInterval、commit/build 信息、字段元信息。
  - `BuildResult` 返回 `RootLogics`。
- Runtime：
  - 新增 `RootRuntimeService`。
  - 加载 Root logic assembly，创建 Root logic 和 cart。
  - 周期执行 `Operation()`。
  - 执行前从 `DIVERSession` 读取 cart 字段；执行后把 Root 写出的 UpperIO 写回 `DIVERSession`。
  - 捕获异常并写入 Terminal，避免 Host 崩溃。
  - Stop 时取消 loop 并 unload AssemblyLoadContext。
- DIVERSDK / DIVERSession：
  - 新增 `SetCartFieldAndSignalUpperIO()`。
  - Root 写 UpperIO 后会标记所有运行节点 `_upperIOPending` 并唤醒发送线程。
- Host API：
  - 新增 `/api/root/logics`
  - 新增 `/api/root/configure`
  - 新增 `/api/root/state`
  - 新增 `/api/root/control/meta`
  - 新增 `/api/root/control/set`
- 生命周期：
  - `RuntimeSessionService.StartAsync()` 在 MCU session 启动成功后启动 Root runtime。
  - `RuntimeSessionService.StopAsync()` 先停止 Root runtime，再停止 MCU session。
- 前端：
  - 新增 `api/root.ts`。
  - `types/index.ts` 增加 Root metadata/state 类型。
  - `RootNodeView.vue` 增加 Root Logic 下拉、运行状态、commit/build 信息、statusText。
  - `runtimeStore.refreshFieldMetas()` 合并 Root control fields，使遥控器可绑定 Root 控制字段。
  - `runtimeStore.setVariable()` 对 Root control field 调用 `/api/root/control/set`，不走 MCU UpperIO。
- 新建模板：
  - `/api/files/newInput` 增加 `templateKind`。
  - 前端 New Input File 对话框增加 `MCU Logic` / `Root Logic` 选择。
  - Root Logic 模板为纯 Root 差速遥控示例：`joystickX/joystickY` 控制输入，经差速分解写入 `left_diff_speed/right_diff_speed` UpperIO。
  - Root 模板不包含 `RunOnMCU` 调用，避免误导用户。
- 验证：
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release --no-restore` 成功；2026-05-14 04:25 后端停止后再次构建通过，0 warning / 0 error。
  - `npm run build` 成功。
  - `ReadLints` 检查新增/修改关键文件无错误。

### [fix] Root 节点 Logic 选择 UI 对齐
- 用户反馈 Root 节点上的 Logic 选择 UI 风格不好看，和现有 Coral 节点不一致。
- 修改 `RootNodeView.vue`：
  - 使用 Naive UI `NSelect` 替代原生 `select`。
  - 布局改为和 Coral 节点一致的 `config-row` / `config-label` / `config-value` 风格。
  - Root 节点显示 Root Logic、State、Build、Status 四行信息。
  - Build 信息使用 monospace 风格展示 commit/build time。
  - Build 完成后通过 `buildVersion` watch 自动刷新 Root logic 列表。
- 验证：
  - `npm run build` 成功，新产物为 `wwwroot/assets/HomeView-Dq29xr2Y.js`。

### [fix] 变量方向四分类
- 用户指出 Root 的 `AsControlItem` 在 Variables 面板里显示成 LowerIO，说明前端没有区分 Upper/Lower/Mutual/ControlItem。
- 修改：
  - `types/index.ts` 为 `CartFieldMeta`、`CartFieldValue`、`VariableValue` 增加 `direction?: 'upper' | 'lower' | 'mutual' | 'control'`。
  - `runtimeStore.refreshFieldMetas()` 把 MCU 字段映射为 `upper/lower/mutual`，把 Root control fields 映射为 `control`。
  - `runtimeStore.refreshVariables()` 保留 MCU 变量方向，并补回 Root control field 的本地变量值。
  - `VariablePanel.vue` 增加 `MutualIO` 和 `ControlItem` 图例与行颜色，不再用“是否可控”推断 Upper/Lower。
  - `ControlWindow.vue` 的可绑定变量过滤改为 `direction !== 'lower'`，因此 ControlItem 可绑定，LowerIO 仍只读。
- 验证：
  - `npm run build` 成功，新产物为 `wwwroot/assets/HomeView-B30sCUQL.js`。

### [fix] Root cart UpperIO 不允许直接遥控
- 用户指出同名变量可能同时出现在子节点和 Root 中；变量本身不能只有一个 `origin`，因为它会在不同地方出现。
- 规则确认：
  - 如果一个变量名被 Root cart 声明为 UpperIO/LowerIO/Mutual，它属于 Root 逻辑的输入/输出，不允许被遥控面板直接驱动。
  - 只有 Root `[AsControlItem]` 字段可以被遥控面板绑定。
  - MCU UpperIO/MutualIO 在没有被 Root cart 接管时仍可控。
- 修改：
  - `runtimeStore` 增加 `rootCartNames` 与 `rootControlNames` 两个集合。
  - `refreshFieldMetas()` 中 Root cart fields 会覆盖同名变量的可控性为 false，Root control fields 覆盖为 true。
  - `refreshVariables()` 中变量可控性按声明集合合并，不再用单一 `origin` 判断。
  - `ControlWindow` 绑定列表回到使用 `controllableVarNames`，由 runtimeStore 统一决定是否可绑定。
- 验证：
  - `npm run build` 成功，新产物为 `wwwroot/assets/HomeView-CPKiNHzK.js`。

### [fix] Root cart 变量类型和方向元信息回填
- 用户反馈 `left_diff_speed` 显示紫色，类型为 `Unknown`，怀疑类型链路没有完全打通。
- 链路排查：
  - Root logic build 会在 `.root.json` 中记录 cart fields：`left_diff_speed` 应为 `type=Int32`、`direction=upper`。
  - `/api/root/state` 会把 Root cart fields 返回给前端，`runtimeStore.refreshFieldMetas()` 也会合并到 `fieldMetas`。
  - 问题出在变量值链路：`refreshVariables()` 和 SignalR `updateVariables()` 写入 `variables` 时仍优先使用后端变量快照里的 `type/typeId`，而 `DIVERSession` 对 Root 生成的变量可能只知道 `Unknown` 和默认 mutual。
- 修改：
  - `runtimeStore.refreshVariables()` 写入变量时改为 `meta?.type/typeId` 优先，变量值只使用快照中的 `value`。
  - `runtimeStore.updateVariables()` 实时推送时同样通过 `fieldMetas` 回填 `type/typeId/direction`，避免 SignalR 更新把类型冲回 `Unknown`。
- 验证：
  - `ReadLints` 检查 `runtime.ts` 无错误。
  - `npm run build` 成功；仅保留既有 SignalR 注释 warning、动态导入 chunk warning、大 chunk warning。

### [architecture] Root runtime 作为 DIVERSession 虚拟节点
- 用户指出前端修正 Root 变量类型不是正确边界；后续 Medulla 可能直接对接 `DIVERSession`，不应依赖 CoralinkerHost 前端逻辑。
- 设计调整：
  - Root runtime / Medulla / CLI 上层控制器都应作为 `DIVERSession` 内部虚拟节点或变量声明源注册。
  - `_variables` 只保存值；变量类型、方向、可控性由 MCU `NodeEntry.CartFields` + 虚拟节点声明统一合并。
  - 前端只消费 `/api/variables/meta` 与 `/api/variables`，不再用 `/api/root/state` 修正变量显示。
- SDK 修改：
  - `DIVERSession` 新增 `VirtualCartFieldDeclaration`、`VirtualNodeEntry`、`DeclaredCartField`。
  - 新增 `RegisterVirtualNode(...)` / `UnregisterVirtualNode(...)`。
  - 新增 `SetVirtualControlField(...)`，保留 `SetRootControlField(...)` 作为兼容别名。
  - `CartFieldMeta` / `CartFieldValue` 扩展 `IsControl`、`IsRootCart`、`Controllable`、`Direction`。
  - `GetAllCartFieldMetas()` / `GetAllCartFields()` 改为基于统一声明表返回类型与方向。
  - `SetCartField()` 改为按 session 合并声明判断可写性：LowerIO 或 Root/虚拟节点接管的 cart 输出不可由普通遥控入口直接写。
  - `SetCartFieldAndSignalUpperIO()` 保留为 Root/上层逻辑发布 UpperIO 的入口。
- Host 修改：
  - `RootRuntimeService` 在配置、状态读取和启动时注册 Root 虚拟节点。
  - Root control 写入改走 `DIVERSession.SetVirtualControlField()`。
  - `/api/variables/meta` 和 `/api/variables` 返回前确保已选 Root logic 的虚拟节点声明已注册。
  - `/api/variable/set` 对不可写变量返回 “read-only or managed by a virtual node”。
  - `VariableInspectorPushService` 的 SignalR 变量快照增加 `typeId` 和 `controllable`。
- 前端修改：
  - `runtimeStore.refreshFieldMetas()` 移除 `/api/root/state` 合并 Root 元信息的特殊处理。
  - `runtimeStore.refreshVariables()` 和 `updateVariables()` 只信 session 变量 API 返回的 `type/typeId/direction/controllable`。
  - `types/index.ts` 为变量元信息和值补充 `isControl`、`isRootCart`、`controllable`。
- 文档：
  - `3rd/CoralinkerSDK/README.md` 作为主文档，新增统一变量声明表和 Root/Medulla 虚拟节点使用示例。
  - `3rd/CoralinkerHost/README.md` 明确 Host 不决定变量类型，VariablePanel/ControlWindow 只消费 DIVERSession 变量 API。
  - `3rd/CoralinkerKitDocs/04-variables-and-io.md` 说明 Root 接管同名变量和 ControlItem 语义。
  - `3rd/CoralinkerKitDocs/06-remote-control.md` 说明遥控器绑定 Root ControlItem，而非直接绑定 Root cart 输出。
  - `3rd/CoralinkerKitDocs/README.md`、`README_CN.md`、`README.md`、`medulla2/README.md` 增加对接说明。
- 验证：
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release --no-restore` 成功（首次正常构建 0 warning / 0 error）。
  - 增加 `SetVirtualControlField` 别名后再次后端 Release 构建成功，0 error，输出既有 nullable/Windows API warning。
  - `npm run build` 成功；仅有既有 SignalR 注释 warning、动态导入 chunk warning、大 chunk warning。
  - `ReadLints` 检查关键 C#/TS 文件无错误。

### [fix] ControlItem 实时推送方向
- 用户截图显示 `joystickX` / `joystickY` 已可编辑、类型为 `f32`，但行底色不是 ControlItem 蓝色。
- 根因：
  - `VariablePanel.vue` 的 `.control-io` 蓝色样式存在。
  - `VariableInspectorPushService.BuildVarsSnapshot()` 计算 `direction` 时只看 `IsLowerIO/IsUpperIO/IsMutual`，没有先判断 `IsControl`。
  - SignalR 实时推送把 ControlItem 推成 `direction=none`，覆盖前端初始 meta 中的 `control`。
- 修改：
  - `VariableInspectorPushService` 对 `field.IsControl` 返回 `direction=control`，图标为 `gamepad`。
  - `runtimeStore.updateVariables()` 忽略旧后端/瞬时推送中的 `direction=none`，避免覆盖已有方向。
- 验证：
  - `ReadLints` 检查相关文件无错误。
  - 后端 Release 构建成功，0 error。
  - `npm run build` 成功。

### [cleanup] 去除变量推送中的 UI fallback 与 `none` 方向
- 用户指出后端推送 `direction=none` 和 `icon=circle` 不合理，这些是 UI fallback 泄漏到后端 DTO 的历史遗留。
- 边界调整：
  - `DIVERSession` 继续作为变量类型、方向、可控性的唯一裁决者。
  - `VariableInspectorPushService` 只转发 session 的领域值，不再自己合成 UI 图标或非法方向。
  - 前端只消费后端合法方向，不再通过 `normalizeDirection()` 修补旧值。
- 修改：
  - `VariableInspectorPushService.BuildVarsSnapshot()` 删除 `icon` 输出，`direction` 直接使用 `CartFieldValue.Direction`。
  - `DIVERSession.GetAllCartFields()` 对未声明 `_variables` 条目跳过，不再返回 `Unknown/unknown`。
  - `DIVERSession.SetCartField()` 拒绝未声明字段，`SetCartFieldAndSignalUpperIO()` 拒绝未声明字段作为 UpperIO 发布。
  - `DIVERSession.DirectionOf()` 对没有合法方向的声明抛出异常，避免静默产生 `none`。
  - `DiverBuildService` 生成 Root metadata 前先过滤 `[AsUpperIO]` / `[AsLowerIO]` 字段，未标注字段不再生成 `direction=none`。
  - `runtimeStore` 删除 `normalizeDirection()`，SignalR 快照类型删除 `icon`，变量方向直接使用后端值。
  - `types/index.ts` 将 `CartFieldMeta`、`CartFieldValue`、`VariableValue` 的 `direction` / `controllable` 收紧为必填。
- 验证：
  - `ReadLints` 检查 `DIVERSession.cs`、`VariableInspectorPushService.cs`、`DiverBuildService.cs`、`runtime.ts`、`types/index.ts` 无错误。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Debug` 成功，0 error，仍有既有 nullable/Windows API warning。
  - `npm run build` 成功，仍有既有 SignalR 注释、动态导入和大 chunk warning。

### [fix] Root runtime 控制字段 JsonElement 转换错误
- 用户反馈 Start 后 `joystickX/Y` 有写入，但 `left_diff_speed/right_diff_speed` 仍为 0，怀疑 Root `Operation()` 未执行。
- 运行日志确认：
  - `[root] ERROR: Unable to cast object of type 'System.Text.Json.JsonElement' to type 'System.IConvertible'.`
  - 错误发生在 Root loop 的 `ApplyControlsToLogic()` 阶段，即 `Operation()` 调用之前。
- 根因：
  - 遥控器统一走 `/api/variable/set`。
  - 请求 DTO 的 `object? Value` 反序列化后是 `JsonElement`。
  - 前端未传 `typeHint` 时，后端直接把 `JsonElement` 存入 `DIVERSession`。
  - Root runtime 再把 session 值赋给 `float joystickX/Y` 时执行 `Convert.ToSingle(JsonElement)`，触发异常，导致每周期都中断在 `Operation()` 前。
- 修改：
  - `/api/variable/set` 写入前确保 Root 虚拟节点声明已注册，并从 `DIVERSession.GetAllCartFieldMetas()` 补齐变量真实类型。
  - 有真实类型后复用 `ValueParser.ParseValueByType()`，避免 JSON 原始值进入 session。
  - `RootRuntimeService.ConvertValue()` 先执行 `CoerceJsonValue()`，防御其它入口残留的 `JsonElement`。
  - `DIVERSession.SetCartField()`、`SetVirtualControlField()`、`SetCartFieldAndSignalUpperIO()` 按声明 `TypeId` 做最终类型归一化，避免 SDK/session 边界再次存入 JSON transport 类型。
  - `RootRuntimeService.PublishUpperFields()` 对发布失败写日志，不再静默忽略。
- 验证：
  - `ReadLints` 检查 `DIVERSession.cs`、`ApiRoutes.cs`、`RootRuntimeService.cs` 无错误。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Debug` 成功，0 error，仍有既有 nullable/Windows API warning。
  - Release 产物未重编：用户当时正在运行 `bin\Release\net8.0\CoralinkerHost.exe`，需关闭后再构建 Release。

### [perf] Medulla 原生 C# 写入快路径
- 用户指出 Medulla 不是网页，而是原生 C# 程序，会通过 SDK/session 原生接口对接；`DIVERSession` 必须同时支持 `JsonElement` 和原始 C# value，且不能让原生路径受 Web 兼容逻辑拖累。
- 修改：
  - `CoerceValueToType()` 首先识别 `JsonElement`，仅 Web/JSON transport 值进入 JSON 解包分支。
  - 原生 C# 值增加精确类型 fast path：`bool/byte/sbyte/char/short/ushort/int/uint/float` 与声明 `TypeId` 匹配时直接返回。
  - 只有类型不匹配但可转换时才走 `Convert.*`，用于兼容 `double -> float`、`int -> short` 等调用。
  - `CoerceJsonValueToType()` 按目标 `TypeId` 直接读取 JSON 数值，避免 JSON 先转中间类型再二次转换。
- 验证：
  - `ReadLints` 检查 `DIVERSession.cs` 无错误。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Debug` 成功，0 error，仍有既有 nullable/Windows API warning。

### [docs] 补充 DIVERSession 写入值类型规则
- 用户要求将 Medulla 原生 C# 对接语义写入 README，并编译 Release。
- 修改 `3rd/CoralinkerSDK/README.md`：
  - 在“数据管理”下新增“写入值类型规则”。
  - 明确 `SetCartField()`、`SetVirtualControlField()`、`SetCartFieldAndSignalUpperIO()` 接受 `object`，最终按声明 `TypeId` 归一化。
  - 明确 Medulla/CLI/桌面程序优先传真实基础类型；类型匹配时走 fast path，不走 JSON 解包，也尽量不走 `Convert.*`。
  - 明确 Web/HTTP 可传 `System.Text.Json.JsonElement`，session 会按目标 `TypeId` 解析，避免 transport 类型进入 `_variables`。
  - 修正 Root/Medulla 示例中 `joystickX` 的 `TypeId` 为 `8`（Single），示例写入值改为 `0.35f`。
- 验证：
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release` 成功，0 error，仍有既有 69 个 warning。

### [feature] Root 可选择不运行逻辑并增加独立日志分栏
- 用户要求：
  - PC/Root Logic 下拉框允许选择“不跑任何 Root 程序”。
  - Root 逻辑中的 `Console.WriteLine` 需要在前端有独立日志分栏，位置在 `Build` 后、Node1/Node2 前。
- 后端修改：
  - `RootRuntimeService.StartAsync()` 在 `RootLogicName` 为空时注销 Root 虚拟节点并跳过启动，写入 Root 日志 `[root] No Root logic configured; skipped`。
  - `TerminalBroadcaster` 新增 Root runtime 日志缓冲 `_rootBuffer`。
  - 新增 `RootLineAsync()`，通过 SignalR `rootLine` 推送 Root 日志。
  - 新增 `GetRootHistory()` / `ClearRootHistory()`。
  - `/api/logs/root` 和 `/api/logs/root/clear` 接入 Root 日志历史和清空。
  - Root runtime 的 started/stopped/error/publish-failed 日志改走 Root 通道，不再混入 Terminal。
  - `Operation()` 调用期间用 `RootConsoleWriter` 临时捕获 `Console.Out` 和 `Console.Error`，转发到 Root 分栏，同时 tee 到原始控制台。
- 前端修改：
  - `RootNodeView.vue` 的 Root Logic 下拉新增 `None (no PC Logic)`，保存为 `null`，Build 显示 `None`。
  - `logs` store 新增 `rootLines`、`appendRoot()`、`clearRoot()`、`loadRootHistory()`。
  - `useSignalR()` 连接后加载 Root 历史日志，并监听 `rootLine`。
  - `runtime.ts` API 新增 `getRootLogs()` / `clearRootLogs()`。
  - `TerminalPanel.vue` 新增固定 `Root` Tab，顺序为 `Terminal`、`Build`、`Root`、节点 tabs，并对 Root error 行显示 badge。
- 验证：
  - `ReadLints` 检查相关文件无错误。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release` 成功，0 error，仍有既有 50 个 warning。
  - `npm run build` 成功，仍有既有 SignalR 注释、动态导入和大 chunk warning。

### [fix] Root 日志分栏边界调整
- 用户指出 `[root] Started Root (...)` 这类 Root runtime 生命周期日志应写到 Terminal，不应进入 Root 分栏。
- 规则确认：
  - Terminal：Host/Root runtime 的 started、stopped、skip、error、publish failed 等系统运行日志。
  - Root：只显示 Root logic 自己在 `Operation()` 中调用的 `Console.WriteLine` / `Console.Error`。
- 修改：
  - `RootRuntimeService.StartAsync()` 的 no logic skip 和 started 日志改回 `TerminalBroadcaster.LineAsync()`。
  - `StopAsync()` 的 stopped 日志改回 Terminal。
  - Root loop 捕获到的 runtime exception 和 UpperIO publish failed 日志改回 Terminal。
  - `InvokeOperationWithConsoleCapture()` 仍只在调用用户 `Operation()` 期间捕获 `Console.Out` / `Console.Error` 并写入 Root 分栏。
- 验证：
  - `ReadLints` 检查 `RootRuntimeService.cs` 无错误。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release` 成功，0 error，仍有既有 50 个 warning。

### [template] Root 默认模板增加 Console 示例
- 用户要求 Root 默认代码模板保留左右轮差速拆分，并增加一个每隔 1 秒输出一次 `Console` 的示例。
- 修改 `ApiRoutes.GenerateRootTemplate()`：
  - 新增 `private float _logElapsedMs;`。
  - `Operation()` 中继续执行 `joystickY + joystickX` / `joystickY - joystickX` 的左右轮分解。
  - 使用 Root runtime 注入的 `interval` 累加时间，约每 1000ms 输出一次：
    `DiffDrive L={cart.left_diff_speed}, R={cart.right_diff_speed}`。
  - 该输出来自 Root logic 内部 `Console.WriteLine`，会进入前端 `Root` 分栏。
- 验证：
  - `ReadLints` 检查 `ApiRoutes.cs` 无错误。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release` 成功，0 error，仍有既有 50 个 warning。

### [fix] 文件删除、Git 历史列表和项目导入导出
- 用户要求修正 CoralinkerHost 文件/Git 相关体验：
  - Generated 编译产物不允许删除。
  - 新建输入源文件的 Git commit message 不应再叫 `save`，应叫 `created`。
  - commit 列表需要直观看到每个 commit 的新增/删除行数。
  - project import/export 需要包含 `data/.git` 历史。
- 后端修改：
  - `/api/files/delete` 拒绝 `assets/generated/` 下的路径，避免删除编译生成物。
  - `/api/files/newInput` 自动提交信息改为 `created yyyy-MM-dd HH:mm:ss`。
  - 输入源删除的自动提交信息改为 `deleted yyyy-MM-dd HH:mm:ss`。
  - `GitHistoryService.GetLog()` 对每个 commit 执行当前 scope 下的 `git show --numstat --format=`，返回 `files/additions/deletions`。
  - `GitCommitInfo` 增加 `Additions` / `Deletions` 字段。
  - project export 额外打包 `data/.git` 和 `.gitignore`。
  - project import 时清理旧 `.git` / `.gitignore`，并恢复归档中的 Git 历史。
  - zip 导入加入目标路径校验，避免恶意归档写出 `data` 目录。
- 前端修改：
  - `GitCommitInfo` 类型增加 `additions` / `deletions`。
  - `HistoryPanel` commit 列表显示 `+N` / `-N` diffstat 和文件数量。
  - 资产树右键菜单对 Generated 文件显示 Locked；删除入口也会拦截 Generated 路径。
- 验证：
  - `ReadLints` 检查 `ApiRoutes.cs`、`GitHistoryService.cs`、`index.ts`、`HistoryPanel.vue`、`TreeNode.vue`、`AssetTree.vue` 无错误。
  - `npm run build` 成功，仍有既有 SignalR 注释、动态导入和大 chunk warning。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release` 成功，0 error，仍有既有 50 个 warning。

### [ui] History 入口归入 Files 面板
- 用户指出 History 更像文件管理功能，不应放在 Graph 右侧/主编辑 Tab 栏里；右上 `Assets` 也可以更名为 `Files`。
- 修改 `HomeView.vue`：
  - 主编辑 Tab 栏移除 `History` 入口，只保留 `Graph` 和打开文件 Tab。
  - 右上面板标题由 `Assets` 改为 `Files`。
  - `Files` 面板头部新增 `History` 按钮，与 `+ New` 并列。
  - 当 `remoteChanged` 为真时，`History` 按钮使用 warning 样式，提示当前有新的后端保存版本。
  - 保留顶部 HEAD 变化提示条中的 `View Diff` 入口，作为冲突/变更场景的直接入口。
- 验证：
  - `ReadLints` 检查 `HomeView.vue` 无错误。
  - `npm run build` 成功，仍有既有 SignalR 注释、动态导入和大 chunk warning。

### [fix] Project Load 导入 Git object 文件 Access denied
- 用户在 Load project 时看到：
  - `[UI][05-17 14:28:01.954] ERROR: Import failed: Error: Access to the path 'aa11f0f7b1abadfca727d48f289dd1b5a45375' is denied.`
- 判断：
  - 该文件名形态符合 Git object 文件。
  - 上一轮加入 project export/import Git 历史后，import 会删除旧 `data/.git` 再恢复 zip 内容。
  - Windows 下 Git object 文件可能带只读属性，直接 `Directory.Delete(..., recursive: true)` 会因文件属性抛 `UnauthorizedAccessException`。
- 修改 `ApiRoutes.cs`：
  - 新增 `DeleteDirectoryIfExists()`，删除目录前递归将文件和目录属性恢复为 `FileAttributes.Normal`。
  - 新增 `DeleteFileIfExists()`，删除 `.gitignore` 前恢复普通属性。
  - import 清理 `inputs`、`generated`、`.git`、`.gitignore` 统一使用这些 helper。
  - zip 解压覆盖已有目标文件前，先将目标文件属性设为 `Normal`。
- 验证：
  - `ReadLints` 检查 `ApiRoutes.cs` 无错误。
  - 常规 Release 构建失败原因是当前正在运行的 `CoralinkerHost (38248)` 锁住 `bin/Release/net8.0/CoralinkerHost.exe` / `.dll`，不是代码编译错误。
  - 改用临时输出目录验证编译：`dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release -p:OutputPath="bin\Release-importfix\"` 成功，0 error，仍有既有 50 个 warning。
  - 用户随后要求再跑一次正常编译；标准 Release 构建 `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release` 成功，0 error，仍有既有 50 个 warning，产物写入 `3rd/CoralinkerHost/bin/Release/net8.0`。
