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
