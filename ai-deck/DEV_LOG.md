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
