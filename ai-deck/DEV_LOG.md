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

## 2026-06-08 18:47 UTC+8 — V2.1 digital_io 输出重排

- 用户明确要求本轮只处理 `digital_io`，不继续分析/修改 CAN。
- 根据用户提供的实际接线关系整理输出映射：
  - 最末级（离 MCU 最远）74HC595 `D0..D7 -> OUT0..OUT7`
  - 中间级 74HC595 `D4..D7 -> OUT8..OUT11`
  - 最靠近 MCU 的 74HC595 `D0..D7 -> OUT12..OUT19`
- 在 `MCUSerialBridge/mcu/bsp/CORAL-NODE-V2.1/digital_io.c` 中新增 `bsp_encode_outputs()`：
  - 逻辑输出 `OUT0..19` 重排为 SPI 原始发送位 `output_raw`
  - 结果位布局：
    - raw `bit0..7 = OUT0..7`
    - raw `bit12..15 = OUT8..11`
    - raw `bit16..23 = OUT12..19`
    - raw `bit8..11` 空置
- `bsp_digital_io_refresh()` 中将
  - `uint32_t output_raw = g_bsp_digital_outputs & BSP_SHIFT_OUTPUT_MASK;`
  - 改为
  - `uint32_t output_raw = bsp_encode_outputs(g_bsp_digital_outputs);`
- 同时移除 `digital_io.c` 中未使用的 `chip/periph.h` 头文件。
- 在 `MCUSerialBridge/mcu/bsp/CORAL-NODE-V2.1/sch.md` 增补输出位映射说明。
- `ReadLints` 检查 `digital_io.c` 无错误。

## 2026-06-08 19:14 UTC+8 — V2.1 清理重编并重生 UPG

- 用户怀疑现场不亮可能是旧产物残留，明确要求确认是否执行过 `scons -c`。
- 已执行完整流程：
  - `scons BUILD_MCU=1 PDN=CORAL-NODE-V2.1 ENABLE_DIVER_RUNTIME=1 -c`
  - `scons BUILD_MCU=1 PDN=CORAL-NODE-V2.1 ENABLE_DIVER_RUNTIME=1 -j 12 debug=1`
  - `scons BUILD_MCU=1 PDN=CORAL-NODE-V2.1 ENABLE_DIVER_RUNTIME=1 -j 12 debug=1 upg`
- 结果：
  - 清理成功
  - 固件重新编译成功
  - 新 UPG 生成成功：
    - `MCUSerialBridge/build/MCUSerialBridge_CORAL-NODE-V2.1_7b57695__20260608_191421.upg`
- 新 UPG 元数据：
  - `COMMIT = 7b57695`
  - `BUILD_TIME = 2026-06-08 19:14:21`
  - `LENGTH = 222560`
  - `ADDRESS = 0x00010000`
  - `CRC32 = 0x2E124A7F`
- 备注：
  - 链接阶段仍有 `_read/_write/_close/_isatty/_getpid/_kill/_lseek` 及 `RWX permissions` 的既有 warning，但未阻塞构建与打包。

## 2026-06-08 19:24 UTC+8 — 临时强制拉高 raw bit8..11

- 用户现场反馈：`OUT8` 和 `OUT11` 仍然不亮，要求做一个临时修改，用于验证是否接线偏到了中间级低 4 位。
- 在 `MCUSerialBridge/mcu/bsp/CORAL-NODE-V2.1/digital_io.c` 中追加临时调试逻辑：
  - `raw_outputs |= 0x00000F00u;`
- 含义：
  - 无论 `g_bsp_digital_outputs` 如何，`raw bit8..11` 固定为高。
  - 如果这样做后现场对应灯/输出亮了，说明中间级实际接线更可能落在 `D0..D3`，而不是之前假设的 `D4..D7`。
- `ReadLints` 检查 `digital_io.c` 无错误。
- 已执行清理、重编和重生 UPG：
  - `scons BUILD_MCU=1 PDN=CORAL-NODE-V2.1 ENABLE_DIVER_RUNTIME=1 -c`
  - `scons BUILD_MCU=1 PDN=CORAL-NODE-V2.1 ENABLE_DIVER_RUNTIME=1 -j 12 debug=1`
  - `scons BUILD_MCU=1 PDN=CORAL-NODE-V2.1 ENABLE_DIVER_RUNTIME=1 -j 12 debug=1 upg`
- 新调试 UPG：
  - `MCUSerialBridge/build/MCUSerialBridge_CORAL-NODE-V2.1_7b57695__20260608_192409.upg`
- 元数据：
  - `BUILD_TIME = 2026-06-08 19:24:09`
  - `LENGTH = 222560`
  - `ADDRESS = 0x00010000`
  - `CRC32 = 0x5C6CD1CB`

## 2026-06-08 19:50 UTC+8 — 新增 UpperIO DO 写 snapshot 最小示例

- 用户要求：在 `ai-deck/kit-docs` 写一个根据 UpperIO 的 DO(`u32`) 写到 snapshot 的最小示例代码。
- 检索确认：
  - `RunOnMCU.WriteSnapshot(byte[] payload)` 现有文档位于：
    - `3rd/CoralinkerKitDocs/02-logic-api.md`
    - `3rd/CoralinkerKitDocs/05-serial-and-can.md`
  - `snapshot` 约定为 4 字节，`bit0..bit31` 对应 `DO0..DO31`。
- 已执行：
  - 新增文件：
    - `ai-deck/kit-docs/UpperIODoToSnapshot_Minimal.cs`
  - 示例包含：
    - `[AsUpperIO] uint doValue`
    - MCU 逻辑中：
      - `byte[] snapshot = BitConverter.GetBytes(cart.doValue);`
      - `RunOnMCU.WriteSnapshot(snapshot);`
    - Root 逻辑中：
      - `[AsControlItem] uint commandDo`
      - `cart.doValue = commandDo;`
- 说明：
  - 这是最小可读示例，未额外引入 DI/状态回读等逻辑。

## 2026-06-08 19:38 UTC+8 — 每字节反位序修正

- 用户现场确认真实原因：
  - 74HC595 为 `D0 = QA`, `D7 = QH`
  - `SPI2` 配置 `lsb_first=1`
  - 因此每个字节先发出的 `bit0` 会被移到 `QH`
  - 结论：每个发送字节内部的位序需要反过来
- 代码修正：
  - 在 `MCUSerialBridge/mcu/bsp/CORAL-NODE-V2.1/digital_io.c` 新增 `bsp_reverse_u8()`
  - `bsp_encode_outputs()` 改为：
    - 先按接线关系组出 3 个逻辑字节
    - `byte0: D0..D7 <- OUT0..OUT7`
    - `byte1: D4..D7 <- OUT8..OUT11`
    - `byte2: D0..D7 <- OUT12..OUT19`
    - 再对 3 个字节分别做 bit reverse 后拼成最终 `output_raw`
  - 删除上一轮临时调试代码 `raw_outputs |= 0x00000F00u;`
- 文档同步：
  - 更新 `MCUSerialBridge/mcu/bsp/CORAL-NODE-V2.1/sch.md`，补充“每字节 bit reverse”说明
- 校验：
  - `ReadLints` 检查 `digital_io.c` 无错误
  - `scons BUILD_MCU=1 PDN=CORAL-NODE-V2.1 ENABLE_DIVER_RUNTIME=1 -j 12 debug=1` 编译成功
  - 固件尺寸：
    - `text=220452`
    - `data=2204`
    - `bss=114188`

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

### [summary] 2026 年 5 月 git 历史梳理
- 用户要求查看 git 历史，概括 5 月主要改动，不需要细节。
- 查询：
  - `git log --since="2026-05-01" --until="2026-05-31 23:59:59" --date=short --pretty=format:"%h %ad %s" --name-only`
  - `git log --since="2026-05-01" --until="2026-05-31 23:59:59" --date=short --pretty=format:"%h%x09%ad%x09%s" --shortstat`
- 结果：
  - `28eee6a`：MCUSerialBridge 构建流程、native DLL 拷贝、线程/停止稳定性，以及 CORAL-NODE-V2.1 BSP 相关改动。
  - `b42a401`：CoralinkerHost 输入源文件 GitHistory、History UI、diff/build 版本信息等。
  - `d02be69`：遥控器窗口边界限制。
  - `612697a`：Root Logic 集成、DIVERSession 虚拟节点/变量元数据、Root 日志分栏、文件/Git/导入导出体验等综合增强。

### [plan] ARM Linux 发行迁移计划修正
- 用户提出 ARM Linux 平台迁移需求，优先顺序是先解决发行，再解决跨平台。
- 初版计划误解了“ARM 上不 Build”的含义，写成 ARM 包只运行已有 generated artifacts。
- 用户澄清：
  - ARM 上不作为开发机，不在 ARM 上做源码开发。
  - 但部署后的 ARM 包必须具备 Build 能力，可以通过 Host/UI/API 构建用户逻辑。
- 已修正计划文件：
  - `c:\Users\lvzhe\.cursor\plans\arm_linux_发行迁移_24982563.plan.md`
- 修正要点：
  - ARM 端 framework-dependent 包需要依赖 .NET 8 SDK，而不仅是 runtime。
  - 发布包默认是可运行且可 Build 的 `standard` 包。
  - 包内需要发布 `res/compiler/`，包含 `DiverCompiler.exe`、`RunOnMCU.cs`、`DIVERInterface.cs`、`DIVERCommonUtils.cs`、`Extensions.cs` 等编译资源。
  - `DiverBuildService` 后续要从发布包资源目录读取编译资源，不再从源码仓库 `../../DiverTest` 推导。
  - `with-history` 模式额外携带 `data/.git`，用于保留 History/diff/checkout 能力。
- 备注：
  - 本轮仍在 Plan mode，仅修改 markdown 计划和 `ai-deck` 记录，没有进入代码实现。

### [plan] ARM Linux 发行迁移计划补充双运行布局
- 用户补充要求：
  - 必须同时支持开发环境下直接运行和打包以后的运行。
  - 不能为了发布包把当前源码仓库调试/开发流程破坏掉。
- 已更新计划文件：
  - `c:\Users\lvzhe\.cursor\plans\arm_linux_发行迁移_24982563.plan.md`
- 新增设计：
  - 引入 `RunLayout`：`Development` / `Published`。
  - `Development`：从源码仓库运行，继续支持当前 Debug/Release、本地 Build、前端调试。
  - `Published`：从 publish 输出目录运行，不依赖源码仓库相对路径，但仍具备 Build 能力。
  - 编译资源查找优先级：
    1. 环境变量 `CORALINKER_COMPILER_RES_DIR`。
    2. 发布包内 `ContentRoot/res/compiler`。
    3. 开发环境 fallback：`ContentRoot/../../DiverTest`。
  - 数据目录允许 `CORALINKER_DATA_DIR` 覆盖；默认仍可使用 ContentRoot 下的 `data`。
  - 后续实现时 `DiverBuildService` 只依赖统一路径层，避免继续散落 `../../DiverTest` 之类推导。
- 验证要求补充：
  - Windows 源码仓库原地运行。
  - Windows publish 目录运行。
  - linux-arm64 framework-dependent 发布包运行并执行 Build。

### [plan] ARM Linux 迁移计划补充 README 分层
- 用户要求审视迁移计划，明确文档落地：
  - 运行目录和发布相关改动写到 Host README。
  - 平台相关改动写到对应 README。
- 已更新计划文件：
  - `c:\Users\lvzhe\.cursor\plans\arm_linux_发行迁移_24982563.plan.md`
- 文档分层：
  - `3rd/CoralinkerHost/README.md`：
    - 新增“运行布局与发布”。
    - 说明 `Development` / `Published`、`ContentRoot`、`DataDir`、`CompilerResourcesDir`、`RuntimeAssembliesDir`。
    - 说明 `CORALINKER_DATA_DIR`、`CORALINKER_COMPILER_RES_DIR`。
    - 说明 linux-arm64 framework-dependent 发布命令、ARM 端 .NET 8 SDK 要求、`standard` / `with-history` 包内容。
  - `3rd/CoralinkerSDK/README.md`：
    - 新增“平台抽象与部署边界”。
    - 说明 `ISerialPortDiscovery`、Windows WMI/Registry、Linux `/dev/serial/by-id`、native library 名称解析。
    - 说明 Host/Medulla/CLI 如何复用统一平台接口。
  - `MCUSerialBridge/README.md`：
    - 新增“跨平台 native bridge”。
    - 说明当前 Win32 串口/线程依赖、`serial_transport_win32/posix`、`os_sync_win32/posix`。
    - 说明 Windows `mcu_serial_bridge.dll` 与 Linux ARM64 `libmcu_serial_bridge.so` 产物。
    - 说明 Linux 串口权限、udev/dialout、交叉编译工具链要求。
- 验证要求补充：
  - README 中发布命令、环境变量、目录结构、平台边界必须与最终实现一致。

### [implementation] DiverCompilerPortable 工程拆分
- 用户要求：
  - 原 `DiverCompiler` 工程必须继续保留给 Windows/VS 调试，不再迁移或修改其 `.csproj`。
  - 新建同级 `DiverCompilerPortable`，引用原工程内部 C# 源文件，仅新增工程配置。
  - Host 使用 `DiverCompilerPortable` 产物作为可发布/跨平台编译器资源。
- 实施：
  - 使用 `git checkout -- DiverCompiler/DiverCompiler.csproj` 撤销此前对原工程文件的迁移改动，恢复原 `net48` VS 工程。
  - 新增 `DiverCompilerPortable/DiverCompilerPortable.csproj`：
    - `TargetFramework=netstandard2.0`。
    - `AssemblyName=DiverCompiler`，保证 Fody weaver 名称仍为 `DiverCompiler.dll`。
    - `EnableDefaultCompileItems=false`，显式链接原 `DiverCompiler` 下的 `ModuleWeaver.cs`、`Processor.cs`、`Processor.Builtin.cs`、`Processor.StringInterpolationHandler.cs`、`Program.cs`。
    - 复制 `DiverTest` 中 Host build 所需的 `RunOnMCU.cs`、`DIVERInterface.cs`、`DIVERCommonUtils.cs`、`Extensions.cs`、`extra_methods.txt` 到输出目录。
    - 复制 `DiverCompiler/native/**/*` 到输出目录。
    - 早期曾加入构建后同步输出到 `DiverTest`，但该目标会把内容资源也复制过去并覆盖 `DiverTest/native` 文件，随后已删除。
  - 修改 `3rd/CoralinkerHost/CoralinkerHost.csproj`：
    - 不使用普通 `ProjectReference` 引用 Portable，避免发布时把 Portable 输出默认复制到发布根目录。
    - 新增 `BuildDiverCompilerPortable` MSBuild target，在 Host build/publish 前对 Portable 工程执行 `Clean;Build`，避免输出目录残留旧文件进入发布包。
    - 新增 `CollectDiverCompilerPortablePublishResources` target，将 Portable 输出映射到发布包 `res/compiler/`。
    - 第一次 publish 发现 `NETSDK1152` 重复发布项，原因是直接在 `ResolvedFileToPublish` 上使用未限定 metadata 导致重复；改为先收集到 `DiverCompilerPortablePublishFile`，再映射到 `ResolvedFileToPublish`。
  - 修改 `HostRuntimePaths`：
    - Development 下优先查找 `DiverCompilerPortable/bin/Debug/netstandard2.0` 和 `bin/Release/netstandard2.0`。
    - 再 fallback 到 `DiverTest`。
  - 清理副作用：
    - 还原 `DiverTest/native/compile_native_binary.py`。
    - 删除误复制到 `DiverTest` 根目录的 `DIVERCommonUtils.cs`、`DIVERInterface.cs`。
    - 用户复查发现 `DiverTest` 下仍有 untracked 生成物；已删除 `DiverCompiler.deps.json`、`DiverCompiler.dll.config` 和 `native/arm_ram_overlay.ld`、`native/dll_test.cpp`、`native/testcpp.*`。
- 验证：
  - `dotnet build DiverCompilerPortable\DiverCompilerPortable.csproj -c Debug`：成功，1 warning，0 error。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release --no-restore`：成功，既有 warnings，0 error。
  - `dotnet publish 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release -o ai-deck\publish-portable-check --no-restore`：成功，确认发布目录 `res/compiler/DiverCompiler.dll` 存在。
  - `git status --short -- DiverTest`：无输出，确认 `DiverTest` 已恢复干净。

### [implementation] Host Publish 专用目录和忽略规则修正
- 用户要求：
  - 给 Host 加一个 Publish 专用文件夹。
  - publish 时目录名最好带 commit 号和时间。
  - 后续明确指出：不要把 publish 目录放进 git 提交范围；根 `.gitignore` 已有 `publish/` 通用规则，不应额外放行。
- 实施：
  - 新增 `3rd/CoralinkerHost/publish-host.ps1`。
    - 默认 `Release` 发布。
    - 输出目录为 `3rd/CoralinkerHost/Publish/CoralinkerHost_<commit>_<yyyyMMdd-HHmmss>/`。
    - 写入 `publish-info.json`，包含 app、configuration、runtime、commit、commitTime、dirty、publishTime、outputDirectory。
  - 曾短暂添加 `/3rd/CoralinkerHost/Publish/` 和 `/3rd/CoralinkerHost/Publish/.gitignore` 放行规则，用户指出这是误解；已撤销。
  - 已删除 `3rd/CoralinkerHost/Publish/.gitignore`，让根 `.gitignore` 的 `publish/` 通用规则直接忽略整个发布目录。
- 验证：
  - `powershell -NoProfile -ExecutionPolicy Bypass -File 3rd\CoralinkerHost\publish-host.ps1 -NoRestore` 成功。
  - 本次输出目录：`3rd/CoralinkerHost/Publish/CoralinkerHost_612697a_20260525-223825/`。
  - `publish-info.json` 已写入 commit `612697a`、commit 时间 `2026-05-17T14:37:29+08:00`、publish 时间 `2026-05-25T22:38:25.4813643+08:00`。
  - `git check-ignore -v 3rd/CoralinkerHost/Publish/CoralinkerHost_612697a_20260525-223825/publish-info.json` 命中 `.gitignore:187:publish/`，确认发布产物不进入 git。

### [verification] Host 发布目录启动测试
- 用户要求：
  - 测试发布出来的文件能不能启动，并说明启动位置。
- 测试目录：
  - `3rd/CoralinkerHost/Publish/CoralinkerHost_612697a_20260525-223825/`
- 启动命令：
  - 在上述目录执行 `.\CoralinkerHost.exe`。
  - 曾尝试设置 `ASPNETCORE_URLS=http://127.0.0.1:5099`，但当前 `Program.cs` 里硬编码 `builder.WebHost.UseUrls("http://0.0.0.0:4499")`，所以实际监听端口仍是 `4499`。
- 结果：
  - 进程启动后输出 `Runtime paths: Layout=Published`。
  - `ContentRoot`、`DataDir`、`CompilerResources` 均指向发布目录。
  - `netstat -ano | findstr 4499` 显示发布目录下的 `CoralinkerHost.exe` 正在监听 `0.0.0.0:4499`。
  - `curl.exe -I --max-time 10 http://127.0.0.1:4499/` 返回 `HTTP/1.1 200 OK`，`Content-Type: text/html`。
  - `Get-Process -Id 16984` 确认监听进程路径为 `D:\Documents\Coral\DIVER\3rd\CoralinkerHost\Publish\CoralinkerHost_612697a_20260525-223825\CoralinkerHost.exe`。
- 清理：
  - 测试结束后执行 `taskkill /PID 16984 /T /F` 关闭发布 Host。
  - 后台 shell 的 exit code 1 来自强制结束进程，不代表启动失败。

### [implementation] 移除过时 Coralinker_arch 工程
- 用户说明：
  - `Coralinker_arch` 代码非常过时且没有适配，构建不了是正常的。
  - 用户已删除 `3rd/Coralinker_arch` 目录，需要删除其余工程文件/解决方案引用。
- 相关排查：
  - VS 报错中 `Coralinker_arch` 的 `System.Management`、`System.IO.Ports`、`DIVERVehicle` 缺失属于该过时工程自身问题，不再修复。
  - 另外 Host 报大量重复定义的根因是 `3rd/CoralinkerHost/Publish/**` 被 SDK-style 默认纳入编译；已在 `CoralinkerHost.csproj` 中排除 `Publish/**`，并验证 Host Release 构建通过。
- 实施：
  - 从 `DIVER.sln` 删除 `Coralinker_arch` 项目项：
    - 项目 GUID `{FEBBD16F-0950-4125-95F9-B02C85DA9737}`。
    - 路径 `3rd\Coralinker_arch\Coralinker_arch.csproj`。
  - 从 `ProjectConfigurationPlatforms` 删除该 GUID 的 Debug/Release、Any CPU/x64/x86 配置。
  - 从 `NestedProjects` 删除该 GUID 到 `3rd-party` solution folder 的挂载。
  - `3rd/Coralinker_arch` 下 tracked 文件当前均为 deleted，符合用户删除工程目录的意图。
- 验证：
  - `dotnet sln DIVER.sln list` 输出不再包含 `3rd\Coralinker_arch\Coralinker_arch.csproj`。
  - `rg "Coralinker_arch|FEBBD16F-0950-4125-95F9-B02C85DA9737"` 只剩 `DiverTest/ChangeLogForDIVERCommonUtils.md` 的历史说明文本。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release --no-restore` 成功，0 error。

### [fix] DiverTest 调用 net48 DiverCompiler 时继承 TargetFramework
- 用户反馈：
  - 移除 `Coralinker_arch` 后，VS 重新生成仍剩 1 个失败。
  - 失败点在 `DiverTest` 的 `RebuildDiverCompiler` target 调用旧 `DiverCompiler.csproj`：
    `Your project does not reference ".NETFramework,Version=v4.8" framework.`
- 根因：
  - `DiverTest` 是 SDK-style `net8.0` 项目。
  - `DiverTest.csproj` 里的 `<MSBuild Projects="..\DiverCompiler\DiverCompiler.csproj" Targets="Rebuild" />` 会把父项目全局属性传给被调用项目。
  - 旧 `DiverCompiler` 是非 SDK-style `net48` 工程，继承 `TargetFramework=net8.0` 后 NuGet/MSBuild framework 判断错乱。
- 修复：
  - 修改 `DiverTest/DiverTest.csproj` 的 `RebuildDiverCompiler` target：
    - `Properties="Configuration=$(Configuration);Platform=AnyCPU"`。
    - `RemoveProperties="TargetFramework;TargetFrameworks;RuntimeIdentifier;RuntimeIdentifiers;SelfContained"`。
  - 保持 `DiverCompiler/DiverCompiler.csproj` 不变，仍可供 Windows/VS 调试。
- 验证：
  - `dotnet build DiverTest\DiverTest.csproj -c Release --no-restore` 成功。
  - 输出显示旧 `DiverCompiler` 正常构建为 `DiverCompiler\bin\Release\DiverCompiler.exe`，随后 `DiverTest` 生成成功。
  - `git status --short -- DiverTest DiverCompiler` 仅显示 `M DiverTest/DiverTest.csproj`，没有额外生成物进入 git 状态。

### [correction] 保留 DiverCompiler 在主解决方案中
- 用户反馈：
  - 不同意把 `DiverCompiler` 从主 `DIVER.sln` 移除。
  - 需要保留同事在 VS 主解决方案里看到/调试旧 `DiverCompiler` 工程的工作流。
- 纠正：
  - 撤回“从主 sln 移除 `DiverCompiler`”的做法。
  - 已把 `DiverCompiler` 项目项、solution 配置映射和 NestedProjects 挂载加回 `DIVER.sln`。
  - `Coralinker_arch` 仍保持移除。
  - 根本修复仍保留在 `DiverTest/DiverTest.csproj`：间接调用旧 `DiverCompiler.csproj` 时清掉父项目继承的 `TargetFramework`/Runtime 属性。
- 验证：
  - `dotnet sln DIVER.sln list`：包含 `DiverCompiler\DiverCompiler.csproj`，不包含 `Coralinker_arch`。
  - `dotnet build DiverCompiler\DiverCompiler.csproj -c Release --no-restore`：成功，0 error。
  - `dotnet restore DIVER.sln`：成功，未再出现 `.NETFramework v4.8` 错误。
  - `dotnet build DIVER.sln -c Release --no-restore`：成功，0 error。

### [fix] VS MSBuild 下 DiverCompiler packages.config 误走 NuGet assets
- 用户反馈：
  - VS 输出显示 `DiverCompiler` 在 `Release|Any CPU` 下被跳过生成，但随后 `DiverCompiler.csproj` 又报：
    `Your project does not reference ".NETFramework,Version=v4.8" framework.`
  - 用户要求严查是否存在两个同名项目及引用关系。
- 严查结果：
  - `DIVER.sln` 中只有一个 `DiverCompiler` 项目：
    `DiverCompiler\DiverCompiler.csproj`，GUID `{F9E5081B-0F60-4C0C-9794-B2EB9E6947E1}`。
  - `DiverCompilerPortable` 不在 `DIVER.sln` 中，项目名不同；只是 `AssemblyName=DiverCompiler`，供 Host 发布和跨平台 weaver 使用。
  - 直接引用/调用关系：
    - `DIVER.sln` 包含旧 `DiverCompiler`。
    - `DiverTest/DiverTest.csproj` 的 `RebuildDiverCompiler` target 会调用旧 `DiverCompiler.csproj`。
    - `3rd/CoralinkerHost/CoralinkerHost.csproj` 调用 `DiverCompilerPortable`，不调用旧 `DiverCompiler.csproj`。
- 根因：
  - `DIVER.sln` 中 `DiverCompiler` 原本缺少 `Debug|Any CPU.Build.0` 和 `Release|Any CPU.Build.0`，所以 VS solution build 显示“已跳过生成”。
  - 旧 `DiverCompiler` 是 `packages.config` + explicit `HintPath` 的 net48 工程，但 VS MSBuild 默认进入 `Microsoft.NuGet.targets` 的 `ResolveNuGetPackageAssets`，该逻辑面向 `project.assets.json`/PackageReference，导致 framework 判断错乱。
- 修复：
  - `DIVER.sln`：给 `DiverCompiler` 补齐：
    - `Debug|Any CPU.Build.0 = Debug|Any CPU`
    - `Release|Any CPU.Build.0 = Release|Any CPU`
  - `Directory.Build.props`：仅对 `MSBuildProjectName == DiverCompiler` 设置：
    - `ResolveNuGetPackages=false`
  - `DiverTest/DiverTest.csproj`：调用旧 `DiverCompiler.csproj` 时显式传：
    - `ResolveNuGetPackages=false`
    - 继续 `RemoveProperties="TargetFramework;TargetFrameworks;RuntimeIdentifier;RuntimeIdentifiers;SelfContained"`
  - 未修改 `DiverCompiler/DiverCompiler.csproj`。
- 验证：
  - `MSBuild.exe DiverCompiler\DiverCompiler.csproj /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU /p:ResolveNuGetPackages=false /v:minimal` 成功。
  - `MSBuild.exe DIVER.sln /t:Rebuild /p:Configuration=Release /p:Platform="Any CPU" /m:1 /v:minimal` 成功，0 error。

### [docs] Host README 补充发布打包说明
- 用户反馈：
  - 发布脚本和发布目录结构没有写入 README。
  - 如果后续上下文丢失，就无法知道如何发布打包。
- 实施：
  - 更新 `3rd/CoralinkerHost/README.md`，新增“发布与运行”章节。
  - 文档覆盖：
    - `Development` / `Published` 两种运行布局。
    - `publish-host.ps1` 基本命令。
    - `-NoRestore` 用法。
    - `-Runtime linux-arm64` 用法。
    - 输出目录格式：`3rd/CoralinkerHost/Publish/CoralinkerHost_<commit>_<yyyyMMdd-HHmmss>/`。
    - `publish-info.json` 字段：commit、commitTime、dirty、publishTime、configuration、runtime、outputDirectory。
    - 发布包关键内容：Host 主程序、`wwwroot/`、`res/compiler/`、native bridge、`publish-info.json`。
    - `res/compiler/` 来源于 `DiverCompilerPortable`，原 `DiverCompiler` 保留给 Windows/VS 调试。
    - 启动命令和默认地址 `http://127.0.0.1:4499/`。
    - 当前 `Program.cs` 固定 `UseUrls("http://0.0.0.0:4499")`，`ASPNETCORE_URLS` 暂不能覆盖端口。
    - 首次运行生成 `data/`，可用 `CORALINKER_DATA_DIR` 覆盖。
    - 可用 `CORALINKER_COMPILER_RES_DIR` 覆盖 compiler resource。
    - `Publish/` 被根 `.gitignore` 的 `publish/` 规则忽略，不应提交发布产物。
    - `CoralinkerHost.csproj` 已排除 `Publish/**`，避免发布包里的 `.cs` 被编进 Host。
- 验证：
  - 文档更新，无代码构建验证。

### [docs] Host README 目录结构补充 Publish
- 用户反馈：
  - `3rd/CoralinkerHost/README.md` 的“目录结构”树没有体现 `Publish/`。
- 实施：
  - 在目录结构树中新增：
    - `Publish/`
    - `CoralinkerHost_<commit>_<yyyyMMdd-HHmmss>/`
    - `CoralinkerHost.exe`
    - `publish-info.json`
    - `wwwroot/`
    - `res/compiler/`
    - `data/`
    - `publish-host.ps1`
  - 保持与“发布与运行”章节中的发布包内容说明一致。

### [publish] CoralinkerHost 发布包内容收敛与 PDB 开关
- 用户反馈：
  - 旧发布目录 `3rd/CoralinkerHost/Publish/CoralinkerHost_612697a_20260525-223825` 中出现 `ClientApp/`，但其中只有 `package*.json` 与 `tsconfig*.json`，不是运行时需要内容。
  - 发布包默认不需要 `.pdb` 调试符号，希望加开关控制是否保留。
- 判断：
  - Host 运行时需要的是 `wwwroot/`，不是 `ClientApp/` 前端源目录。
  - 发布包首次运行后才会创建 `data/`，发布脚本不应预置运行时工作区。
  - `CoralinkerSDK` 当前是 `OutputType=Exe`，Host 引用它时 `.NET publish` 会带出 SDK 独立程序 sidecar；Host 包只需要 `CoralinkerSDK.dll`。
- 实施：
  - `3rd/CoralinkerHost/CoralinkerHost.csproj`：新增排除 `ClientApp/**` 的 `Compile/None/Content/EmbeddedResource Remove`。
  - `3rd/CoralinkerHost/publish-host.ps1`：
    - 新增 `[switch]$IncludePdb`。
    - 默认增加 publish 参数 `-p:DebugType=None -p:DebugSymbols=false`。
    - 发布后删除 `ClientApp/`。
    - 默认删除所有 `.pdb`。
    - 删除 Host 包不需要的 `CoralinkerSDK.exe`、`CoralinkerSDK`、`CoralinkerSDK.deps.json`、`CoralinkerSDK.runtimeconfig.json`。
    - `publish-info.json` 增加 `includePdb`。
  - `3rd/CoralinkerHost/README.md`：
    - 记录默认不保留 PDB。
    - 记录 `-IncludePdb` 用法。
    - 记录发布包不应包含 `ClientApp/`、`package*.json`、`tsconfig*.json`、初始 `data/`。
    - 记录 SDK sidecar 清理逻辑。
- 验证：
  - 执行 `powershell -NoProfile -ExecutionPolicy Bypass -File 3rd\CoralinkerHost\publish-host.ps1 -NoRestore` 成功，生成 `3rd/CoralinkerHost/Publish/CoralinkerHost_df02128_20260526-103436/`。
  - 新包顶层包含：`CoralinkerHost.exe/.dll/.deps.json/.runtimeconfig.json`、`CoralinkerSDK.dll`、`wwwroot/`、`res/`、`runtimes/`、native bridge、依赖 DLL、`publish-info.json`、`web.config`。
  - 验证 `ClientApp` 不存在、`data` 不存在、`CoralinkerSDK.exe` 不存在。
  - `dir /s /b ...\*.pdb` 无输出，默认包未包含 PDB。
  - 发布后一次 `Get-ChildItem -Recurse` 验证命令在 PowerShell 中长时间未返回，被用户中断；后续改用窄范围 `cmd /c if exist` 与 `dir` 验证完成。发布流程本身正常。

### [publish] 修正 web.config/static web assets 清理策略
- 用户反馈：
  - 询问 `CoralinkerHost.staticwebassets.endpoints.json`、`web.config` 是否必要，以及为什么 Host 同时有 `.exe` 和 `.dll`。
  - 用户指出只是询问必要性，不应直接决定默认删除；未来可能走 IIS。
- 修正判断：
  - `web.config` 是 IIS / ASP.NET Core Module 部署所需文件；Kestrel 直接运行 `CoralinkerHost.exe` 或 `dotnet CoralinkerHost.dll` 时不需要。
  - `CoralinkerHost.staticwebassets.endpoints.json` 是 ASP.NET Core static web assets 端点清单；当前 Host 代码使用 `UseDefaultFiles()` + `UseStaticFiles()` 从 `wwwroot/` 提供静态文件，没有调用 `MapStaticAssets()`，直跑模式通常不依赖该文件，但默认保留更稳妥。
  - `CoralinkerHost.exe` 是 Windows apphost 启动器；`CoralinkerHost.dll` 是托管程序集本体。Windows 可直接运行 exe，也可 `dotnet CoralinkerHost.dll`；跨平台包通常依赖 dll 或对应 RID 的 apphost。
- 实施：
  - `3rd/CoralinkerHost/publish-host.ps1`：
    - 默认不再删除 `web.config`。
    - 默认不再删除 `CoralinkerHost.staticwebassets.endpoints.json`。
    - 新增 `-ExcludeIisConfig`：显式精简直跑包时删除 `web.config`。
    - 新增 `-ExcludeStaticWebAssetsEndpoints`：显式精简直跑包时删除 static web assets endpoints 清单。
    - 新增 `-IncludeSdkExecutable`：需要 SDK 独立 CLI 时保留 `CoralinkerSDK.exe` 等 sidecar。
    - `publish-info.json` 增加 `includeSdkExecutable`、`excludeIisConfig`、`excludeStaticWebAssetsEndpoints`。
  - `3rd/CoralinkerHost/README.md`：
    - 改写为部署模式说明，不再说脚本默认清理 IIS/static assets 文件。
    - 补充 `-ExcludeIisConfig`、`-ExcludeStaticWebAssetsEndpoints`、`-IncludeSdkExecutable` 用法。
- 验证：
  - 默认发布成功：`3rd/CoralinkerHost/Publish/CoralinkerHost_df02128_20260526-110536/`。
  - 默认包验证：`web.config` 存在，`CoralinkerHost.staticwebassets.endpoints.json` 存在，`CoralinkerHost.exe` 存在，`CoralinkerHost.dll` 存在。

### [publish] 调整为三平台统一 portable 发布包并准备 SSH 部署
- 用户要求：
  - 回忆 `c:\Users\lvzhe\.cursor\plans\arm_linux_发行迁移_24982563.plan.md` 中的 ARM Linux 发行迁移计划。
  - 准备发布到 `industio@192.168.0.116`。
  - 不希望按 Windows/Linux/ARM 分多个版本；希望一个发布包兼容 Windows x64、Linux x64、Linux ARM64。
  - 本轮先不处理 native bridge，只保证 Host 本体和 Compiler 能在三个平台运行。
- 决策：
  - 最终发布形态改为无 RID 的 framework-dependent portable 包。
  - 默认不传 `-Runtime`；`-Runtime linux-arm64` 仅作为可选 RID 专用包，不作为当前发行方式。
  - 三平台统一启动入口为 `dotnet CoralinkerHost.dll`。
  - Windows 包中的 `CoralinkerHost.exe` 只是 Windows apphost 便捷入口，不作为跨平台依赖。
  - 目标机需要 .NET 8 SDK，因为部署后 Build 用户逻辑需要执行 `dotnet restore/build`，并由 Fody 加载 `res/compiler/DiverCompiler.dll`。
  - `res/compiler/DiverCompiler.dll` 来自 `DiverCompilerPortable`，目标为 `netstandard2.0`，作为 Fody weaver 由目标机 .NET SDK 加载，适用于 Windows x64、Linux x64、Linux ARM64。
- 实施：
  - `3rd/CoralinkerHost/CoralinkerHost.csproj`：`BuildDiverCompilerPortable` 的 MSBuild 调用增加：
    - `RemoveProperties="TargetFramework;TargetFrameworks;RuntimeIdentifier;RuntimeIdentifiers;SelfContained;PublishSingleFile;PublishTrimmed"`
  - 原因：尝试 `-Runtime linux-arm64` 时，Host 的 `RuntimeIdentifier` 污染了 `DiverCompilerPortable` 构建，导致其查找 `netstandard2.0/linux-arm64` assets。隔离属性后可避免类似问题。
  - `3rd/CoralinkerHost/README.md`：
    - 补充默认无 RID portable 包才是当前三平台统一发行方式。
    - 补充目标机需要 .NET 8 SDK。
    - 补充 `dotnet CoralinkerHost.dll` 跨平台启动方式。
    - 补充 `DiverCompiler.dll` 是 `netstandard2.0` Fody weaver，不是 Windows-only exe。
- 验证：
  - 第一次执行 `publish-host.ps1 -Runtime linux-arm64` 失败，错误为 `DiverCompilerPortable` assets 缺少 `netstandard2.0/linux-arm64`。
  - 修正 MSBuild `RemoveProperties` 后，`publish-host.ps1 -Runtime linux-arm64` 成功，但该包不是最终目标。
  - 执行默认 portable 发布成功：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_df02128_20260526-111558/`
  - `publish-info.json` 中 `runtime: null`。
  - 验证包内存在：
    - `CoralinkerHost.dll`
    - `CoralinkerHost.runtimeconfig.json`
    - `res/compiler/DiverCompiler.dll`
    - `res/compiler/RunOnMCU.cs`
  - 已压缩：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_df02128_20260526-111558.tar.gz`
    - 大小约 8.85 MB。
- SSH/网络状态：
  - 本机有 `ssh.exe`、`scp.exe`、`tar.exe`。
  - `ssh -o BatchMode=yes -o ConnectTimeout=8 industio@192.168.0.116 ...` 连接超时。
  - `ping -n 2 192.168.0.116`：100% 丢包。
  - `Test-NetConnection -ComputerName 192.168.0.116 -Port 22 -InformationLevel Quiet`：`False`。
  - 当前判断：不是密码/认证问题，而是网络或目标 SSH 服务不可达；暂未上传。

### [publish] 发布包新增启动脚本和 SHA256 完整性校验
- 用户要求：
  - 包内加启动命令。
  - Windows 需要 PowerShell 和 BAT。
  - Linux 需要 SH。
  - 启动命令需检查 .NET 环境和 SDK、所需文件、包体完整性，然后启动服务器。
- 实施：
  - `3rd/CoralinkerHost/publish-host.ps1` 发布后生成：
    - `start-host.ps1`
    - `start-host.bat`
    - `start-host.sh`
    - `package-manifest.sha256`
  - `start-host.ps1`：
    - 检查 `dotnet` 命令。
    - 检查 .NET SDK 8 或更高版本。
    - 检查 `Microsoft.NETCore.App 8.x` 和 `Microsoft.AspNetCore.App 8.x` runtime。
    - 检查 `CoralinkerHost.dll`、`CoralinkerHost.deps.json`、`CoralinkerHost.runtimeconfig.json`、`publish-info.json`、`wwwroot/`、`res/compiler/`、`DiverCompiler.dll`、`RunOnMCU.cs`、`DIVERInterface.cs`、`DIVERCommonUtils.cs`、`Extensions.cs`。
    - 读取 `package-manifest.sha256` 并用 `Get-FileHash` 校验包体 SHA256。
    - 支持 `-CheckOnly`，只检查不启动。
  - `start-host.bat`：
    - 代理调用 `start-host.ps1`，方便 Windows CMD/双击入口。
  - `start-host.sh`：
    - 检查 `dotnet`、SDK 8+、.NET 8 runtime、ASP.NET Core 8 runtime。
    - 检查同样的必需文件和目录。
    - 使用 `sha256sum -c package-manifest.sha256` 校验包体。
    - 支持 `--check-only`，只检查不启动。
  - `package-manifest.sha256`：
    - 发布脚本对包内所有文件生成 SHA256，排除清单文件自身。
    - 使用 Linux `sha256sum -c` 兼容格式：`<hash> *<relative/path>`。
  - `publish-info.json`：
    - 新增 `startScripts` 和 `integrityManifest` 字段。
  - `3rd/CoralinkerHost/README.md`：
    - 补充启动脚本、检查项、`CheckOnly` 用法和完整性清单说明。
- 修正：
  - 第一次生成清单失败，原因是 Windows PowerShell 5.1 不支持 `[System.IO.Path]::GetRelativePath`。
  - 改为基于输出根目录字符串前缀计算相对路径，兼容 Windows PowerShell 5.1。
  - 第一次 `start-host.ps1 -CheckOnly` 失败，原因是检查逻辑要求 SDK 必须为 8.x，而本机 SDK 是 9.0.309。
  - 改为：Host 运行必须有 .NET 8 runtime / ASP.NET Core 8 runtime；Build 能力需要 SDK 8 或更高版本。
- 验证：
  - 发布成功：`3rd/CoralinkerHost/Publish/CoralinkerHost_df02128_20260526-112335/`。
  - 包内存在 `start-host.ps1`、`start-host.bat`、`start-host.sh`、`package-manifest.sha256`。
  - `publish-info.json` 已记录启动脚本和完整性清单。
  - 执行：
    - `powershell -NoProfile -ExecutionPolicy Bypass -File 3rd\CoralinkerHost\Publish\CoralinkerHost_df02128_20260526-112335\start-host.ps1 -CheckOnly`
  - 结果成功：
    - `Checking .NET runtime and SDK...`
    - `Checking required package files...`
    - `Checking package integrity...`
    - `Startup checks passed.`
  - 已压缩：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_df02128_20260526-112335.tar.gz`
    - 大小约 8.87 MB。

### [publish] 重新生成最新 portable 包
- 用户要求：
  - 用户已删除原来的 `3rd/CoralinkerHost/Publish/` 内容。
  - 需要直接重新打一个最新发布包。
- 实施：
  - 执行默认 portable 发布：
    - `powershell -NoProfile -ExecutionPolicy Bypass -File 3rd\CoralinkerHost\publish-host.ps1 -NoRestore`
  - 生成发布目录：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_df02128_20260526-113039/`
  - 压缩为：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_df02128_20260526-113039.tar.gz`
- 验证：
  - `publish-info.json` 中 `runtime: null`，确认仍是无 RID 三平台 portable 包。
  - `start-host.sh` 包含 Linux root 检查：
    - `CURRENT_UID=$(id -u 2>/dev/null || echo "")`
    - 非 `0` 会提示 `Linux startup must run as root. Re-run with sudo or root user.`
  - 执行 `start-host.ps1 -CheckOnly` 成功：
    - `Checking .NET runtime and SDK...`
    - `Checking required package files...`
    - `Checking package integrity...`
    - `Startup checks passed.`
  - 压缩包大小：`8,869,850` bytes。

### [deploy] 目标机缺少 dotnet SDK
- 用户反馈：
  - 目标机运行发布包启动检查时提示：
    - `dotnet command was not found. Install .NET 8 SDK.`
- 判断：
  - 当前 portable 包是 framework-dependent 包，不自带 .NET runtime/SDK。
  - Host 运行需要 .NET 8 runtime / ASP.NET Core 8 runtime。
  - 部署后 Build 用户逻辑需要 .NET SDK 8+，因为目标机会执行 `dotnet restore/build` 并加载 `res/compiler/DiverCompiler.dll`。
- 建议：
  - 在目标机上安装 `.NET 8 SDK`。
  - 目标机此前 SSH banner 显示 `OpenSSH_8.9p1 Ubuntu-3ubuntu0.12`，大概率是 Ubuntu 22.04，可使用 Microsoft apt 源安装 `dotnet-sdk-8.0`。
  - 安装后重新运行：
    - `sudo ./start-host.sh --check-only`

### [publish] 整理 packaging 脚本并增强 .NET 安装提示
- 用户要求：
  - 包内加入 `install-dotnet-sdk-ubuntu.sh`。
  - 打包相关文件在开发库中放到合理位置。
  - 安装脚本应根据当前 Ubuntu 版本下载对应资源。
  - 启动检查不过时，应手动提示用户安装 .NET SDK，并说明 Ubuntu / 其他 Linux 的处理方式。
  - 解释为什么需要版本 8，以及版本 9 是否可以。
- 实施：
  - 新增 `3rd/CoralinkerHost/packaging/`，作为发布包脚本源文件目录。
  - 新增：
    - `packaging/start-host.ps1`
    - `packaging/start-host.bat`
    - `packaging/start-host.sh`
    - `packaging/install-dotnet-sdk-ubuntu.sh`
  - `publish-host.ps1`：
    - 发布时从 `packaging/` 复制这些脚本到包根目录。
    - `publish-info.json` 增加 `setupScripts: ["install-dotnet-sdk-ubuntu.sh"]`。
    - `package-manifest.sha256` 包含安装脚本和启动脚本。
  - `install-dotnet-sdk-ubuntu.sh`：
    - 要求 root。
    - 读取 `/etc/os-release`。
    - 要求 `ID=ubuntu`。
    - 使用 `VERSION_ID` 拼接 Microsoft apt 源 URL：`https://packages.microsoft.com/config/ubuntu/<VERSION_ID>/packages-microsoft-prod.deb`。
    - 支持 `amd64` / `arm64`。
    - 安装 `dotnet-sdk-8.0` 并输出 SDK/runtime 列表。
  - `start-host.ps1` / `start-host.sh`：
    - 缺少 `dotnet`、SDK 或 runtime 时输出安装提示。
    - Ubuntu 提示运行：`sudo ./install-dotnet-sdk-ubuntu.sh`。
    - 其他 Linux 提示使用发行版包管理器或 Microsoft 文档：`https://learn.microsoft.com/dotnet/core/install/linux`。
    - PowerShell 版额外提示 Windows 下载页：`https://dotnet.microsoft.com/download/dotnet/8.0`。
    - 版本说明：Host 是 `net8.0`，运行时必须有 `Microsoft.NETCore.App 8.x` 和 `Microsoft.AspNetCore.App 8.x`；SDK 9.x 可以用于 Build，但不能替代缺失的 .NET 8 runtime。
  - `3rd/CoralinkerHost/README.md`：
    - 补充 `packaging/` 目录结构。
    - 补充 `install-dotnet-sdk-ubuntu.sh` 使用方法。
    - 补充版本 8 vs SDK 9 的说明。
- 验证：
  - 发布成功：`3rd/CoralinkerHost/Publish/CoralinkerHost_df02128_20260526-115545/`。
  - `start-host.ps1 -CheckOnly` 成功。
  - 直接读取发布包内 `start-host.ps1` / `start-host.sh`，确认包含 Ubuntu/其他 Linux 安装提示和版本说明。
  - 已压缩：`3rd/CoralinkerHost/Publish/CoralinkerHost_df02128_20260526-115545.tar.gz`。
  - 压缩包大小：`8,870,305` bytes。

### [publish] 启动脚本补充 Git 依赖检查
- 用户反馈：
  - 文件记录日志模块依赖 Git。
  - 启动脚本应检查 `git`。
  - 要求再查是否还有其他类似运行期外部命令依赖。
- 排查：
  - 搜索 `3rd/CoralinkerHost` 和 `3rd/CoralinkerSDK` 的 `ProcessStartInfo`、`Process.Start`、`FileName=`、`git`、`dotnet`。
  - 运行期外部命令依赖结论：
    - `dotnet`：`DiverBuildService` 执行 `dotnet restore/build`，并检查 SDK。
    - `git`：`GitHistoryService` 执行 `git add/commit/log/diff/show/checkout/status` 等，用于文件历史、diff、checkout/revert、项目历史。
    - `sha256sum`：仅 Linux 启动脚本用于 `package-manifest.sha256` 校验。
  - 未在 SDK 中发现额外运行期外部命令调用。
- 实施：
  - `packaging/start-host.ps1`：
    - 新增 `GitInstallHint`。
    - 新增 `Check-GitEnvironment`，检查 `git` 命令存在且 `git --version` 可执行。
    - 启动检查流程中加入 `Checking Git...`。
  - `packaging/start-host.sh`：
    - 新增 `git_install_hint` / `fail_git`。
    - 检查 `command -v git` 和 `git --version`。
  - `packaging/install-dotnet-sdk-ubuntu.sh`：
    - prerequisites 中安装 `git`。
    - 安装完成输出 `git --version`。
  - `3rd/CoralinkerHost/README.md`：
    - 启动检查列表加入 `git`。
    - Ubuntu 安装脚本说明改为同时安装 `.NET SDK` 和 `git`。
- 验证：
  - 发布成功：`3rd/CoralinkerHost/Publish/CoralinkerHost_df02128_20260526-120305/`。
  - 执行 `start-host.ps1 -CheckOnly` 成功，输出包含 `Checking Git...`。
  - 读取包内 `start-host.sh`，确认 Git 安装提示存在。
  - 读取包内 `install-dotnet-sdk-ubuntu.sh`，确认安装 `git` 并输出版本。
  - 已压缩：`3rd/CoralinkerHost/Publish/CoralinkerHost_df02128_20260526-120305.tar.gz`。
  - 压缩包大小：`8,870,445` bytes。

### [mcu-serial-bridge] Linux native bridge 移植第一版
- 用户要求：
  - 开始移植 `mcu_serial_bridge` 到 Linux，作为 ARM Linux 平台迁移的下一步。
- 排查：
  - native C 层 Win32 依赖集中在：
    - `MCUSerialBridge/c_core/include/msb_handle.h`
    - `MCUSerialBridge/c_core/src/msb_handle.c`
    - `MCUSerialBridge/c_core/src/msb_bridge.c`
    - `MCUSerialBridge/c_core/src/msb_thread.c`
    - `MCUSerialBridge/c_core/src/msb_packet.c`
    - `MCUSerialBridge/bootloader/src/mbl_bootloader.c`
  - C# wrapper P/Invoke 原先硬编码 `mcu_serial_bridge.dll`，Linux 下不合适。
- 实施：
  - 新增 `MCUSerialBridge/c_core/include/msb_platform.h`：
    - Windows 分支继续使用 `windows.h`。
    - Linux 分支声明当前 C 层使用的 Win32 子集类型/API。
  - 新增 `MCUSerialBridge/c_core/src/msb_platform_posix.c`：
    - `pthread` 实现 `CRITICAL_SECTION`、`CONDITION_VARIABLE`、`CreateThread`、`WaitForSingleObject`、auto-reset event。
    - `termios` + POSIX fd 实现 `CreateFileA`、`ReadFile`、`WriteFile`、`SetCommState`、`PurgeComm`、`FlushFileBuffers`。
    - `clock_gettime/gettimeofday/usleep` 实现 tick、local time、sleep。
  - `MCUSerialBridge/c_core/SConscript`：
    - 按 `env['PLATFORM']` 分 Windows / 非 Windows。
    - 非 Windows 增加 `-std=c11 -Wall -Wextra -fPIC -D_GNU_SOURCE`，链接 `pthread`，编译 `msb_platform_posix.c`。
    - Linux 下 SCons `SharedLibrary('mcu_serial_bridge')` 会生成 `libmcu_serial_bridge.so`。
  - `MCUSerialBridge/c_core/src/msb_bridge.c`：
    - Windows 保留 `\\.\COMx` 规范化。
    - Linux 保留传入串口设备路径，如 `/dev/ttyUSB0`。
  - `MCUSerialBridge/bootloader/src/mbl_bootloader.c`：
    - 改用 `msb_platform.h`。
    - Linux 保留原始串口路径，避免拼 Windows 前缀。
  - `MCUSerialBridge/wrapper/MCUSerialBridgeCLR.cs` 与 `MCUSerialBridge/wrapper/MCUBootloaderHandlerCLR.cs`：
    - P/Invoke 库名从 `mcu_serial_bridge.dll` 改为 `mcu_serial_bridge`，交给 .NET 按平台解析。
  - `MCUSerialBridge/README.md`：
    - 记录 Windows / Linux native bridge 构建方式、Linux 依赖、串口路径规则、验证边界。
- 验证：
  - `scons` 在 Windows 本机通过，生成 `MCUSerialBridge/build/mcu_serial_bridge.dll`。
  - `dotnet build MCUSerialBridge\wrapper\MCUSerialBridgeWrapper.csproj` 成功，只有既有 nullable warning。
  - `dotnet build 3rd\CoralinkerSDK\CoralinkerSDK.csproj` 成功，只有既有 nullable / Windows-only API warning。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release --no-restore` 成功，只有既有 warning。
  - 当前机器没有可用 WSL/Linux 构建环境，尚未在 Linux x64/ARM64 上实际生成 `.so`。

### [mcu-serial-bridge] Windows 交叉编译 Linux native 库方案
- 用户纠正：
  - 不要在 Linux 目标机上编译 `mcu_serial_bridge`。
  - 需要指导如何在 Windows 开发机上编译 Linux `.so`。
- 结论：
  - Windows 本机已有的 Strawberry `gcc` 是 Windows 目标编译器，只能生成 Windows 目标产物，不能直接生成 Linux ELF `.so`。
  - 推荐使用 Zig 作为 Windows 上的交叉编译器：
    - `zig cc -target x86_64-linux-gnu` 生成 Linux x64 `.so`。
    - `zig cc -target aarch64-linux-gnu` 生成 Linux ARM64 `.so`。
  - LLVM/Clang 也可行，但需要额外维护 Linux sysroot、glibc headers/libs、linker 配置，复杂度高。
- 后续建议：
  - 将 `MCUSerialBridge/c_core/SConscript` 扩展为支持 `TARGET_OS=linux TARGET_ARCH=x64|arm64`。
  - 交叉编译输出目录建议分平台：
    - `MCUSerialBridge/build/linux-x64/libmcu_serial_bridge.so`
    - `MCUSerialBridge/build/linux-arm64/libmcu_serial_bridge.so`
  - Host 发布包应携带这些预编译 native 库，不在目标机上编译。

### [mcu-serial-bridge] Zig 交叉编译与 Host 发布集成
- 用户要求：
  - 已安装 Zig。
  - 用最优雅的方式把环境、编译、发布都搞定，然后用户测试。
- 实施：
  - 新增 `MCUSerialBridge/build-native.ps1`：
    - 参数：`-Target all|windows|linux-x64|linux-arm64`、`-ZigPath`。
    - 自动生成 `msb_error_c.h` 和 `MCUSerialBridgeError.cs`。
    - Windows 目标调用 `scons`，并把 `build/mcu_serial_bridge.dll` 复制到 `build/runtimes/win-x64/native/`。
    - Linux x64 目标调用 `zig cc -target x86_64-linux-gnu`。
    - Linux ARM64 目标调用 `zig cc -target aarch64-linux-gnu`。
  - 新增 `MCUSerialBridge/wrapper/NativeBridgeLibraryResolver.cs`：
    - 使用 `NativeLibrary.SetDllImportResolver`。
    - 根据当前 OS 和 `RuntimeInformation.ProcessArchitecture` 选择：
      - `runtimes/win-x64/native/mcu_serial_bridge.dll`
      - `runtimes/linux-x64/native/libmcu_serial_bridge.so`
      - `runtimes/linux-arm64/native/libmcu_serial_bridge.so`
    - 保留根目录 native 文件作为 fallback。
  - `MCUSerialBridge/wrapper/MCUSerialBridgeCLR.cs` 和 `MCUBootloaderHandlerCLR.cs`：
    - 静态构造中调用 `NativeBridgeLibraryResolver.EnsureRegistered()`。
  - `MCUSerialBridge/wrapper/MCUSerialBridgeWrapper.csproj`、`3rd/CoralinkerSDK/CoralinkerSDK.csproj`、`3rd/CoralinkerHost/CoralinkerHost.csproj`：
    - 纳入 resolver 源文件。
    - Host/SDK 按 `.NET runtimes/<rid>/native/` 目录复制三平台 native assets。
  - `3rd/CoralinkerHost/publish-host.ps1`：
    - 默认发布前执行 `MCUSerialBridge/build-native.ps1 -Target all`。
    - 新增 `-SkipNativeBuild` 和 `-ZigPath`。
    - `publish-info.json` 增加 `skipNativeBuild` 与 `nativeBridgeRuntimes`。
  - `3rd/CoralinkerHost/packaging/start-host.ps1` / `start-host.sh`：
    - 启动检查加入三平台 native bridge 必需文件。
  - README：
    - `MCUSerialBridge/README.md` 改为 Windows 开发机使用 Zig 交叉编译 Linux native 库。
    - `3rd/CoralinkerHost/README.md` 记录发布脚本默认构建和打包三平台 native assets，并说明 `-SkipNativeBuild`。
- 验证：
  - `zig version` 输出 `0.16.0`。
  - `powershell -NoProfile -ExecutionPolicy Bypass -File MCUSerialBridge\build-native.ps1 -Target all` 成功。
  - 生成文件：
    - `MCUSerialBridge/build/runtimes/win-x64/native/mcu_serial_bridge.dll`，`186880` bytes。
    - `MCUSerialBridge/build/runtimes/linux-x64/native/libmcu_serial_bridge.so`，`173896` bytes。
    - `MCUSerialBridge/build/runtimes/linux-arm64/native/libmcu_serial_bridge.so`，`177168` bytes。
  - `dotnet build MCUSerialBridge\wrapper\MCUSerialBridgeWrapper.csproj` 成功，只有既有 nullable warning。
  - `dotnet build 3rd\CoralinkerSDK\CoralinkerSDK.csproj` 成功，只有既有 warning。
  - `powershell -NoProfile -ExecutionPolicy Bypass -File 3rd\CoralinkerHost\publish-host.ps1 -NoRestore` 成功。
  - 发布目录：`3rd/CoralinkerHost/Publish/CoralinkerHost_43b8087_20260526-125449/`。
  - 发布包包含三平台 native bridge，`package-manifest.sha256` 中也包含对应条目。
  - `start-host.ps1 -CheckOnly` 成功。

### [deploy] 上传并解压 Host 发布包到 192.168.0.117
- 用户要求：
  - 复制安装包到 `/home/industio/Coralinker` 下面，然后解压。
- 前置确认：
  - 本机存在 SSH key：
    - `C:\Users\lvzhe\.ssh\id_ecdsa`
    - `C:\Users\lvzhe\.ssh\id_ecdsa.pub`
  - 目标机 `192.168.0.117` 的 host key 已在 `known_hosts` 中。
  - 重新测试 key 登录成功：
    - `ssh -o BatchMode=yes -i "$env:USERPROFILE\.ssh\id_ecdsa" industio@192.168.0.117 "hostname && whoami"`
    - 输出：`industio` / `industio`。
- 实施：
  - 为最新发布目录创建 tar 包：
    - 源目录：`3rd/CoralinkerHost/Publish/CoralinkerHost_43b8087_20260526-125449/`
    - 包：`3rd/CoralinkerHost/Publish/CoralinkerHost_43b8087_20260526-125449.tar.gz`
    - 大小：`9,010,358` bytes。
  - 远端创建目录：
    - `/home/industio/Coralinker`
  - 使用 `scp` 上传到：
    - `/home/industio/Coralinker/CoralinkerHost_43b8087_20260526-125449.tar.gz`
  - 在远端执行：
    - `cd /home/industio/Coralinker && tar -xzf CoralinkerHost_43b8087_20260526-125449.tar.gz`
- 验证：
  - 远端目录存在：
    - `/home/industio/Coralinker/CoralinkerHost_43b8087_20260526-125449`
  - 远端 ARM64 native bridge 存在：
    - `/home/industio/Coralinker/CoralinkerHost_43b8087_20260526-125449/runtimes/linux-arm64/native/libmcu_serial_bridge.so`
  - 同目录也包含 `libSystem.IO.Ports.Native.so`。

### [publish/deploy] 修复 shell 脚本 CRLF 并完整重发
- 用户反馈：
  - 远端运行 `start-host.sh` 报错：
    - `start-host.sh: 2 set: Illegal option -`
- 排查：
  - 远端执行 `file start-host.sh`：
    - `ASCII text executable, with CRLF line terminators`
  - 读取前几个字节确认：
    - `#!/usr/bin/env sh\r\nset -eu\r\n`
  - 根因：
    - 之前 `publish-host.ps1` 里既有内联生成启动脚本，又从 `packaging/` 复制静态脚本，后者覆盖了前者。
    - `packaging/start-host.sh` 在 Windows 工作区中是 CRLF，导致包内 `.sh` 也是 CRLF。
- 用户纠正：
  - 不要在 `publish-host.ps1` 里面内联字符生成静态文件。
  - 有需要打包的静态文件，应直接放在 `packaging/`，并把 `.sh` 源文件做成 LF。
- 实施：
  - 从 `publish-host.ps1` 删除 `startHostPs1` / `startHostBat` / `startHostSh` 内联字符串与写文件逻辑。
  - `publish-host.ps1` 现在只复制：
    - `packaging/start-host.ps1`
    - `packaging/start-host.bat`
    - `packaging/start-host.sh`
    - `packaging/install-dotnet-sdk-ubuntu.sh`
  - 新增 `.gitattributes`：
    - `3rd/CoralinkerHost/packaging/*.sh text eol=lf`
  - 将 `packaging/start-host.sh` 和 `packaging/install-dotnet-sdk-ubuntu.sh` 当前文件内容转为 LF。
- 完整重建：
  - 执行：
    - `powershell -NoProfile -ExecutionPolicy Bypass -File 3rd\CoralinkerHost\publish-host.ps1`
  - 发布目录：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_43b8087_20260526-131325/`
  - 本地确认：
    - `start-host.sh CRLF=False`
    - `install-dotnet-sdk-ubuntu.sh CRLF=False`
    - `start-host.ps1 -CheckOnly` 成功。
  - 打包：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_43b8087_20260526-131325.tar.gz`
    - 大小：`9,010,389` bytes。
- 远端部署：
  - 上传到：
    - `/home/industio/Coralinker/CoralinkerHost_43b8087_20260526-131325.tar.gz`
  - 解压到：
    - `/home/industio/Coralinker/CoralinkerHost_43b8087_20260526-131325/`
  - `file start-host.sh install-dotnet-sdk-ubuntu.sh` 不再显示 CRLF。
  - `sh -n start-host.sh` 和 `sh -n install-dotnet-sdk-ubuntu.sh` 通过。
  - `sudo -n ./start-host.sh --check-only` 通过，输出 `Startup checks passed.`。

### [mcu-serial-bridge] Linux 高波特率支持修复
- 用户反馈：
  - Windows 发布包运行可 Probe 到节点。
  - Linux ARM64 发布包使用 `/dev/ttyACM0` 和 `1000000` 波特率时 Reset 超时：
    - `Probe Reset failed: Proto_Timeout`
- 判断：
  - Linux Posix 串口层 `msb_baud_to_speed()` 原先没有 `1000000` 映射。
  - 对 USB CDC/ACM 设备，Linux 应支持更多 line coding 波特率；不能静默退回低速。
  - `B115200` 等宏来自系统 `<termios.h>` 及 libc/内核平台头文件，不是项目内定义；标准宏覆盖范围有限。
- 实施：
  - `MCUSerialBridge/c_core/src/msb_platform_posix.c`：
    - 保留标准 termios 波特率映射。
    - 新增常见高速标准宏映射：`500000`、`576000`、`921600`、`1000000`、`1500000`、`2000000`、`2500000`、`3000000`、`3500000`、`4000000`。
    - 新增 Linux `termios2 + BOTHER` 自定义波特率路径。
    - 非标准波特率支持范围：`1..12000000`。
    - 超出范围返回 `ERROR_INVALID_PARAMETER`，不再静默降速。
  - `MCUSerialBridge/c_core/include/msb_platform.h`：
    - 新增 `ERROR_INVALID_PARAMETER`。
- 验证：
  - 首次 Zig 编译发现系统头已有 `TCGETS2/TCSETS2` 但缺少 `struct termios2` 定义；改为按 Linux ABI 定义 `struct termios2` 后通过。
  - `powershell -NoProfile -ExecutionPolicy Bypass -File MCUSerialBridge\build-native.ps1 -Target all` 成功。
  - 重新发布：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_43b8087_20260526-132546/`
  - 本地 `start-host.ps1 -CheckOnly` 成功。
  - 已上传并解压到：
    - `/home/industio/Coralinker/CoralinkerHost_43b8087_20260526-132546/`
  - 远端 `sudo -n ./start-host.sh --check-only` 成功。
  - 远端 ARM64 `libmcu_serial_bridge.so` 已更新，大小 `179472` bytes。

### [publish/deploy] Linux start-host 支持跳过完整性校验
- 用户反馈：
  - 现场调试时只替换发布包内新的 `libmcu_serial_bridge.so`，每次都重新计算 `package-manifest.sha256` 太麻烦。
  - 需要 `start-host.sh` 加一个选项，允许跳过完整性检查。
- 实施：
  - `3rd/CoralinkerHost/packaging/start-host.sh`：
    - 新增 `SKIP_INTEGRITY_CHECK`。
    - 参数解析改为循环，支持组合：
      - `--check-only`
      - `--skip-integrity-check`
      - `--`
    - 使用 `--skip-integrity-check` 时仍执行 root、dotnet、SDK/runtime、git、Host 文件、`wwwroot/`、`res/compiler/`、三平台 native 库等检查。
    - 只跳过 `package-manifest.sha256` 的存在性和 `sha256sum -c` 校验。
    - 默认行为保持不变：仍要求 `package-manifest.sha256` 并校验完整性。
  - `3rd/CoralinkerHost/README.md`：
    - 补充 Linux 调试命令：
      - `sudo ./start-host.sh --skip-integrity-check`
      - `sudo ./start-host.sh --check-only --skip-integrity-check`
    - 明确该选项用于临时替换 native `.so` 的调试场景，正式发布仍应使用默认完整性校验或重新发布。
- 远端验证：
  - 将更新后的 `start-host.sh` 复制到：
    - `/home/industio/Coralinker/CoralinkerHost_43b8087_20260526-132546/start-host.sh`
  - 远端执行：
    - `sh -n ./start-host.sh`
    - `sudo -n ./start-host.sh --check-only --skip-integrity-check`
  - 输出：
    - `WARNING: package integrity check skipped by --skip-integrity-check.`
    - `Startup checks passed.`

### [mcu-serial-bridge] 修复 Linux ARM64 Probe 1M baud timeout
- 用户反馈：
  - Windows 发布包可以 Probe 到 `CORAL-NODE-V2.1`。
  - Linux ARM64 发布包使用 `/dev/ttyACM0`、`1000000` baud 时 `Probe Reset failed: Proto_Timeout`。
  - 用户要求不要继续排查 DTR/RTS，因为物理上没有连接；需要一次性看完其他串口差异。
- 排查过程：
  - 写了 `ai-deck/tools/raw_msb_probe_acm0.sh`，支持：
    - `reset`
    - `version`
    - `layout`
    - `hex:<frame_hex>`
  - raw 脚本保持同一个 fd 打开，直接向 `/dev/ttyACM0` 写 MSB 帧并读取返回。
  - raw Reset/Version/Layout 均成功，证明设备链路和 `1000000` baud 可用。
  - 为 native 增加 `MSB_TRACE_IO=1` 诊断：
    - native 写出的 Reset 帧完整且 CRC 正确。
    - `after-write poll=0 queued=0` 时没有回包。
    - `tcgetattr` 校验显示请求 `baud=1000000` 时曾实际落为 `ispeed=13/ospeed=13`，即 Linux `B9600` 编码。
  - 直接 raw 发送 native 写出的同一帧能收到 `0x82`，排除 timestamp/CRC/协议帧问题。
- 根因：
  - Windows/Zig 交叉编译到 Linux ARM64 时，不能可靠依赖目标 libc/termios 标准 `B1000000` 常量路径。
  - `cfsetispeed/cfsetospeed` 标准路径可能没有真正把目标 fd 配到 1M，导致 MCU 看不到正确波特率的数据。
- 实施：
  - `MCUSerialBridge/c_core/src/msb_platform_posix.c`：
    - 保留 raw/8N1/无软件流控/无硬件流控配置。
    - `tcsetattr()` 后始终使用 `termios2 + BOTHER` 对 fd 写入精确数值波特率。
    - baud 支持范围仍为 `1..12000000`，超出返回 `ERROR_INVALID_PARAMETER`。
    - `ReadFile` 改为先 `poll()` 等待可读，再 `read()`，降低 Linux tty 0-timeout busy read 差异。
    - `DBG_PRINT` 增加 `fflush(stdout)`，避免 Host 重定向日志时 native 输出不及时落盘。
    - 保留 `MSB_TRACE_IO=1` 可选诊断输出；默认不输出。
  - `MCUSerialBridge/README.md`：
    - 增加 Linux 高波特率串口配置说明。
    - 记录 `sudo env MSB_TRACE_IO=1 ./start-host.sh --skip-integrity-check` 的诊断用法。
- 验证：
  - `powershell -NoProfile -ExecutionPolicy Bypass -File MCUSerialBridge\build-native.ps1 -Target linux-arm64` 成功。
  - 更新后的 `.so` 已复制到：
    - `/home/industio/Coralinker/CoralinkerHost_43b8087_20260526-132546/runtimes/linux-arm64/native/libmcu_serial_bridge.so`
  - 远端默认静默启动：
    - `sudo ./start-host.sh --skip-integrity-check`
  - 远端 API Probe 成功：
    - `serial://name=/dev/ttyACM0&baudrate=1000000`
    - Version: `CORAL-NODE-V2.1`, Commit `f1d8f16`, BuildTime `2026-05-13 17:14:38`
    - Layout: DI=27, DO=20, Ports=5

### [publish/deploy] 完整构建、打包、上传并解压新 Host 发布包
- 用户要求：
  - 重新跑一次完整构建、打包、推送和解压。
- 本地完整发布：
  - 执行：
    - `powershell -NoProfile -ExecutionPolicy Bypass -File 3rd\CoralinkerHost\publish-host.ps1`
  - 发布脚本自动执行：
    - `MCUSerialBridge/build-native.ps1 -Target all`
    - `dotnet publish 3rd/CoralinkerHost/CoralinkerHost.csproj -c Release`
    - 清理默认不需要的 SDK apphost sidecars 和 PDB。
    - 复制 `packaging/` 启动脚本。
    - 生成 `publish-info.json` 和 `package-manifest.sha256`。
  - 输出目录：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_43b8087_20260526-141521/`
  - 构建结果：
    - 成功，无 error。
    - 仍有既有 warning：
      - `DiverCompiler/Processor.cs` unreachable code。
      - `MCUSerialBridgeCLR.cs` nullable warning。
      - `SerialPortResolver.cs` Windows-only API analyzer warning。
- 打包：
  - 执行 tar 打包：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_43b8087_20260526-141521.tar.gz`
  - 包大小：
    - `9024272` bytes（远端显示约 `8.7M`）。
- 上传与解压：
  - 上传到：
    - `/home/industio/Coralinker/CoralinkerHost_43b8087_20260526-141521.tar.gz`
  - 远端解压目录：
    - `/home/industio/Coralinker/CoralinkerHost_43b8087_20260526-141521/`
- 远端验证：
  - 在新目录执行：
    - `sudo -n ./start-host.sh --check-only`
  - 结果：
    - 所有 `package-manifest.sha256` 条目校验 `OK`。
    - 输出 `Startup checks passed.`。
  - 停止旧 Host 后，从新目录启动 Host。
  - 远端 API Probe 成功：
    - `serial://name=/dev/ttyACM0&baudrate=1000000`
    - Version: `CORAL-NODE-V2.1`
    - Commit: `f1d8f16`
    - BuildTime: `2026-05-13 17:14:38`
    - Layout: DI=27, DO=20, Ports=5
- 当前远端状态：
  - 新 Host 从以下目录运行：
    - `/home/industio/Coralinker/CoralinkerHost_43b8087_20260526-141521/`
  - 进程命令：
    - `dotnet /home/industio/Coralinker/CoralinkerHost_43b8087_20260526-141521/CoralinkerHost.dll`

### [build/offline] Ubuntu 内网 Build 使用发布包内离线 NuGet 源
- 用户反馈：
  - Ubuntu 内网机器编译用户逻辑时 `dotnet restore` 失败：
    - `Unable to load the service index for source https://api.nuget.org/v3/index.json`
    - `Resource temporarily unavailable (api.nuget.org:443)`
  - 需要确认 `BitConverter`、`Math` 等基础库是否也需要在线包。
  - 希望梳理可能需要的预编译包，并让客户能在编译发布后的包里自行添加依赖。
- 判断：
  - `BitConverter`、`Math` / `MathF`、集合、`Encoding`、`DateTime`、`Task` 等来自 .NET SDK/runtime 基础类库，不需要 NuGet 包。
  - 失败来自 `LogicBuild.csproj` 固定 `PackageReference`：
    - `Fody`
    - `Newtonsoft.Json`
    - `System.IO.Ports`
    - `System.Management`
  - 其中传递依赖还包括：
    - `runtime.native.System.IO.Ports`
    - `System.CodeDom`
- 实施：
  - `3rd/CoralinkerHost/Services/DiverBuildService.cs`：
    - 临时用户逻辑工程的 `PackageReference` 改为读取 `res/compiler/build-packages.json`。
    - 默认清单缺失时仍回退到内置默认包列表。
    - Build 时生成项目级 `NuGet.Config`：
      - `<clear />` 清空所有外部源。
      - 只添加 `res/compiler/nuget-packages` 本地离线源。
    - `dotnet restore` 改为：
      - `dotnet restore --configfile <NuGet.Config> --verbosity minimal`
    - restore 退出码非 0 时明确打印 `RESTORE FAILED` 并抛出 `BuildFailedException`。
  - `3rd/CoralinkerHost/publish-host.ps1`：
    - 发布时生成 `res/compiler/build-packages.json`。
    - 从开发机 NuGet cache 复制离线包到 `res/compiler/nuget-packages/`。
    - 默认离线包：
      - `fody/6.6.4`
      - `newtonsoft.json/13.0.3`
      - `system.io.ports/9.0.3`
      - `runtime.native.system.io.ports/9.0.3`
      - `system.management/9.0.4`
      - `system.codedom/9.0.4`
    - `publish-info.json` 增加 `buildPackages` 和 `offlineNuGetPackages` 信息。
  - `3rd/CoralinkerHost/packaging/refresh-package-manifest.sh`：
    - 新增发布包内脚本。
    - 客户修改 `res/compiler/build-packages.json` 或 `res/compiler/nuget-packages/` 后，可在 Linux 目标机执行该脚本重新生成 `package-manifest.sha256`。
  - `start-host.ps1` / `start-host.sh`：
    - 启动检查新增：
      - `res/compiler/build-packages.json`
      - `res/compiler/nuget-packages/`
  - `3rd/CoralinkerHost/README.md`：
    - 记录默认离线包列表。
    - 记录客户新增第三方包流程：
      - 在联网开发机下载包和传递依赖。
      - 复制到 `res/compiler/nuget-packages/<lowercase-package-id>/<version>/`。
      - 修改 `res/compiler/build-packages.json`。
      - 执行 `sudo ./refresh-package-manifest.sh`。
- 验证：
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release` 成功，只有既有 warning。
  - `powershell -NoProfile -ExecutionPolicy Bypass -File 3rd\CoralinkerHost\publish-host.ps1 -SkipNativeBuild` 成功。
  - 新发布目录：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_7978a94_20260602-154935/`
  - 确认包含：
    - `res/compiler/build-packages.json`
    - `res/compiler/nuget-packages/fody/6.6.4/fody.nuspec`
    - `res/compiler/nuget-packages/runtime.native.system.io.ports/9.0.3/runtime.native.system.io.ports.nuspec`
    - `res/compiler/nuget-packages/system.codedom/9.0.4/system.codedom.nuspec`
    - `refresh-package-manifest.sh`
  - 新发布目录 `start-host.ps1 -CheckOnly` 成功。
  - `ReadLints` 检查相关修改文件无 linter error。

### [frontend/runtime] Root 节点变量刷新与 MCU 节点对齐
- 用户反馈：
  - 新增一个文件并编译后，导入到节点新增变量，有时 Variables 表没有立刻刷新。
  - 用户强调 Root 节点应尽量和 MCU 节点一样当成节点处理，避免后续触发和处理遗漏。
- 排查：
  - 后端变量来源：
    - `/api/variables/meta` 调用 `RootRuntimeService.EnsureConfiguredRegistered()` 后读取 `DIVERSession.Instance.GetAllCartFieldMetas()`。
    - `/api/variables` 调用 `RootRuntimeService.EnsureConfiguredRegistered()` 后读取 `DIVERSession.Instance.GetAllCartFields()`。
  - MCU 节点 Program 成功后：
    - `DIVERSession.ProgramNode()` 会设置 `MetaJson` / `LogicName`。
    - 立即 `HostRuntime.ParseMetaJson(metaJson)`。
    - 立即 `InitializeVariables(entry.CartFields)`。
  - Build 成功后 `HomeView.handleBuild()` 已有 MCU 节点自动刷新链路：
    - `reprogramAllNodes()`
    - `runtimeStore.refreshVariables()`
    - `runtimeStore.refreshFieldMetas()`
  - MCU 节点手动切换 Logic 后 `CoralNodeView.updateLogicName()` 也会刷新：
    - `runtimeStore.refreshVariables()`
    - `runtimeStore.refreshFieldMetas()`
  - Root 节点遗漏点：
    - `RootNodeView.configureRootLogic()` 只刷新 `fieldMetas`，没有刷新 `variables`。
    - `RootNodeView` 监听 `buildVersion` 时只 `loadRootInfo()`，没有刷新 Variables 表。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/RootNodeView.vue`：
    - 新增 `refreshRootNodeRuntime()`。
    - 统一执行：
      - `loadRootInfo()`
      - `runtimeStore.refreshVariables()`
      - `runtimeStore.refreshFieldMetas()`
    - `configureRootLogic()` 改为调用统一刷新方法。
    - `onMounted()` 改为调用统一刷新方法。
    - `watch(buildVersion)` 改为调用统一刷新方法。
- 验证：
  - `ReadLints` 检查 `RootNodeView.vue` 无 linter error。
  - `npm run build` 成功。
  - 构建中仍有既有 warning：
    - SignalR `/*#__PURE__*/` 注释位置 warning。
    - `device.ts` 同时动态/静态导入 warning。
    - chunk size warning。

### [frontend/history] 修复 From/To 选择后 Diff 显示为空
- 用户反馈：
  - History 显示 Diff 有问题。
  - 后端能拿到 diff，但前端展示为空。
  - 特别是在选择 From 和 To 以后容易出现；刷新后新打开时可能正常。
- 排查：
  - `HistoryPanel.vue` 原先的 Monaco Diff 只渲染 `diff.oldText` / `diff.newText`。
  - `/api/history/diff` 返回对象中即使 `unifiedDiff` 有内容，只要 `oldText/newText` 为空，前端就会创建两个空 model，表现为空白。
  - From/To 切换时，前端文件下拉使用 `fromCommit.files + toCommit.files` 的并集。
  - 这个并集只代表两个端点 commit 各自改过的文件，不代表当前 `from..to` 比较区间真实发生变化的文件。
  - 因此切换 From/To 后可能保留或默认选中一个“区间内实际无变化”的 `selectedPath`，导致单文件文本 diff 为空。
- 实施：
  - `3rd/CoralinkerHost/Services/GitHistoryService.cs`：
    - `GetDiff()` 增加一次 `git diff --name-only <from> <to> -- <scope>`。
    - `GitDiffResult` 增加 `Files` 字段，用于返回当前比较区间真实变更文件列表。
  - `3rd/CoralinkerHost/ClientApp/src/types/index.ts`：
    - `GitDiffResult` 增加 `files: string[]`。
  - `3rd/CoralinkerHost/ClientApp/src/components/history/HistoryPanel.vue`：
    - 新增 `rangeFiles` 状态。
    - `changedFiles` 优先使用 `rangeFiles`。
    - `setTo()` / `setFrom()` 在 all scope 下不再直接使用端点 commit 文件并集选默认文件，而是交给 `reloadSelectedDiff()` 根据区间文件选择。
    - `reloadSelectedDiff()` 先拉取当前范围 diff 得到 `rangeFiles`，如果当前 `selectedPath` 不在区间文件里，则切到第一个区间变更文件。
    - `renderDiff()` 增加兜底：没有 `oldText/newText` 时显示 `unifiedDiff`，避免 API 有 diff 但 UI 空白。
- 验证：
  - `ReadLints` 检查 `HistoryPanel.vue`、`types/index.ts`、`GitHistoryService.cs` 无错误。
  - `npm run build` 成功。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release` 成功。
  - 构建中仍有既有 warning：
    - Vite/Rollup chunk 和动态导入 warning。
    - .NET nullable / CA1416 等既有 warning。

### [frontend/history] 继续修复 Diff 双请求和 Monaco 空白渲染
- 用户反馈：
  - 实测点击 From 或 To 后，前端会请求两次 `/api/history/diff`。
  - hash 一样。
  - 后端第一次响应中 `oldText/newText/unifiedDiff` 可能为空，第二次响应有效。
  - 页面文字栏仍显示空白。
  - 用户追问新增文件 `oldText=null` 或删除文件 `newText=null` 是否已处理。
- API 直接验证：
  - 运行中 Host 地址：`http://localhost:4499`。
  - `/api/history/log?maxCount=10` 正常返回 4 个测试 input commit。
  - 对截图中的 `685d44a24767fe29479d9facdcece6cf8d95afc4 -> 99c6b2e8553ebb01453e965d47097db014778e65 / assets/inputs/A.cs` 调 `/api/history/diff`：
    - `oldText` 非 null，长度 1452。
    - `newText` 非 null，长度 1451。
    - `unifiedDiff` 非 null，长度 466。
    - `files` 为 `assets/inputs/A.cs`。
  - 新增文件 case：
    - `oldText = null`。
    - `newText` 非 null。
    - `unifiedDiff` 非 null。
  - 结论：
    - 新增/删除文件的 null 是正确语义。
    - 前端 `renderDiff()` 使用 `oldText ?? ""` / `newText ?? ""`，可以显示空旧栏或空新栏。
    - 当前空白问题更偏向 Monaco DiffEditor 容器生命周期/布局，而不是后端 diff 数据缺失。
- 后端修正：
  - `3rd/CoralinkerHost/Services/GitHistoryService.cs`：
    - `GetDiff()` 改为一次请求完成最终展示数据。
    - 总是先通过 `git diff --name-only <from> <to> -- assets/inputs` 获取当前区间真实变更文件。
    - 如果请求 path 为空，或 path 不在当前区间变更文件中，则自动切到第一个区间变更文件。
    - 再对最终 path 计算 `unifiedDiff` / `oldText` / `newText`。
    - 同一响应返回 `files`、最终 `path` 和最终文本内容。
- 前端修正：
  - `3rd/CoralinkerHost/ClientApp/src/components/history/HistoryPanel.vue`：
    - 去掉 `reloadSelectedDiff()` 中先请求范围 diff 再请求文件 diff 的双请求。
    - `setFrom()` / `setTo()` 后只发一次 `historyStore.loadDiff()`。
    - 增加 `diffRequestId`，旧请求返回时直接丢弃，避免快速点击造成旧响应覆盖新响应。
    - Diff 容器由 `v-if="diff"` 改为 `v-show="diff"`，避免 `diff=null` 时销毁 Monaco 容器。
    - `watch(props.show)` 在面板隐藏时调用 `disposeDiffEditor()`。
    - 新增 `diffEditorContainer`，如果当前 editor 绑定的 DOM 容器不是当前 `diffRef`，则 dispose 并重建。
    - `renderDiff()` 在 `nextTick()` 后再等待一帧 `requestAnimationFrame()`，确保容器尺寸稳定后再 `layout()`。
- 验证：
  - `ReadLints` 检查相关文件无错误。
  - `npm run build` 成功。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release` 成功。
  - 重新发布成功：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_7978a94_20260602-163625`

### [host/about] 增加前后端版本信息与发布前端编译
- 用户反馈：
  - 怀疑 publish 后前端没有重新编译。
  - 希望界面能看到前端/后端版本。
  - 入口建议放在 Graph 标题/工具栏 Add Node 右侧，点击 About 弹窗显示前端版本、后端版本（commit/tag/build）和 Copyright。
- 排查：
  - 原 `publish-host.ps1` 直接执行 `dotnet publish`。
  - `ClientApp` 被 csproj 排除，不会由 `dotnet publish` 自动重新执行 `npm run build`。
  - 因此发布包可能携带旧 `wwwroot`。
- 实施：
  - `3rd/CoralinkerHost/publish-host.ps1`：
    - 发布前进入 `ClientApp` 执行 `npm run build`。
    - 生成 `wwwroot/build-info.json`，包含：
      - `app`
      - `version`
      - `tag`
      - `commit`
      - `commitTime`
      - `buildTime`
      - `configuration`
      - `dirty`
    - `publish-info.json` 增加 `tag`。
  - `3rd/CoralinkerHost/Services/HostAboutService.cs`：
    - 读取 `publish-info.json` 和 `wwwroot/build-info.json`。
    - 在开发态尝试从 git 读取 commit/tag。
    - 返回 Backend/Frontend 两组版本信息。
  - `3rd/CoralinkerHost/Program.cs`：
    - 注册 `HostAboutService`。
  - `3rd/CoralinkerHost/Web/ApiRoutes.cs`：
    - 新增 `/api/about`。
  - `3rd/CoralinkerHost/ClientApp/src/api/about.ts`：
    - 新增 About API。
  - `3rd/CoralinkerHost/ClientApp/src/types/index.ts`：
    - 新增 `HostVersionInfo`、`HostAboutSnapshot`。
  - `3rd/CoralinkerHost/ClientApp/src/views/HomeView.vue`：
    - Graph 工具栏 Add Node 右侧新增 About 按钮。
    - 新增 About 弹窗，显示 Frontend/Backend 版本信息与 Copyright。
- 验证：
  - `ReadLints` 检查相关文件无错误。
  - `npm run build` 成功。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release` 成功。
  - `publish-host.ps1 -SkipNativeBuild` 输出明确先执行 `Building CoralinkerHost frontend...`。
  - 新发布目录：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_7978a94_20260602-164942`
  - 确认发布目录存在：
    - `wwwroot/build-info.json`
    - `publish-info.json`
  - 从发布目录启动 Host 后请求 `/api/about` 成功，返回 Backend/Frontend 的 tag/commit/build/layout。

### [frontend/graph] Variables Flow 叠加图层第一版
- 用户需求：
  - Variables 现在是平铺表格，希望能以依赖关系图显示。
  - 在 Graph 左下方增加按钮，默认不开启，避免前端卡顿。
  - 开关是纯前端行为，不保存。
  - `ControlItem` 作为 Root 私有变量显示在 Root 框内部。
  - `mutual` / `upper` / `lower` 作为 Root 和 Node 间交换变量，以小框显示 `Type Name Value`，按类型上色。
  - 用细线/箭头表示来源和去向，最好为弧线，并随节点拖动自动重排。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - 新增左下角 `Var Flow` 按钮。
    - 新增 `showVariablesFlow` 前端局部状态，不进入 store，不保存。
    - 开启时调用：
      - `runtimeStore.refreshVariables()`
      - `runtimeStore.refreshFieldMetas()`
    - 新增 `variables-flow-layer` 叠加层，不加入 VueFlow nodes/edges，避免触发图保存。
    - 通过读取 `.vue-flow__transformationpane` 的 CSS transform，使 Flow 图层跟随 VueFlow 平移/缩放。
    - 变量框布局：
      - `control` 变量放入 Root 节点内部。
      - 其他变量放在 Root 和节点之间。
      - 位置由 Root 和 MCU 节点坐标计算。
    - 线条布局：
      - `lower`: Root -> Variable -> Nodes。
      - `upper`: Nodes -> Variable -> Root。
      - `mutual`: 当前第一版绘制 Root/Nodes 之间共享流。
      - 使用 SVG cubic bezier 弧线和箭头 marker。
    - 颜色：
      - `upper`: blue
      - `lower`: green
      - `mutual`: amber
      - `control`: purple
- 当前限制：
  - 由于当前前端变量数据只有全局变量名/direction/value，没有精确到“哪个 Node 产生/消费”的 per-node 依赖元数据，第一版对非 control 变量默认连到所有 MCU 节点。
  - 后续如果后端补充变量来源/去向 meta，可把边从“全节点广播”改成精确边。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue` 无错误。
  - `npm run build` 成功。
  - 构建中仍有既有 Vite/Rollup warning：
    - SignalR pure annotation warning。
    - `device.ts` 动态/静态导入 warning。
    - chunk size warning。

### [frontend] 拆分 Variables Flow 布局/布线模块并撤掉高开销 A* 路由
- 用户反馈：
  - Variables Flow 不只是线避让，还包括变量位置安排，整体更像 PCB 的布局+布线问题。
  - `GraphCanvas.vue` 文件过长，应把布局布线单独抽出文件。
  - 用户要求参考 ComfyUI 的做法。
- 调研：
  - 查询 ComfyUI / ComfyUI_frontend 后确认：
    - ComfyUI 当前前端基于 LiteGraph。
    - 连线渲染以 slot-based link rendering 为主。
    - 支持 Straight / Linear / Spline 等 Link Render Mode。
    - 并不是每帧执行全局 A* / maze routing。
  - 因此撤掉前一版高开销 A* 路由，避免拖动节点时重新路由导致明显卡顿。
- 实施：
  - 新增 `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - `computeVariableFlowLayout()` 作为唯一入口。
    - 内部包含：
      - 变量框候选位置评分。
      - 变量顺序候选/全排列。
      - slot 锚点。
      - 正交 polyline 路径生成。
      - 几何评分工具。
    - 导出类型/常量：
      - `NodeRect`
      - `VariableFlowItem`
      - `FlowLine`
      - `ROOT_SIZE`
      - `NODE_SIZE`
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - 删除旧的大段 layout/routing/geometry/A* 函数。
    - 只保留 VueFlow、节点 DOM 测量、状态刷新和 SVG 渲染。
    - 通过 `computeVariableFlowLayout()` 获取变量框和连线路径。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue`、`variableFlowLayout.ts` 无错误。
  - `npm run build` 成功。
  - 构建中仍有既有 Vite/Rollup warning：
    - SignalR pure annotation warning。
    - `device.ts` 动态/静态导入 warning。
    - chunk size warning。

### [frontend] Variables Flow 增强 J/effort 优化：端点分散与变量顺序评分
- 用户反馈：
  - 进出线端点不要挤在一起，应分开一点。
  - 线可以按示意图用弧线排列。
  - 多节点时变量的位置和顺序都可以调整，目标是让总体 J/effort 最小。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - 新增 `FlowLayoutResult`，布局结果同时包含变量组原点和优化后的变量顺序。
    - `createVariableOrderCandidates()`：
      - 变量数量 `<= 6` 时做全排列，直接参与候选评分。
      - 变量更多时使用原顺序、反序、按方向启发式排序等候选，避免阶乘爆炸。
    - 真实绘制和评分统一使用 slot 锚点：
      - Root/Node 边上的锚点按变量序号分散。
      - 变量框边上的锚点按 Root/target 分散。
    - `anchorToward()`：
      - 根据另一端相对位置选择 top/bottom/left/right 边。
      - 同一边按 slot 分配端点，避免多条线挤在一起。
    - `curvePath()`：
      - 上下关系使用竖向三次贝塞尔控制点。
      - 左右关系使用横向三次贝塞尔控制点。
    - `scoreVariableConnections()`：
      - 评分阶段也使用同样的 slot 锚点，保证评分和实际绘制一致。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue` 无错误。
  - `npm run build` 成功。
  - 构建中仍有既有 Vite/Rollup warning：
    - SignalR pure annotation warning。
    - `device.ts` 动态/静态导入 warning。
    - chunk size warning。

### [frontend] Variables Flow 改为自动占位/推挤评分布局
- 用户反馈：
  - 横向排列时变量放在 Root 和 Node 之间效果较好。
  - 竖向排列时变量继续横向放在中间会挡住节点和线。
  - 希望变量框和连线都参与自动推挤；前端运算频次不高，可以多算。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - 外部变量布局从“选一列 + 纵向避让”改为“整组变量 block 候选点评分”。
    - 生成候选位置：
      - Root/Node 中心区域。
      - 节点包围盒上方、下方、左侧、右侧。
      - 四角和简单 3x3 网格候选点。
    - 评分项：
      - 变量 group 是否与 Root/Node padding 后区域重叠。
      - 虚拟连线是否穿过 Root/Node padding 后区域。
      - 连线总长度。
      - 变量 group 中心与 Root/Node 中心区域的距离。
    - 新增竖向拓扑偏置：
      - 当 Root 和 Node 主要呈上下关系时，惩罚变量组停在节点包围盒的中间高度带。
      - Root 在上、Node 在下时，优先把变量组推到下方。
    - 新增几何工具：
      - `uniquePoints()`
      - `distance()`
      - `overlapArea()`
      - `segmentIntersectsRect()`
      - `segmentsIntersect()`
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue` 无错误。
  - `npm run build` 成功。
  - 构建中仍有既有 Vite/Rollup warning：
    - SignalR pure annotation warning。
    - `device.ts` 动态/静态导入 warning。
    - chunk size warning。

### [frontend] 修正 Variables Flow 的 Upper/Lower 箭头方向
- 用户反馈：
  - 截图显示 Variables Flow 箭头方向明显不符合预期。
- 根因：
  - 上一版将 `upper` / `lower` 的数据流语义反了。
  - 正确语义：
    - `upper` / `AsUpperIO`：Root/上位机下发到 MCU 节点。
    - `lower` / `AsLowerIO`：MCU 节点回传到 Root/上位机。
  - 变量框在自动布局后可能位于 Root/Node 任意一侧，固定变量框左/右端点会继续造成局部箭头方向看起来反。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - `upper` 改为 Root -> Variable -> Node。
    - `lower` 改为 Node -> Variable -> Root。
    - `mutual` 暂按双向路径绘制，避免单箭头误导。
    - 新增 `itemToRect()`，变量框也按矩形参与动态锚点计算。
    - 每段线的源端点和目标端点都使用 `anchorToward(rect, point)`，按另一端位置选择矩形左右边。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue` 无错误。
  - `npm run build` 成功。
  - 构建中仍有既有 Vite/Rollup warning：
    - SignalR pure annotation warning。
    - `device.ts` 动态/静态导入 warning。
    - chunk size warning。

### [frontend/graph] 修正 Variables Flow 节点位置计算
- 用户反馈：
  - Variables Flow 显示后，节点位置计算不对。
  - 截图表现为变量框和箭头落点漂移，曲线绕远。
- 原因：
  - 第一版使用固定 `ROOT_SIZE/NODE_SIZE` 估算 Root 和 MCU 节点尺寸。
  - 实际 VueFlow 节点 DOM 尺寸与估算差异明显。
  - 变量框位置基于固定右侧锚点和平均坐标，容易放到不合理位置。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - 新增 `measuredNodeRects`。
    - 开启 Flow 后 `nextTick()` 立即测量节点 DOM。
    - 在 Flow transform 同步循环中读取 `.vue-flow__node[data-id]` 实际 `offsetWidth/offsetHeight`。
    - 以实际 Root/Node 矩形计算：
      - Root 右侧锚点。
      - MCU 节点左侧锚点。
      - MCU 节点包围盒。
      - 变量框中间布局位置。
    - `curvePath()` 支持左右方向，减少节点相对位置变化时的反向控制点错误。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue` 无错误。
  - `npm run build` 成功。

### [runtime/variables] API 排查 A.cs 加载后 Variables 缺少 input/output
- 用户反馈：
  - Node 加载了编译后的产物 `A.cs`。
  - `A.cs` 里定义了 `input` 和 `output`。
  - 但 Variables 表没有显示它们。
  - 用户要求先直接调用当前运行后端 API，查看后端版本、nodes、files 情况。
- 当前运行后端：
  - `/api/about`：
    - Backend layout: `Published`
    - Backend tag: `7978a94-dirty`
    - Backend commit: `7978a94`
    - Backend buildTime: `2026-06-02T16:49:42.2260707+08:00`
    - Frontend buildTime: `2026-06-02T16:49:42.2260707+08:00`
- 节点状态：
  - `/api/nodes`：
    - 当前 1 个节点：`b763edcd30594cf48da109f58fee9353`
    - `nodeName = Node-1-b763edcd`
    - `logicName = A`
    - `hasProgram = true`
    - `programSize = 289`
    - `cartFields = []`
    - buildInfo:
      - `sourceCommitShort = ed3ac25`
      - `buildTime = 2026-06-02T17:22:37.9404062+08:00`
      - `buildId = 20260602-092238`
  - `/api/node/b763edcd30594cf48da109f58fee9353` 返回同样结果，`cartFields = []`。
- 文件/产物状态：
  - `/api/logic/list`：
    - 只有 `A`
    - `binSize = 289`
    - `jsonSize = 2`
  - `/api/files/tree`：
    - 存在 `assets/inputs/A.cs`
    - 存在 `assets/generated/A.bin`
    - 存在 `assets/generated/A.bin.json`
    - 存在 `assets/generated/A.build.json`
    - 存在 `assets/generated/A.diver`
    - 存在 `assets/generated/A.diver.map.json`
  - `/api/files/read?path=assets/generated/A.bin.json`：
    - 内容为 `[]`
  - `/api/files/read?path=assets/inputs/A.cs`：
    - 源码确实有：
      - `[AsUpperIO] public int input;`
      - `[AsLowerIO] public int output;`
  - `/api/files/read?path=assets/generated/A.diver`：
    - 不包含 `input`
    - 不包含 `output`
  - `/api/files/read?path=assets/generated/A.diver.map.json`：
    - 不包含 `input`
    - 不包含 `output`
- Variables API：
  - `/api/variables/meta`：
    - 只有 `Node-1-b763edcd.__iteration`
  - `/api/variables`：
    - 只有 `Node-1-b763edcd.__iteration = 0`
- 结论：
  - 这次现象不是前端 Variables 刷新没有拿到数据。
  - 后端当前节点信息的 `cartFields` 已经为空。
  - 编译产物 `A.bin.json` 是空数组 `[]`。
  - 因此 Variables 表只显示默认 `__iteration` 是符合当前后端状态的。
  - 下一步要查 Build/Compiler metadata 生成逻辑，尤其是只在 CartDefinition 里定义字段、但 `Operation()` 中没有实际访问 `cart.input/cart.output` 时，是否没有被 compiler/meta 扫描出来。

### [template/mcu] 默认 MCU 初始例程实际访问 IO 字段
- 用户决策：
  - 暂不修改编译器。
  - 未访问的变量不进入编译 metadata 是合理语义。
  - 调整默认初始例程，让模板里的 IO 字段被实际访问，从而新建后能进入 Variables 表。
- 根因确认：
  - `DiverCompiler/ModuleWeaver.cs` 生成 `<Logic>.bin.json` 时使用 `dll.IOs`。
  - `dll.IOs` 来自 `Processor` 中实际参与编译/访问的 Cart IO 字段集合。
  - 只在 `CartDefinition` 中声明字段，但 `Operation()` 不读写该字段时，字段不会进入 `dll.IOs`，因此 `*.bin.json` 为 `[]`。
- 实施：
  - `3rd/CoralinkerHost/Web/ApiRoutes.cs`：
    - `GenerateDefaultTemplate()` 中默认 MCU Cart 字段改名：
      - UpperIO: `<ClassName>ControlSpeed`
      - LowerIO: `<ClassName>ActualSpeed`
    - 删除原 `input` / `output` 字段名。
    - `Operation()` 中实际读写：
      - `var random = iteration % 10;`
      - `cart.<ClassName>ActualSpeed = cart.<ClassName>ControlSpeed + 1 * random;`
    - 注释同步改为新字段名。
- 验证：
  - `ReadLints` 检查 `ApiRoutes.cs` 无错误。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Release` 成功。
  - 构建中仍有既有 warning：
    - `DiverCompiler/Processor.cs` unreachable code。
    - SDK/Host nullable 和 Windows-only API warning。

### [frontend/runtime] 修复 Build 后 Root Control 变量在 Variables 表消失
- 用户反馈：
  - RootPC 选择 `A` 后 `joystickX/Y` 会出现。
  - RootPC 选择 None 后 `joystickX/Y` 消失，这是正确行为。
  - 再次选择 `A` 后变量成功出现。
  - 但保持 Root=A 时点击 Build，Variables 表里的 `joystickX/Y` 消失。
  - 用户怀疑可能是编译没完成就刷新，或刷新流程不对。
- API 排查：
  - 改用 `curl.exe --max-time`，避免 PowerShell `Invoke-RestMethod` 在 Cursor 工具层卡住。
  - `/api/ping` 正常。
  - Root=None 时：
    - `/api/root/state` 返回 `logicName=null`
    - `cartFields=[]`
    - `controlFields=[]`
    - 这是正确状态。
  - `/api/root/logics`：
    - Root logic `A` 存在。
    - `cartFields` 包含 `left_diff_speed/right_diff_speed`。
    - `controlFields` 包含 `joystickX/joystickY`。
  - 用 API 配置 Root=A 后：
    - `/api/root/state` 返回 `logicName=A`。
    - `controlFields` 包含 `joystickX/joystickY`。
    - `/api/variables/meta` 包含 `joystickX/joystickY`。
  - 触发 `/api/build` 后：
    - Build 成功。
    - 返回 `rootLogics=["A"]`。
    - `/api/root/state` 仍保持 `logicName=A`，且 `controlFields` 仍包含 `joystickX/joystickY`。
    - `/api/variables/meta` 仍包含 `joystickX/joystickY`。
    - `/api/variables` 也仍包含 `joystickX/joystickY`。
- 结论：
  - 后端 Build 后没有丢 Root 配置。
  - 后端 Variables API 也没有丢 Root control variables。
  - 问题在前端 Build 后刷新顺序/异步竞态。
- 根因：
  - `HomeView.handleBuild()` 在 Build 成功后较早调用 `filesStore.notifyBuildComplete()`。
  - 这会触发 `RootNodeView` 的 `watch(buildVersion)`，并发执行 `refreshRootNodeRuntime()`。
  - 同时 `HomeView` 还在继续：
    - `reprogramAllNodes()`
    - `runtimeStore.refreshVariables()`
    - `runtimeStore.refreshFieldMetas()`
  - 多条异步刷新链路没有顺序保护，旧请求晚返回时可能覆盖新状态。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/views/HomeView.vue`：
    - 将 `filesStore.notifyBuildComplete()` 移到最终刷新之后：
      - `await reprogramAllNodes()`
      - `await runtimeStore.refreshVariables()`
      - `await runtimeStore.refreshFieldMetas()`
      - `filesStore.notifyBuildComplete()`
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/RootNodeView.vue`：
    - 增加 `rootRefreshId`。
    - `loadRootInfo()` 每次请求生成序号。
    - 如果旧请求晚返回，返回 `false`。
    - `refreshRootNodeRuntime()` 在旧请求情况下直接停止，不再继续刷新 variables/metas。
- 验证：
  - `ReadLints` 检查 `HomeView.vue`、`RootNodeView.vue` 无错误。
  - `npm run build` 成功。
  - 构建中仍有既有 Vite/Rollup warning：
    - SignalR pure annotation warning。
    - `device.ts` 动态/静态导入 warning。
    - chunk size warning。

### [publish] 发布测试版本 CoralinkerHost_7978a94_20260602-182113
- 用户请求：
  - 打一个 publish 版本，用户自己测试。
- 执行：
  - 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File "3rd\CoralinkerHost\publish-host.ps1" -SkipNativeBuild`
  - 发布脚本先执行前端构建：
    - `npm run build`
  - 再执行 Host publish。
- 结果：
  - 新发布目录：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_7978a94_20260602-182113`
  - publish info：
    - `tag = 7978a94-dirty`
    - `commit = 7978a94`
    - `publishTime = 2026-06-02T18:21:13.7242814+08:00`
  - frontend build info：
    - `buildTime = 2026-06-02T18:21:13.7242814+08:00`
- 验证：
  - `CoralinkerHost.dll` 存在。
  - `wwwroot/index.html` 存在。
  - `package-manifest.sha256` 存在。
- 注意：
  - 本次使用 `-SkipNativeBuild`，未重新编译 native bridge。
  - 构建仍有既有 Vite/Rollup warning 和 .NET nullable/平台 API warning。

### [frontend] 修复 Variables Flow 连线缩放和变量布局拥挤
- 用户反馈：
  - Variables Flow 连线端点位置不对，拖动节点后方向大致正确，但看起来存在缩放。
  - 变量框上下挤在一起不好看，希望能分散并尽量不挡住现有节点。
- 根因判断：
  - 叠加层本身跟随 VueFlow transformation pane 做 transform 是正确的。
  - SVG 的 `viewBox` 与 CSS 固定尺寸不一致，会把 path 坐标再次按 SVG 比例缩放，造成端点偏移/缩放感。
  - 原锚点固定使用 Root 右边、Node 左边，节点位置变化后无法保证端点位于朝向变量框的一侧。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - 新增 `flowCanvasSize`，让 SVG `viewBox`、`width`、`height`、CSS 尺寸一致，避免二次缩放。
    - 连线端点改为 `anchorToward(rect, point)`，根据变量框所在方向选择节点左/右边，并将端点 y 限制在节点矩形内部。
    - 新增 `layoutExternalVariables()`：
      - 根据 Root 和 MCU 节点包围盒选择变量列。
      - 优先使用 Root/Node 中间空隙，空隙不足时选择整体包围盒左右侧候选列。
      - 对 Root/Node 占用区做 padding，变量框纵向错开并避让已放置变量。
    - 曲线路径仍使用三次贝塞尔，但端点改为动态锚点。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue` 无错误。
  - `npm run build` 成功。
  - 构建中仍有既有 Vite/Rollup warning：
    - SignalR pure annotation warning。
    - `device.ts` 动态/静态导入 warning。
    - chunk size warning。

### [frontend/graph] 实现固定 Variables Flow 层级布局
- 用户请求：
  - 执行“固定变量流布局”计划，不修改计划文件。
  - Graph 不再自由布局，VarFlow 常开。
  - Root 在上，ControlItem 在 Root 下方，IO 变量在 Root/Nodes 中间，Nodes 在下方按顺序排列。
  - Node 和变量都支持拖拽交换/调整顺序并持久化。
  - `__` 开头变量不显示。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - 删除候选评分式变量布局，改为 `computeFixedGraphLayout()` 确定性布局。
    - Root 宽度随 control 变量数量扩展。
    - Control 变量、外部 IO 变量、Node 分层排列。
    - 输出 Root/Node 矩形、变量框和 slot-based orthogonal 连线路径。
    - 新增 `mergeVariableOrder()`，过滤 `__` 变量；无新增变量时保持顺序，有新增变量时按 direction 分组和当前变量列表顺序插入，保留已有变量相对顺序。
    - 修正 Root/Node 中心被 IO 变量总宽度推远的问题：变量行只自身向右展开，不再反推 Root/Node 布局中心。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - `showVariablesFlow` 默认 `true`，挂载后刷新 variables/metas 并启动 overlay transform 同步。
    - VueFlow 节点坐标由固定布局回写，不再随机初始化或保存 `x/y`。
    - Root 节点 `draggable=false`。
    - Node 拖拽停止后按最近水平槽位移动顺序，并保存到每个 Node 的 `extraInfo.order`；保存前合并现有 `extraInfo`，避免覆盖其它字段。
    - 变量框使用 HTML drag/drop 调整 `variableFlowOrder`。
    - VueFlow 连接禁用，位置变更不再触发自由布局保存。
    - 加载和新增节点后触发一次 `fitView()`，避免异步加载后视口停留在空区域。
  - `3rd/CoralinkerHost/ClientApp/src/types/index.ts`、`stores/project.ts`：
    - `ProjectState` 新增 `variableFlowOrder?: string[]`。
    - Pinia project store 新增 `variableFlowOrder` 和 `setVariableFlowOrder()`。
  - `3rd/CoralinkerHost/Services/ProjectStore.cs`：
    - 后端 `ProjectState` 新增 `VariableFlowOrder`。
    - `SaveToDisk()` / `LoadFromDiskIfExists()` 读写 `variableFlowOrder`，随 `project.json` 导入导出。
  - `RootNodeView.vue`、`CoralNodeView.vue`：
    - 根元素补 `width: 100%` / `box-sizing: border-box`，让 VueFlow 固定宽度作用到组件。
- 验证：
  - `ReadLints`：相关前端文件无 linter error。
  - `npm run build`：成功；仍有既有 SignalR pure annotation、`device.ts` 动态/静态导入、chunk size warning。
  - `dotnet build CoralinkerHost.csproj`：成功；仍有既有 nullable 和 Windows-only API warning。

### [frontend/graph] 修复刷新后节点闪现再消失
- 用户反馈：
  - 前端 dev 启动后刷新网页，节点会闪现一下然后消失。
  - 怀疑可能是后端数据缺字段或其它前后端问题。
- 判断：
  - 由于节点已经闪现，说明后端节点数据已经到达前端并至少完成过一次渲染。
  - 更可能是前端在下一帧由变量刷新、DOM 测量或 layout watcher 触发二次布局，把节点状态/视口改坏。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - 固定布局输入不再使用 `getNodeRect(node)` 的 DOM 实测尺寸。
    - 删除 `measuredNodeRects`、`measureNodeRects()`、`getNodeRect()`、`sameRectMap()` 等测量反馈链路。
    - `syncFlowTransform()` 只同步 VueFlow transformation pane，不再每帧测量节点。
    - `flowCanvasSize` 改用 `computeFixedGraphLayout()` 输出的 Root/Node rect 与变量框位置计算。
  - 保留固定尺寸布局语义：Root/Node 坐标和尺寸由 `variableFlowLayout.ts` 确定，不再受 DOM 渲染结果反向影响。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue`、`variableFlowLayout.ts` 无错误。
  - `npm run build` 成功；仍有既有 Vite/Rollup warning。

### [frontend/graph] 修复 Root ControlItem 溢出和 Root 连线锚点
- 用户反馈：
  - 变量连接到 Root 的位置不对。
  - ControlItem 位置超出了 Root 框下方，需要修改 Root 高度。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - Root 高度在存在 ControlItem 时自动增加，包含 control 变量行高度和底部 padding。
    - ControlItem 不再布局到 `rootRect.y + rootRect.height + gap`，改为放入 Root 内部底部区域。
    - Root 连接锚点改为 `anchorRootBottom()`，固定落在 Root 底边，并增加左右 inset，避免连线端点挤到 Root 内部文字/控件区域。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/RootNodeView.vue`：
    - `.root-node` 增加 `height: 100%`，填满 VueFlow 布局给出的高度。
    - `.node-content` 增加底部 padding，为内嵌 ControlItem 留出空间。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts`、`RootNodeView.vue` 无错误。
  - `npm run build` 成功；仍有既有 Vite/Rollup warning。

### [frontend/graph] 调整固定变量流 UI 尺寸与连线形态
- 用户反馈：
  - 普通 Node 宽度太窄，内容溢出。
  - 变量框尺寸不一定固定，可以由内容决定。
  - 变量框之间连线不要横平竖直，改用贝塞尔连接。
  - 连接的节点位置需要分开一点。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - `NODE_SIZE.width` 从 `320` 调整为 `420`，给端口配置和 IO 区域更多宽度。
    - 新增变量框尺寸估算逻辑，根据 type/name/value 长度计算宽度，并设置 min/max 限制。
    - Control 变量和外部 IO 变量行布局改为使用逐项宽度累加，而不是固定宽度等距。
    - 连线路径从正交 polyline 改为三次贝塞尔曲线。
    - 增大连接 slot margin，并给多条线加入 lane offset，减少端点和曲线重叠。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - 变量框 DOM 样式绑定 `item.width` / `item.height`。
    - 移除 CSS 中变量框固定 `width: 210px` / control 固定 `width: 154px`。
    - 变量框 grid 改为 `auto minmax(0, 1fr) auto`，适配动态宽度。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue`、`variableFlowLayout.ts` 无错误。
  - `npm run build` 成功；仍有既有 Vite/Rollup warning。

### [frontend/graph] 修复 Root 内容压线、变量框排版和内部变量过滤
- 用户反馈：
  - Root 自身内容仍然超出或被 ControlItem 压住。
  - 变量框希望固定为两排布局：左侧显示类型，右侧第一排显示名字、第二排显示值。
  - `__iteration` 不要显示出来；当前带节点前缀的 `Node-...__iteration` 仍显示了。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - `ROOT_SIZE.height` / `ROOT_BASE_HEIGHT` 从 `150` 增加到 `190`。
    - 有 ControlItem 时 Root 高度再增加固定 `ROOT_CONTROL_AREA_HEIGHT`。
    - 变量框宽度估算改为 `typeWidth + max(nameWidth, valueWidth)`，匹配两排布局。
    - `filterVisibleVariables()` 改为 `isInternalVariableName()`，过滤：
      - `__` 开头变量。
      - 尾段 `__` 开头变量。
      - 包含 `__` 隐藏标记的带节点前缀变量，如 `Node-...__iteration`。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - 变量框 grid 显式改为两列两行。
    - `.var-type` 固定在左列并跨两行。
    - `.var-name` 固定在右列第一行。
    - `.var-value` 固定在右列第二行并右对齐。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts`、`GraphCanvas.vue`、`RootNodeView.vue` 无错误。
  - `npm run build` 成功；仍有既有 Vite/Rollup warning。

### [frontend/graph] 固定 Root X=0 基准并修复 Root 逻辑保存覆盖
- 用户反馈：
  - Root 从 None 切换到有代码后会多出 ControlItem 变量，此时 Root 和视图都会移动。
  - 希望 Root 占据的位置作为居中 `X=0` 的定位基准，变量/节点变化时视图尽量不变化。
  - Root 编程后有较大概率没有成功写入 Project，需要检查是否被近期调试/同步代码影响。
- 根因：
  - 固定布局中 `centerX` 仍由 `contentWidth` 推导；变量行或节点行宽度变化会导致 Root 位置重新计算。
  - `GraphCanvas.vue` 在加载和新增节点后调用 `fitView()`，会主动移动当前视图。
  - Root 选择逻辑只调用 `/api/root/configure`；后端会写 `ProjectStore`，但前端 `projectStore.rootLogicName` 仍是旧值。之后变量顺序/节点顺序等前端保存会调用 `updateProject()`，可能用旧 `rootLogicName` 覆盖后端刚写入的配置。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - 新增 `ROOT_CENTER_X = 0`。
    - `computeFixedGraphLayout()` 使用固定 `centerX = ROOT_CENTER_X`。
    - 外部变量行取消左边界钳制，改为围绕 Root 中心展开。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - 使用 `setViewport()` 替代 `fitView()`。
    - 移除 `logFixedLayout()` 及加载/新增节点时的调试日志调用。
    - 首次加载时只把视口对准 Root；后续新增节点和变量变化不再主动移动视图。
  - `3rd/CoralinkerHost/ClientApp/src/stores/project.ts`：
    - 新增 `setRootLogicName(logicName)`，用于同步前端 Project 状态。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/RootNodeView.vue`：
    - Root 逻辑选择成功后调用 `projectStore.setRootLogicName(logicName)`。
    - 随后调用 `projectStore.saveProject({ silent: true })`，确保 Project 状态和后端配置一致。
- 验证：
  - 第一次 `npm run build` 发现 `flowSizes` 已无用，已删除。
  - `ReadLints` 检查 `variableFlowLayout.ts`、`GraphCanvas.vue`、`RootNodeView.vue`、`project.ts` 无错误。
  - `npm run build` 成功；仍有既有 Vite/Rollup warning。

### [frontend/graph] 收敛变量框样式、连线锚点并删除废弃连接点
- 用户反馈：
  - 变量框 name/value 都需要右对齐，宽度还偏宽。
  - 变量框类型与 Table 不一致，Table 是 `f32/i32`，图上显示成 `float/int`。
  - 变量框上下入线：单线应在框中心，多线才均匀分布。
  - 变量流线条改成实线。
  - Root/Node 上旧的 `in/out`、蓝色 VueFlow 连接点已不需要，需要删除相关代码。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - 缩小 control / flow 变量框 min/max 宽度。
    - `formatType()` 改为与 Node Table 一致的短类型映射：`Int32 -> i32`、`Single -> f32` 等。
    - 变量框宽度估算降低 name 字符宽度和最大宽度。
    - 变量框 top/bottom 锚点改为按实际连线数量分布：单线居中，多线均匀分布。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - 去掉 `.flow-line` 的 `stroke-dasharray`，变量流线条改为实线。
    - 变量框 padding / column gap 调小。
    - `.var-name`、`.var-value` 显式右对齐。
    - 删除废弃的 `@connect` / `onConnect()` 和 `sourceHandle/targetHandle` 创建边逻辑。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/RootNodeView.vue`：
    - 删除 Root 上的 VueFlow `Handle`、`out ●` 标签和对应 CSS。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/CoralNodeView.vue`：
    - 删除普通 Node 左右 VueFlow `Handle`。
    - 删除废弃的 harness / handle / 连接高亮 CSS。
- 验证：
  - `rg` 检查 `Handle`、`handle-in/out`、`node-handle`、`sourceHandle/targetHandle` 等无真实残留。
  - `ReadLints` 检查 `variableFlowLayout.ts`、`GraphCanvas.vue`、`RootNodeView.vue`、`CoralNodeView.vue` 无错误。
  - `npm run build` 成功；仍有既有 Vite/Rollup warning。

### [frontend/sdk] 按实际变量读写关系绘制 VarFlow
- 用户反馈：
  - 箭头也不要半透明，需要直接实心/不透明。
  - 当前变量流仍像是所有变量默认全连接到所有 Node 和 Root。
  - 实际上有些 Node 不读写、不持有某些变量；Root 也不是所有变量都持有/读写。
  - `sourceIds` 应按 className，而不是 UUID。
- 根因：
  - `variableFlowLayout.ts` 的 `routeVariableLines()` 使用 `Object.values(nodeRects)`，每个变量按方向连到所有 Node。
  - SDK 现有 `CartFieldMeta` / `CartFieldValue` 只提供合并后的字段方向，没有暴露每个变量由哪些 class 声明、哪些 class 读、哪些 class 写。
- 实施：
  - `3rd/CoralinkerSDK/DIVERSession.cs`：
    - `CartFieldMeta` / `CartFieldValue` 增加：
      - `SourceIds`
      - `ReaderIds`
      - `WriterIds`
    - 新增 `FieldFlowRelation` / `FieldFlowRelationBuilder`。
    - 新增 `BuildFieldFlowRelationMap()`，按 className 聚合变量关系：
      - MCU 节点使用 `entry.LogicName` 作为 sourceId，fallback 到 `NodeName`。
      - Root 虚拟节点从 `Root:<className>` 中剥离 className。
      - MCU `UpperIO` => reader。
      - MCU `LowerIO` => writer。
      - MCU `Mutual` => reader + writer。
      - Root cart `upper` => writer。
      - Root cart `lower` => reader。
      - Root cart `mutual` => reader + writer。
    - `/api/variables/meta` 和 `/api/variables` 通过原记录自动输出这些关系字段。
  - `3rd/CoralinkerHost/Services/VariableInspectorPushService.cs`：
    - SignalR 变量快照增加 `sourceIds`、`readerIds`、`writerIds`。
  - `3rd/CoralinkerHost/ClientApp/src/types/index.ts`：
    - `CartFieldMeta`、`CartFieldValue`、`VariableValue` 增加可选 `sourceIds`、`readerIds`、`writerIds`。
  - `3rd/CoralinkerHost/ClientApp/src/stores/runtime.ts`：
    - HTTP 刷新和 SignalR 更新变量时保存关系字段。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - 变量流线条 `opacity` 改为 `1`。
    - 节点传入 `sourceId = logicName`。
    - Root 传入 `rootSourceIds = [projectStore.rootLogicName]`。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - `FixedGraphNode` / `VariableFlowItem` 增加 source/read/write 关系字段。
    - `routeVariableLines()` 改为按 reader/writer className 匹配 Root 和具体 Node。
    - 保留旧快照兼容兜底：如果暂时没有 reader/writer，就使用 `sourceIds` 建立声明关系。
- 验证：
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Debug` 成功，0 error，仍有既有 warning。
  - `ReadLints` 检查相关前端文件无错误。
  - `npm run build` 成功；仍有既有 Vite/Rollup warning。

### [host/frontend] 修复开发模式 Build NuGet 源并增加错误弹窗
- 用户反馈：
  - 本地开发目录启动 Host 后调用 Build 报错：`Missing offline NuGet packages directory: ...\DiverCompilerPortable\bin\Debug\netstandard2.0\nuget-packages`。
  - 前端遇到 Build 错误时只在 Console/短通知显示，用户容易看不到，需要弹窗。
  - 验证时如果 Host 正在运行导致输出文件被锁，应停下来提示用户，不再绕临时输出目录。
- 根因：
  - 开发模式下 `HostRuntimePaths.CompilerResourcesDir` 指向 `DiverCompilerPortable\bin\Debug\netstandard2.0`，该目录有 weaver/source resources，但没有发布包才包含的 `nuget-packages`。
  - `DiverBuildService` 原先强制使用 `compilerDir\nuget-packages` 作为唯一离线 NuGet 源。
  - `HomeView.vue` 的 Build 失败处理只调用 `uiStore.error()`，缺少持久可见的错误对话框。
- 实施：
  - `3rd/CoralinkerHost/Services/DiverBuildService.cs`：
    - 新增 `ResolveNuGetPackagesDir()`。
    - 优先使用 `compilerDir\nuget-packages`。
    - Published layout 缺少离线包目录时仍报错，提示重新 publish Host。
    - Development layout fallback 到：
      - `CORALINKER_NUGET_PACKAGES_DIR`
      - `NUGET_PACKAGES`
      - 用户目录 `.nuget\packages`
    - Build 日志增加实际 NuGet package source 输出。
    - 清理一个未使用异常变量 warning。
  - `3rd/CoralinkerHost/ClientApp/src/views/HomeView.vue`：
    - 新增 `showBuildErrorDialog` / `buildErrorMessage`。
    - Build 返回失败或 catch 到 `/api/build` 异常时打开 `Build Failed` modal。
    - modal 显示完整错误文本，并提供 `Open Build Log` 按钮。
    - 保留 toast，并将错误追加到 Build 日志。
- 验证：
  - `ReadLints` 检查 `DiverBuildService.cs`、`HomeView.vue` 无错误。
  - `npm run build` 成功；仍有既有 Vite/Rollup warning。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Debug` 在 Host 被用户停止后直接编译成功，0 error，仍有既有 nullable/field warning。

### [frontend/graph] 修正 VarFlow 配色和无消费者连线
- 用户反馈：
  - Graph 中变量框/连线配色与最初规则不一致，应以 Variables 表格/图例为准。
  - 只有 Root 时，`left_diff_speed` 应只有进入 Root 的线；如果没有消费者，不应画出去的线。
- 根因：
  - `GraphCanvas.vue` 中 VarFlow 使用了另一套颜色：`upper=蓝`、`lower=绿`、`mutual=黄`、`control=紫`，与 `VariablePanel.vue` 的 `UpperIO=绿`、`LowerIO=橙`、`MutualIO=紫`、`ControlItem=蓝` 不一致。
  - `variableFlowLayout.ts` 的 `relationIds()` 在 `readerIds` / `writerIds` 为空时无条件 fallback 到 `sourceIds`，导致声明者被同时当成 reader/writer，Root-only 场景会产生多余反向线。
- 实施：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`：
    - `.flow-line.upper-io` 改为绿色。
    - `.flow-line.lower-io` 改为橙色。
    - `.flow-line.mutual-io` 改为紫色。
    - `.variable-flow-item.upper/lower/mutual/control-io` 同步到表格/图例配色。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`：
    - `relationIds()` 不再使用 `sourceIds` 兜底 reader/writer。
    - 空 reader/writer 表示没有对应关系，路由时不会画多余生产者/消费者线。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue`、`variableFlowLayout.ts` 无错误。
  - `npm run build` 成功；仍有既有 Vite/Rollup warning。

### [architecture] 调查模拟节点 / 虚拟 MCU 节点方案
- 用户需求：
  - Host 目前只能添加真实节点，没有实体节点时无法调试和演示代码执行。
  - 需要支持虚拟节点：
    - 32 个 Input 和 32 个 Output，内部一一连接。
    - 2 个虚拟 RS485 接口互相连接。
    - 1 个虚拟 RS232 接口 TX/RX 自环。
    - 1 个虚拟 CAN 接口，发出的消息会被收回来。
    - 节点代码虚拟机使用 `MCURuntime` 执行。
- 调查：
  - `3rd/CoralinkerHost/Web/ApiRoutes.cs`：
    - `/api/node/probe`、`/api/node/add` 都以 `mcuUri` 为入口，调用 `RuntimeSessionService`。
  - `3rd/CoralinkerHost/Services/RuntimeSessionService.cs`：
    - 只是包装 `DIVERSession`，负责 terminal 日志广播。
  - `3rd/CoralinkerSDK/DIVERSession.cs`：
    - `AddNode()` 当前固定 `ProbeNode(mcuUri)`，真实探测成功后创建 `NodeEntry`。
    - `StartNode()` 当前硬编码 `new MCUNode(entry.UUID, entry.McuUri)`。
    - 后台循环通过 `entry.Handle.RefreshState()`、`RefreshStats()`、`SendUpperIO()` 和 LowerIO 回调驱动运行。
  - `3rd/CoralinkerSDK/MCUNode.cs`：
    - 封装 `MCUSerialBridge`，公共面集中，适合抽象成运行时节点接口。
    - 关键能力：`Connect/Configure/Program/Start/Stop/SendUpperIO/SetWireTap/RegisterSerialPortCallback/RegisterCANPortCallback/RefreshState/RefreshStats`。
  - `MCURuntime/mcu_runtime.h` / `MCURuntime/mcu_runtime.c`：
    - 已提供 Host-side VM API：`vm_set_program`、`vm_run`、`vm_put_upper_memory`、`vm_get_lower_memory`、`vm_put_snapshot_buffer`、`vm_put_stream_buffer`、`vm_put_event_buffer`。
    - PC 版通过 host callbacks 输出硬件 IO、串口/CAN、错误和 console。
    - 当前 runtime 大量使用全局变量，需特别关注多模拟节点实例隔离。
  - `MCUSerialBridge/wrapper/MCUSerialBridgeCLR.cs`：
    - `LayoutInfo` 可表达 32 DI/32 DO 和最多 16 个端口，模拟节点布局可直接复用。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/AddNodeDialog.vue`：
    - UI 当前只有 COM Port / VIDPID 真实节点添加路径，需要新增模拟节点分支。
- 倾向方案：
  - 不在串口协议层伪造虚拟 COM 或虚拟 `MCUSerialBridge`。
  - 在 SDK 层新增节点运行时抽象，例如 `IRuntimeNode` / `INodeRuntimeHandle`。
  - 真实节点由现有 `MCUNode` 实现接口；模拟节点新增 `SimulatedMcuNode` 实现同一接口。
  - `DIVERSession.NodeEntry` 记录节点类型或 `McuUri` scheme（如 `sim://default`），`StartNode()` 根据类型创建对应 handle。
  - `SimulatedMcuNode` 内部通过 `MCURuntimeNative` P/Invoke wrapper 运行 `MCURuntime`：
    - `Program()` 调 `vm_set_program`。
    - `Start()` 启动循环任务，周期性 `vm_put_snapshot_buffer`、`vm_put_upper_memory`、`vm_run`、读取 `vm_get_lower_memory` 并触发 LowerIO 回调。
    - `write_stream` / `write_event` callback 中做 RS485/RS232/CAN loopback，并触发 WireTap callbacks。
  - 前端新增 “Add Simulated Node” 或 Add Node Dialog 中的 `Simulation` 模式，调用专用 API 添加，不走真实 probe。
- 风险/约束：
  - `MCURuntime` 当前 VS 工程只有 x64 Debug 配置是 DynamicLibrary；若要进入 Host portable package，需要新增 Windows/Linux x64/Linux ARM64 native runtime 构建和打包。
  - `mcu_runtime.c` 当前 PC 版全局状态不适合多模拟节点并发；需要重构为 per-instance runtime context，或短期只支持单模拟节点/运行时进程隔离。
  - IO loopback 语义要与真实硬件/MCU runtime 的 `write_snapshot/write_stream/write_event` 时序一致，否则 demo 行为可能和真实节点偏差。

### [architecture] 修订模拟节点快速方案：多进程隔离和发布打包
- 用户确认：
  - 快速方案要支持多个模拟节点。
  - 每个模拟节点使用不同进程隔离，避免 `MCURuntime` 全局状态互相污染。
  - 需要随 Host 发布，补动态库构建。
  - Wrapper 由实现方案处理。
- 补充调查：
  - `MCURuntime/mcu_runtime.c` 当前 PC 版导出主要是旧 Debug harness：
    - `set_lowerio_cb`
    - `set_error_report_cb`
    - `put_upper`
    - `test`
  - `test()` 固定执行测试循环，不能作为 Host 模拟节点的长期运行 API。
  - `write_stream` / `write_event` / `print_line` 当前 PC 版没有可供 Host 捕获的完整 callback 导出。
  - `MCUSerialBridge/build-native.ps1` 已经具备三平台 native 构建模式：
    - Windows 通过 `scons`。
    - Linux x64 / ARM64 通过 `zig cc -target ...` 交叉编译。
  - `CoralinkerHost.csproj` 和 `CoralinkerSDK.csproj` 当前只收集 `mcu_serial_bridge` native assets，需要加入 `mcu_runtime`。
- 最新倾向方案：
  - 新增 simulator 子进程程序，例如 `Coralinker.SimNodeHost`。
  - 每个模拟节点启动一个独立 `Coralinker.SimNodeHost` 进程。
  - `Coralinker.SimNodeHost` 独占加载 `mcu_runtime` 动态库，因此可以继续容忍 `mcu_runtime.c` 的全局状态。
  - SDK 侧新增 `SimulatedMcuNode`，实现和真实 `MCUNode` 相同的运行时接口。
  - `SimulatedMcuNode` 不直接调用 native VM，而是管理子进程 + IPC。
  - IPC 推荐 NDJSON over stdin/stdout，简单、跨平台、便于日志定位；如后续性能不足再换 named pipe。
  - IPC 命令：
    - `configure`
    - `program`
    - `start`
    - `stop`
    - `upperIO`
    - `setWireTap`
    - `state`
    - `stats`
  - IPC 事件：
    - `lowerIO`
    - `console`
    - `fatal`
    - `wiretapSerial`
    - `wiretapCan`
  - `MCURuntime` native API 需要补通用导出，不再使用 `test()`：
    - `sim_load_program` / `vm_set_program` 包装
    - `sim_step` / `vm_run` 包装
    - `sim_put_upper`
    - `sim_put_snapshot`
    - `sim_put_stream`
    - `sim_put_event`
    - `sim_get_lower`
    - callback 注册：lower、console、error、stream、event、snapshot
  - 发布打包：
    - 新增 `MCURuntime/build-native.ps1`，或将 MCURuntime 纳入统一 native build。
    - 产物放入：
      - `runtimes/win-x64/native/mcu_runtime.dll`
      - `runtimes/linux-x64/native/libmcu_runtime.so`
      - `runtimes/linux-arm64/native/libmcu_runtime.so`
    - `publish-host.ps1` manifest、startup integrity check、csproj native asset item 都要同步增加。

### [docs] 记录 MCURuntime 原始 VM 调试链路
- 用户在模拟节点实现中途指出需要先明确 `MCURuntime` 原本如何实现、同事如何调试，并要求不能破坏原来的调试体验。
- 调查确认：
  - `MCURuntime/mcu_runtime.c` 是 VM 内核，核心执行入口为 `vm_set_program(...)` 和 `vm_run(iteration)`。
  - 原始 PC 调试路径主要通过 `DiverTest`：
    - `DiverTest/DiverTest.csproj` PreBuild 重建 `DiverCompiler`。
    - 执行 `DiverCompiler.exe -g` 生成 VM 相关文件。
    - 执行 `DiverTest/build_cpp.bat`，使用 VS `vcvars64.bat` + MSVC `cl /LD /MDd /Zi /DEBUG` 编译 `MCURuntime.dll`。
    - `DiverTest/DIVER/DIVERInterface.cs` 通过 P/Invoke 调 `set_lowerio_cb`、`set_error_report_cb`、`put_upper`、`test`。
  - `test()` 是 legacy debug harness：加载 program，循环执行若干次 `vm_run(i)`，注入 snapshot/event，并把 lower IO callback 回传给 C#。
- 新增 `MCURuntime/DEBUGGING.md`：
  - 说明 VM 本体 API、`DiverTest` 调试流程、Visual Studio/PDB native 断点路径。
  - 明确 Host 模拟节点新增的 `sim_*` 导出仅服务运行时模拟，不替代原来的 `test()` 调试入口。
  - 明确必须继续保留 `test`、`put_upper`、`set_lowerio_cb`、`set_error_report_cb`，以及 `DiverTest/build_cpp.bat` 的 MSVC Debug DLL 路径。
- 更新 `MCURuntime/MCURuntime.vcxproj`，将 `DEBUGGING.md` 加入 VS 项目文件列表，方便后续开发者在 VS 中直接查看。

### [feature] 完成模拟节点快速方案骨架和打包
- 继续实现用户确认的快速方案：多个模拟节点，每个节点独立子进程隔离，随 Host 发布。
- SDK 层：
  - 新增 `3rd/CoralinkerSDK/IRuntimeNode.cs`。
  - `MCUNode` 显式实现 `IRuntimeNode`，保留原 public API，不破坏真实节点调用方式。
  - `DIVERSession.NodeEntry.Handle` 从 `MCUNode?` 改为 `IRuntimeNode?`。
  - `DIVERSession` 新增 `CreateRuntimeNode()`，根据 `sim://` scheme 创建 `SimulatedMcuNode`，否则创建真实 `MCUNode`。
  - 新增 `DIVERSession.AddSimulatedNode()`，生成唯一 `sim://{uuid}`，默认 layout 为 32 DI / 32 DO / RS485-A / RS485-B / RS232 / CAN。
  - 新增 `3rd/CoralinkerSDK/SimulatedMcuNode.cs`：
    - 负责启动 `CoralinkerSimNodeHost` 子进程。
    - 使用 stdin/stdout NDJSON 发送 `hello/configure/program/start/stop/upper/wiretap/shutdown`。
    - 处理 `lower/console/error/wire` 事件，转发到 DIVERSession 现有 LowerIO、Console、WireTap 流程。
- SimNodeHost：
  - 新增 `3rd/CoralinkerSimNodeHost/CoralinkerSimNodeHost.csproj`、`Program.cs`、`McuRuntimeNative.cs`、`NativeMcuRuntimeResolver.cs`。
  - 子进程内部通过 P/Invoke 加载 `mcu_runtime`。
  - native 加载延后到 `program` / runtime 调用阶段，便于 add/probe 时给出明确错误。
  - 实现端口回环：
    - port 0/1：RS485-A 与 RS485-B 互连。
    - port 2：RS232 TX/RX 自环。
    - port 3：CAN 自环。
  - wire 事件统一输出到父进程，父进程再调用已有 Serial/CAN callback。
- MCURuntime：
  - `mcu_runtime.c` 新增 `SIM_EXPORT`，支持 Windows `__declspec(dllexport)` 和 Linux visibility。
  - 新增模拟节点运行 API：
    - `sim_set_callbacks`
    - `sim_load_program`
    - `sim_put_upper`
    - `sim_put_port_input`
    - `sim_step`
    - `sim_destroy`
  - 保留 legacy debug exports：
    - `test`
    - `put_upper`
    - `set_lowerio_cb`
    - `set_error_report_cb`
  - PC host/debug 函数从仅 `_DEBUG` 改为所有非 MCU 构建可用，确保 Release native 包也导出 `sim_*`。
  - `write_snapshot` 更新虚拟 snapshot，`sim_step` 在 `vm_run` 前喂回 snapshot，用于 DO→DI 回环并避免 VM snapshot 断言。
  - 修复 Zig/clang 交叉编译不兼容项：
    - 删除 release 分支误留的 `VAL_OUT(ptr);`。
    - 将 `auto` 改成显式 `int`。
    - 将非标准 `itoa` 改为 `snprintf`。
    - 将 `heap_obj[0].pointer = -1` 改为显式指针转换。
- Host/API/UI：
  - `RuntimeSessionService` 新增 `AddSimulatedNodeAsync()`。
  - `ApiRoutes` 新增 `/api/node/add-simulated`，并让 add 返回 `mcuUri`。
  - 前端 `device.ts` 新增 `addSimulatedNode()`。
  - `AddNodeDialog.vue` 新增 `Simulated` connection mode，支持可选 node name，不显示固件 Upgrade。
  - `types/index.ts` 的 `AddNodeResult` 增加 `mcuUri`。
- 构建/发布：
  - 新增 `MCURuntime/build-native.ps1`，输出三平台 native runtime assets。
  - `CoralinkerHost.csproj`：
    - 收集 `MCURuntime/build/runtimes/*/native` 下的 `mcu_runtime`。
    - 构建并拷贝 `CoralinkerSimNodeHost` 到 Host 输出的 `simnode/`。
    - 发布时把 `simnode/` 带入 publish output。
  - `publish-host.ps1` 同时执行 `MCUSerialBridge/build-native.ps1` 与 `MCURuntime/build-native.ps1`。
  - `DIVER.sln` 加入 `CoralinkerSimNodeHost` 项目。
- 验证：
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Debug` 成功。
  - `npm run build` 成功。
  - `powershell -File MCURuntime\build-native.ps1 -Target windows -Configuration Debug` 成功。
  - `powershell -File MCURuntime\build-native.ps1 -Target linux-x64 -Configuration Release` 成功。
  - `powershell -File MCURuntime\build-native.ps1 -Target linux-arm64 -Configuration Release` 成功。
  - 确认 Host Debug 输出包含 `simnode/CoralinkerSimNodeHost.dll` 和 `runtimes/win-x64/native/mcu_runtime.dll`。
  - `ReadLints` 无新增错误。
  - `git diff --check` 无 whitespace 错误，仅输出 Windows 换行提示。
- 待后续手动联调：
  - 在 UI 中 Add Node -> Simulated，选择 Logic，Build/Program/Start。
  - 检查 console 输出、LowerIO 更新、32 路 DO→DI 回环、RS485/RS232/CAN wiretap 回环。
  - 在 Linux x64/ARM64 目标机实际启动发布包，验证 native resolver 和子进程路径。

### [publish] 生成包含模拟节点的 CoralinkerHost 发布包
- 按用户要求直接编译一个发布包用于查看。
- 执行：
  - `powershell -NoProfile -ExecutionPolicy Bypass -File "publish-host.ps1" -Configuration Release`
  - 工作目录：`3rd/CoralinkerHost`
- 发布脚本成功完成：
  - 构建 `MCUSerialBridge` native runtime assets：
    - `win-x64`
    - `linux-x64`
    - `linux-arm64`
  - 构建 `MCURuntime` native runtime assets：
    - `win-x64`
    - `linux-x64`
    - `linux-arm64`
  - 构建前端 `ClientApp`。
  - 执行 `dotnet publish`。
  - 收集离线 NuGet 包。
  - 生成 `package-manifest.sha256`。
- 输出目录：
  - `3rd/CoralinkerHost/Publish/CoralinkerHost_4eeb413_20260603-101633`
- 已确认关键文件存在：
  - `simnode/CoralinkerSimNodeHost.dll`
  - `runtimes/win-x64/native/mcu_runtime.dll`
  - `runtimes/linux-x64/native/libmcu_runtime.so`
  - `runtimes/linux-arm64/native/libmcu_runtime.so`
  - `package-manifest.sha256`
  - `publish-info.json`
- 过程说明：
  - 发布输出仍有既有 C# nullable / Windows-only API warning。
  - 前端仍有既有 Vite/Rollup dynamic import/chunk size warning。
  - 发布命令退出码为 0。
  - 一次用于确认文件的 PowerShell 命令因变量被外层 shell 展开而失败并挂起，已用 `Stop-Process` 结束；随后使用字面量路径确认关键文件均存在。

### [ui] 调整模拟节点添加和显示样式
- 用户反馈：
  - Add Simulated Node 时 `URI Preview` 不需要显示。
  - 添加后的虚拟节点希望有明显不同颜色，使用橙色底色。
  - 前端判断直接基于 URL 是否以 `sim://` 开头，不需要额外 sim 字段。
  - 询问多个 sim 节点 URL 是否一样，是否会导致通讯错位。
- 实现：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/AddNodeDialog.vue`
    - `URI Preview` 仅在非 `simulated` 模式显示。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/CoralNodeView.vue`
    - 新增 `isSimulatedNode` computed：`mcuUri.toLowerCase().startsWith('sim://')`。
    - 根节点 class 增加 `simulated`。
    - 模拟节点使用橙色渐变背景、橙色边框和橙色 selected box-shadow。
    - 模拟节点隐藏固件升级按钮。
    - URI 区域中模拟节点显示 `Virtual`，不再显示 `Not Set`。
- 设计确认：
  - 后端 `DIVERSession.AddSimulatedNode()` 每次生成 `sim://{uuid}`，所以多个模拟节点 URL 不一样。
  - 每个 `SimulatedMcuNode` 都启动独立 `CoralinkerSimNodeHost` 子进程，IPC 由该节点 wrapper 管理，不会因为 URL 相同而通讯错位。
- 验证：
  - `ReadLints` 检查 `AddNodeDialog.vue`、`CoralNodeView.vue` 无错误。
  - `npm run build` 成功。
  - `git diff --check` 无 whitespace 错误，仅有 Windows 换行提示。

### [sim-node] 修复模拟节点节拍、IO/WireTap 统计并迁移 native shim
- 用户反馈：
  - 模拟节点执行过快，`scanInterval=100ms` 就应 100ms 执行一次，其余时间睡眠。
  - 串口收发、写 IO 网页上看不到状态/日志。
  - `mcu_runtime.c` 不应承载大量模拟节点 `sim_*` 函数和状态；模拟逻辑应移到 SimNode 工程。
  - `MCURuntime/build-native.ps1` 不合适，因为编译产物属于 sim node runtime，应放在 SimNode 工程。
- 节拍修复：
  - `3rd/CoralinkerSimNodeHost/Program.cs`
    - `ProgramRuntime()` 保存 `sim_load_program()` 返回的 `interval` 到 `_scanIntervalMs`。
    - `StartLoop()` 改为 `Stopwatch` + `Task.Delay` 调度。
    - 每次 loop 调 `McuRuntimeNative.Step(timestampMs)`，不再固定每 10ms 跑一次。
  - `3rd/CoralinkerSimNodeHost/McuRuntimeNative.cs`
    - `Step` 签名从 `int iterations` 改为 `uint timestampMs`。
- IO / WireTap 统计修复：
  - `3rd/CoralinkerSimNodeHost/Program.cs`
    - snapshot callback 改为发送 `snapshot` 事件。
    - 串口/CAN 回环仍发送 `wire` 事件并写回 native input queue。
  - `3rd/CoralinkerSDK/SimulatedMcuNode.cs`
    - 新增 `HandleSnapshotEvent()`：
      - 读取 snapshot 前 4 字节为 bit mask。
      - 同步更新 `_stats.DigitalOutputs` 与 `_stats.DigitalInputs`，实现虚拟 DO→DI 网页显示。
    - 新增 `UpdatePortStats()`：
      - 每个 wire 事件更新对应端口的 `TxFrames/RxFrames/TxBytes/RxBytes`。
    - 新增 `ShouldEmitWireTap()`：
      - 只有端口开启相应 TX/RX WireTap 时才向 Host 日志聚合发事件。
    - `SetWireTap(0xFF, flags)` 会写入 0..15 端口，避免“全端口启用”对模拟节点无效。
- native shim 迁移：
  - 新增 `3rd/CoralinkerSimNodeHost/native/sim_node_runtime.c`：
    - 承载 `sim_set_callbacks`、`sim_load_program`、`sim_put_upper`、`sim_put_port_input`、`sim_step`、`sim_destroy`。
    - 承载模拟节点专用状态：VM memory、snapshot input、callback、模拟时间。
    - 实现 PC host-side hooks：`write_snapshot`、`write_stream`、`write_event`、`print_line`、`report_error`、`get_cyclic_*`。
  - `MCURuntime/mcu_runtime.c`：
    - 删除 `sim_*` 和模拟状态。
    - 仅保留 `_DEBUG && !IS_MCU && !SIM_NODE_HOST` 的 legacy debug harness：
      - `test`
      - `put_upper`
      - `set_lowerio_cb`
      - `set_error_report_cb`
    - 保留此前少量非行为性 C 兼容修正。
  - 删除 `MCURuntime/build-native.ps1`。
  - 新增 `3rd/CoralinkerSimNodeHost/build-native.ps1`：
    - 编译 `MCURuntime/mcu_runtime.c` + `SimNodeHost/native/sim_node_runtime.c`。
    - 输出到 `3rd/CoralinkerSimNodeHost/build/runtimes/.../native`。
  - `McuRuntimeNative.cs` / `NativeMcuRuntimeResolver.cs` 改为解析 `sim_node_runtime`：
    - Windows: `sim_node_runtime.dll`
    - Linux: `libsim_node_runtime.so`
  - `CoralinkerHost.csproj`：
    - native 资产改为从 `CoralinkerSimNodeHost/build/runtimes` 收集。
    - 发布路径改为 `simnode/runtimes/{rid}/native/...`。
  - `publish-host.ps1`：
    - native build 列表改为 `MCUSerialBridge` + `CoralinkerSimNodeHost`。
    - 发布元数据字段改为 `nativeSimNodeRuntimes`。
- 验证：
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj -c Debug` 成功。
  - `powershell -File 3rd\CoralinkerSimNodeHost\build-native.ps1 -Target windows -Configuration Debug` 成功。
  - `powershell -File 3rd\CoralinkerSimNodeHost\build-native.ps1 -Target linux-x64 -Configuration Release` 成功。
  - `powershell -File 3rd\CoralinkerSimNodeHost\build-native.ps1 -Target linux-arm64 -Configuration Release` 成功。
  - `powershell -File 3rd\CoralinkerHost\publish-host.ps1 -Configuration Release` 成功。
  - 新 publish 输出：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_4eeb413_20260603-105422`
  - 已确认 publish 包包含：
    - `simnode/CoralinkerSimNodeHost.dll`
    - `simnode/runtimes/win-x64/native/sim_node_runtime.dll`
    - `simnode/runtimes/linux-x64/native/libsim_node_runtime.so`
    - `simnode/runtimes/linux-arm64/native/libsim_node_runtime.so`
    - `package-manifest.sha256`
  - 已确认 publish 包不再包含旧的根目录 `runtimes/win-x64/native/mcu_runtime.dll`。
  - `git diff --check` 无 whitespace 错误，仅有 Windows 换行提示。
- 待用户实测确认：
  - UI 中模拟节点的 `scanInterval` 是否按 100ms / 配置值稳定执行。
  - Digital IO 灯是否跟 `RunOnMCU.WriteSnapshot` 同步。
  - WireTap 日志和端口 TX/RX frame/byte 统计是否按 TX/RX 方向递增。

### [varflow-sim-stats] 完成相邻节点 gap 布局与模拟节点 TX/RX 统计显示
- 用户反馈：
  - VarFlow 仍需处理“有消费者但无生产者”的变量，应放到消费节点左侧并撑开左侧空隙。
  - 左节点只发布变量、右节点只接收变量时，应共用同一个节点间空隙。
  - 相邻节点间通信变量，只要 Root 不参与，无论左发右收还是右发左收，都应放入两节点中间。
  - 需要继续修复模拟节点 TX/RX 统计数量显示。
- VarFlow 实现：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`
    - 用 `gapVariableGroups` 替代未完成的 `sideVariableGroups`。
    - gap `-1` 表示第一个节点左侧；gap `i` 表示节点 `i` 与节点 `i+1` 中间；gap `last` 表示最后一个节点右侧。
    - 分类规则：
      - 无消费者、单生产者：放到生产节点右侧 gap。
      - 无生产者、单消费者：放到消费节点左侧 gap。
      - 单生产者 + 单消费者且二者相邻、Root 不参与：放到二者中间 gap。
      - Root 参与读写的变量继续放 Root 下方横排。
    - gap 中变量纵向排列，间距按该 gap 最大变量框宽度动态撑开，同一 gap 只撑一次。
    - 节点宽度保持 `380`。
- TX/RX 统计修复：
  - `3rd/CoralinkerSDK/DIVERSession.cs`
    - `BuildNodeStateSnapshot()` 在生成节点状态快照前对运行中节点调用 `RefreshStats()` 并更新 `entry.Stats`，避免 SignalR 状态推送拿到旧统计。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/PortStatsView.vue`
    - 端口统计 UI 显示 `TX frames/bytes`、`RX frames/bytes`，不再只显示 frame 数。
- 验证：
  - `ReadLints` 检查：
    - `DIVERSession.cs`
    - `PortStatsView.vue`
    - `variableFlowLayout.ts`
    - 无 linter error。
  - `npm run build` 成功。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj` 成功。
  - 剩余 warning 为既有 Vite chunk/annotation warning 与既有 C# nullable / Windows-only API / unused event warning。

### [varflow-instance-routing] Root 右侧变量、实例级关系与侧边连线
- 用户反馈：
  - Root 的只发布变量也要从 Root 下方横排移到 Root 右侧。
  - 同一个 Logic/Class 可以被不同节点复用，不能只靠 Class 判断变量属于哪个节点，需要按节点实例判断。
  - 放在节点左/右侧的变量，连线应从节点左右侧走。
  - 如果侧边变量堆叠总高度不超过节点高度，侧边连线用直线；如果超过节点高度，在节点侧边按等距 slot 走原有弧线风格。
- 后端关系 ID 修复：
  - `3rd/CoralinkerSDK/DIVERSession.cs`
    - VarFlow 关系的 `SourceIds/ReaderIds/WriterIds` 不再使用 `LogicName` / ClassName。
    - 普通节点 source id 改为 `entry.UUID`。
    - 虚拟节点 source id 改为 `entry.SourceId`，Root runtime 对应 `root-runtime`。
    - 这样多个节点使用同一个 Logic 时，关系仍按节点实例分开。
- 前端匹配修复：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`
    - `FixedGraphNode.sourceId` 改为节点 `id`。
    - `rootSourceIds` 改为固定 `root-runtime`，不再使用 `rootLogicName`。
- VarFlow 布局/连线：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`
    - 新增 Root 右侧特殊 gap，用于 Root 只发布、无消费者变量。
    - 侧边 item 增加 placement/slot 元数据。
    - Root 右侧、节点左侧、节点右侧变量都从左右侧 anchor 连线。
    - 侧边堆叠高度不超过目标节点/Root 高度时使用直线。
    - 超过高度时按 `sideSlotIndex/sideSlotCount` 在侧边分配等距 slot，使用横向贝塞尔弧线。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts`、`GraphCanvas.vue`、`DIVERSession.cs` 无错误。
  - `npm run build` 成功。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj` 成功。
  - 剩余 warning 为既有构建 warning，无新增 error。

### [varflow-long-label-gap] 修复长变量名重叠并放宽 gap 自适应
- 用户反馈：
  - 超长变量名时变量控件内部发生重叠，类型标记和变量名挤在一起。
  - 这种情况下应先把变量控件布局修正确。
  - 放在节点侧边/节点间的长变量应让节点间空隙自适应变大。
  - 要检查之前 `DEV_LOG.md` 是否写入成功。
- 修复：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`
    - `FLOW_ITEM_MAX_WIDTH` 从 `260` 放宽到 `420`。
    - 变量名估算从 `name.length * 6.8` 放宽到 `name.length * 7.2`。
    - 变量名估算上限从 `150` 放宽到 `300`。
    - 节点间 gap 继续通过 `maxVariableWidth()` 推导，因此长变量会自动撑大 gap。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`
    - `.var-name/.var-value` 增加 `display: block`、`width: 100%`、`min-width: 0`、`max-width: 100%`。
    - 保留 `overflow: hidden`、`text-overflow: ellipsis`、`white-space: nowrap`，使长文本在可用列内正确省略，不再压到类型列。
- 日志检查：
  - 已确认此前 `varflow-sim-stats` 与 `varflow-instance-routing` 记录存在于 `DEV_LOG.md` 末尾。
  - 本条按增量方式追加。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts`、`GraphCanvas.vue` 无错误。
  - `npm run build` 成功。
  - 剩余 warning 为既有 Vite/Rollup chunk 与 SignalR annotation warning。

### [varflow-side-spacing] 收紧变量宽度估算并调整 Root 侧边排列
- 用户反馈：
  - 自动计算的变量占据长度偏大，类型和名字之间有无效空隙，应按实际控件宽度计算。
  - PC/Root 出来或进去的变量不能从 Root 侧面的上方开始排列，容易超过下方范围，应从 Root 下方开始排列。
  - 侧边弧线仍可能重叠，需要把节点和变量表之间的间隙调大。
- 修复：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`
    - `FLOW_ITEM_MAX_WIDTH` 从 `420` 收紧到 `320`。
    - 变量名宽度估算从 `name.length * 7.2` 调整为 `name.length * 6.2`。
    - 变量名估算上限从 `300` 调整为 `240`。
    - 变量框额外宽度从 `38` 调整为 `24`，减少类型列和名字列之间的空白。
    - Root 右侧变量起始 Y 改为 `rootRect.y + rootRect.height + SIDE_VARIABLE_STACK_GAP`，从 Root 下方开始排列。
    - `SIDE_VARIABLE_GAP` 从 `24` 增加到 `40`，拉开节点/Root 与侧边变量列之间距离，降低弧线重叠概率。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts` 无错误。
  - `npm run build` 成功。
  - 剩余 warning 为既有 Vite/Rollup chunk 与 SignalR annotation warning。

### [review-prune-stale-vars] 提交前 review 并改为清理 SDK 变量残留
- 用户反馈：
  - `DiverCompiler/Processor.cs` 中 `cart_io_list` 改动风险过高，可能影响 MCU runtime 对 program descriptor 的解析和执行。
  - C/D 残留更可能是新 `MetaJson` 已不含字段，但 `DIVERSession._variables` 全局存储仍保留旧值。
- 修复：
  - `DiverCompiler/Processor.cs`
    - 撤回 `cart_io_list` 过滤改动，恢复原有基于 CartDefinition 字段输出 IO 列表的行为。
  - `3rd/CoralinkerSDK/DIVERSession.cs`
    - 新增 `PruneUndeclaredVariables()`。
    - `ProgramNode()` 在新 `MetaJson` 解析并初始化变量后，清理不再被任何当前节点/虚拟节点声明的变量。
    - `RemoveNode()` 和 `UnregisterVirtualNode()` 后也调用清理，避免删除节点/Root 后残留。
- Review 注意事项：
  - `CORAL-NODE-V2.1` LaTeX 文件按用户要求忽略。
  - `3rd/CoralinkerSimNodeHost/` 为新工程，提交时应只加入源码、项目、build 脚本和 native shim；不要把 `build/runtimes/*` native 产物一起提交。
  - `MCURuntime/build/` 是未跟踪生成目录，不应提交；当前未发现实际文件。
  - `3rd/CoralinkerHost/CoralinkerHost - Backup.csproj` 被删除，提交前需确认这是有意清理备份文件。
- 验证：
  - `ReadLints` 检查 `DiverCompiler/Processor.cs`、`DIVERSession.cs` 无错误。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj` 成功。
  - `npm run build` 成功。
  - `git diff --check -- . ":(exclude)CORAL-NODE-V2.1/**"` 通过，仅有换行提示。

### [gitignore-simnode-build] 忽略 SimNode native build 输出
- 用户反馈：
  - `3rd/CoralinkerSimNodeHost/build/runtimes/*` 没有被 `.gitignore` 忽略。
- 根因：
  - 当前 `.gitignore` 有 `[Bb]uild[Ll]og.*` 和 `bld/`，但没有通用 `build/` 目录规则。
  - 新的 SimNode native 构建输出放在 `3rd/CoralinkerSimNodeHost/build/runtimes/...`，因此被 Git 视为未跟踪。
- 修复：
  - `.gitignore`
    - 新增 `/3rd/CoralinkerSimNodeHost/build/`。
    - 新增 `/MCURuntime/build/`。
    - 不影响 `build-native.ps1` 脚本提交。
- 验证：
  - `git check-ignore -v "3rd/CoralinkerSimNodeHost/build/runtimes/linux-arm64/native/libsim_node_runtime.so" "MCURuntime/build"` 命中新增规则。
  - `git status --short -- "3rd/CoralinkerSimNodeHost" "MCURuntime/build" ".gitignore"` 只显示 `.gitignore` 修改和 SimNode 源码目录未跟踪，native build 输出不再单独暴露。

### [compiler-cart-used-fields] 仅导出实际使用的 Cart IO 字段
- 用户反馈：
  - 第一轮代码使用 Cart 字段 A/B/C/D。
  - 后续 CartDefinition 仍声明 C/D，但 Operation 代码不再使用 C/D。
  - 重新编译后 C/D 仍出现在变量表和 VarFlow 中，并且还连到节点。
  - 未被任何代码使用的 Cart 字段不应存在。
- 根因：
  - `DiverCompiler/Processor.cs` 中 `SI.cart_io_list` 由 `SI.sfield_offset.field_offset` 的所有 CartDefinition 字段生成。
  - 这会把 CartDefinition 中仍声明但当前编译逻辑未引用的字段也写入 `.bin.json`。
  - Host 解析 `.bin.json` 后会把这些字段加入 `entry.CartFields`，再进入变量表与 VarFlow 关系。
- 修复：
  - `DiverCompiler/Processor.cs`
    - `SI.cart_io_list` 改为从 `SI.referenced_typefield` 中实际引用到的 Cart 字段生成。
    - 仍然从 `SI.sfield_offset.field_offset` 取 offset/type/flags，并按 offset 排序。
    - 使用显式 `new HashSet<string>(..., StringComparer.Ordinal)`，兼容当前目标框架。
- 验证：
  - 首次构建发现 `Enumerable.ToHashSet()` 在当前目标框架不可用，已改为显式 `HashSet`。
  - `ReadLints` 检查 `DiverCompiler/Processor.cs` 无错误。
  - `dotnet build 3rd\CoralinkerHost\CoralinkerHost.csproj` 成功。
  - 剩余 warning 为既有 `Processor.cs` unreachable code warning。
- 注意：
  - 该修复影响后续 Build 产物。
  - 已经使用旧 `.bin.json` 编程过的节点需要重新 Build 并 Program，未使用字段才会从变量表/VarFlow 消失。

### [varflow-node-edge-slots] 节点边缘出线按边均分
- 用户反馈：
  - 节点出线位置需要在对应边上均分，不能集中在一个位置。
- 修复：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`
    - `routeVariableLines()` 先扫描 center flow 变量线，统计 Root bottom 和每个节点 top 的实际出线 slot。
    - 新增 `rootSlotKey()`、`nodeSlotKey()`、`slotIndex()`，保证同一个节点/边按自己的线数量分配 anchor。
    - Root bottom 使用 Root 自己的 slotCount。
    - 普通节点 top 使用该节点自己的 slotCount。
    - 移除节点边缘锚点对全局 `itemIndex/items.length` 的依赖，避免局部线条集中。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts` 无错误。
  - `npm run build` 成功。
  - 剩余 warning 为既有 Vite/Rollup chunk 与 SignalR annotation warning。

### [varflow-side-bezier] 修复侧边贝塞尔曲线回折
- 用户反馈：
  - 侧边贝塞尔控制点不合理，短距离连接时曲线出现回折。
- 修复：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`
    - `sideBezierPath()` 不再使用固定最小 `54px` 控制点距离。
    - 控制点距离改为 `dx * 0.45`，并通过 `clamp()` 限制在端点水平距离内。
    - lane 偏移从 `lane * 8` 收敛为 `lane * 4`，并限制为 `dx * 0.12` 范围内，避免控制点越过端点造成折返。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts` 无错误。
  - `npm run build` 成功。
  - 剩余 warning 为既有 Vite/Rollup chunk 与 SignalR annotation warning。

### [varflow-root-side-upward] 修正 Root 右侧变量向上排列
- 用户反馈：
  - Root 右侧变量不应从 Root 下方继续向下排列。
  - 应贴 Root 右侧并向上排列。
  - 例如 5 个变量，第 5 个在最下面，然后向上依次为 4、3、2、1。
- 修复：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`
    - `layoutGapVariables()` 计算 Root 右侧变量栈高度 `stackHeight`，并传入 `gapVariableY()`。
    - `ROOT_RIGHT_GAP` 的 Y 起点改为 `rootRect.y + rootRect.height - stackHeight`。
    - 保持变量数组原有顺序从上到下绘制，因此视觉上底部是最后一个变量，向上依次递减。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts` 无错误。
  - `npm run build` 成功。
  - 剩余 warning 为既有 Vite/Rollup chunk 与 SignalR annotation warning。

### [planning-kit-host-agent-docs] 补强 Kit 文档内嵌 Host 与 Agent API 计划
- 用户反馈：
  - 客户侧 Agent 可能不够聪明，不能只提供 API 列表，需要写清系统理解和操作细节。
  - 需要考虑多节点编程、添加/删除节点、修改节点参数是否开放给 Agent。
  - 网页编译/编程/启动后会同步刷新状态，Agent 也需要明确刷新顺序。
  - Cursor/Agent 文件操作需要考虑全量同步、批量保存、带提交日志提交、冲突处理。
  - 最新工作记录已经完成模拟节点相关能力，需要纳入文档和 API 方案。
- 调查：
  - 重新读取 `DEV_LOG.md` 中模拟节点记录，确认当前事实：
    - 模拟节点通过 `sim://{uuid}` 添加。
    - 每个模拟节点由独立 `CoralinkerSimNodeHost` 子进程隔离运行。
    - 默认 layout 为 32 DI / 32 DO / RS485-A / RS485-B / RS232 / CAN。
    - Host 发布包已经纳入 `simnode/CoralinkerSimNodeHost.dll` 和 `simnode/runtimes/{rid}/native/sim_node_runtime`。
    - VarFlow 关系已经改为按节点实例 UUID / `root-runtime` 处理，避免同一 Logic 多节点复用时混淆。
- 计划更新：
  - 更新 Cursor plan `kit_host_docs_a545dcbd.plan.md`。
  - 增加“面向低能力 Agent 的写法”：
    - 系统地图。
    - 固定操作流程。
    - 成功字段、失败日志、禁止事项。
    - 可照抄 HTTP 请求示例和 JSON 字段说明。
  - 增加文件同步协议：
    - `GET /api/files/snapshot`。
    - `POST /api/files/sync`。
    - 多文件批量写入、全量校验、`baseHead`/hash 冲突判断、`commitMessage`。
    - 保留现有单文件接口供网页使用。
  - 增加真实节点和模拟节点统一生命周期：
    - Probe/Add 或 Add Simulated -> Configure -> Build -> Program -> Start -> Observe -> Stop -> Remove。
    - Agent 应可增删节点、修改节点参数、创建模拟节点，以复现网页能搭建的实验环境。
  - 增加多节点编程规则：
    - Build 返回多个 artifacts 时，按 `logicName` 明确选择。
    - 同一 Logic 可编程到多个节点，但 VarFlow/变量关系按节点实例 UUID 区分。
    - 编程前检查 layout/port configs 与代码端口使用是否匹配。
    - 编程、删除节点或更换程序后必须刷新 variables/meta/flow。
  - 增加编译/编程/启动后状态同步顺序，并建议新增 `/api/agent/state` 聚合接口，降低低能力 Agent 漏调用概率。
- 验证：
  - 本轮处于计划模式，仅更新计划和 Markdown 工作记录，未修改代码。

### [feature-kit-host-agent-docs] 实现 Kit 文档内嵌 Host 与 Agent API
- 用户要求：
  - 按已确认计划实现，不再修改 plan 文件。
  - 所有 todo 从第一项开始推进，直到全部完成。
- Host 文档服务：
  - 新增 `3rd/CoralinkerHost/Services/KitDocsService.cs`。
  - 新增路由：
    - `GET /api/docs/kit`
    - `GET /api/docs/kit/md`
    - `GET /api/docs/kit/md/{**path}`
    - `GET /docs/kit`
    - `GET /docs/kit/`
    - `GET /docs/kit/{**path}`
  - 开发态从 `3rd/CoralinkerKitDocs` 读取 Markdown。
  - 发布态支持从 `res/docs/kit/md` 和 `wwwroot/docs/kit` 读取。
  - Markdown 响应使用 `no-cache`，HTML 响应使用长期缓存并依赖版本 URL。
  - 响应包含 ETag 和 Last-Modified。
  - HTML 渲染时将站内 `.md` href 转为 `.html` href。
- Agent API：
  - 新增 `3rd/CoralinkerHost/Services/FileSyncService.cs`：
    - `GET /api/files/snapshot`
    - `POST /api/files/sync`
    - 支持 `baseHead`、逐文件 `baseHash`、批量 changes、`commitMessage`、`force`。
    - Agent 写入范围限制为 `assets/inputs/*.cs`，不允许写 generated。
  - 新增 `3rd/CoralinkerHost/Services/AgentStateService.cs`：
    - `GET /api/agent/capabilities`
    - `GET /api/agent/state`
    - `POST /api/agent/refresh`
    - 聚合 project/git/build/session/nodes/root/variables/logs/fatal error。
  - 新增 `3rd/CoralinkerHost/Services/FatalErrorStore.cs`：
    - `GET /api/errors/fatal`
    - `POST /api/errors/fatal/clear`
    - `RuntimeSessionService` 收到 `OnFatalError` 时记录最近错误快照。
  - 新增：
    - `GET /api/logs/build`
    - `GET /api/variables/flow`
- 发布脚本：
  - 更新 `3rd/CoralinkerHost/publish-host.ps1`。
  - 新增 Markdown -> HTML 生成逻辑。
  - 发布 Markdown 源文件到 `res/docs/kit/md`。
  - 发布 HTML 到 `wwwroot/docs/kit`。
  - 跳过 `_write_request`。
  - `publish-info.json` 增加 `kitDocs` 元数据。
  - 修复 PowerShell 反引号字符串导致的脚本解析错误：
    - 行内代码正则使用单引号模式。
    - 代码块 fence 检测使用单引号字面量。
- Kit 文档：
  - 新增内部写作记录：
    - `3rd/CoralinkerKitDocs/_write_request/2026-06-03-agent-host-docs.md`
  - 重写：
    - `3rd/CoralinkerKitDocs/README.md`
  - 新增：
    - `00-system-overview.md`
    - `09-agent-workflows.md`
    - `10-agent-api.md`
  - 更新：
    - `03-build-and-deploy.md`
    - `04-variables-and-io.md`
    - `07-node-management.md`
  - 文档明确：
    - Agent 默认读 Markdown。
    - 人读 HTML。
    - 文件同步走 Host API，不操作网页编辑器。
    - 真实节点和模拟节点统一生命周期。
    - 模拟节点 `sim://{uuid}`、独立 `CoralinkerSimNodeHost` 子进程、默认 32 DI/32 DO + RS485/RS232/CAN layout。
    - 多节点编程按 `logicName` 和节点 UUID，Program 后刷新 variables/meta/flow。
- 验证：
  - `dotnet build 3rd/CoralinkerHost/CoralinkerHost.csproj` 成功；仅剩既有 `DiverCompiler/Processor.cs` unreachable warning。
  - `ReadLints` 检查新增/修改 Host C# 文件无错误。
  - 启动临时 Host 并验证端点：
    - `/api/ping`
    - `/api/docs/kit`
    - `/api/docs/kit/md/README.md`
    - `/docs/kit/README.html`
    - `/api/agent/capabilities`
    - `/api/files/snapshot`
    - `/api/agent/state`
    - `/api/variables/flow`
    - `/api/errors/fatal`
    - `/api/logs/build`
  - `/api/files/sync` 空 changes 返回 `ok=True, committed=False`。
  - `POST /api/agent/refresh` 返回 `ok=True`。
  - HTML 验证：
    - `_write_request` 不在 docs index。
    - `href="*.md"` 数量为 0。
    - `href="*.html"` 存在。
  - `publish-host.ps1 -SkipNativeBuild -NoRestore -ExcludeIisConfig -ExcludeStaticWebAssetsEndpoints` 成功。
  - 发布包验证：
    - `res/docs/kit/md/README.md` 存在。
    - `wwwroot/docs/kit/README.html` 存在。
    - `res/docs/kit/md/10-agent-api.md` 存在。
    - `_write_request` 未发布。
    - manifest 中包含 `kitDocs.markdown=res/docs/kit/md` 和 `kitDocs.html=wwwroot/docs/kit`。
  - `git diff --check -- . ":(exclude)CORAL-NODE-V2.1/**"` 通过，仅有 LF/CRLF 提示。

### [docs-kit-agent-entry] 明确 README 相对链接到 Agent Markdown API 的解析规则
- 用户反馈：
  - 读取 `http://localhost:4499/api/docs/kit/md/` 只看到 README 时，担心没有上下文的 Agent 不知道应该去哪里读取其他文档。
- 判断：
  - 当前 README 已写 `GET /api/docs/kit` 和 `GET /api/docs/kit/md/README.md`，并列出相对 Markdown 链接。
  - 对人或较强 Agent 足够，但对低能力 Agent 来说，相对链接应解析到哪个 API 前缀仍不够显式。
  - `/api/docs/kit` 的索引已能返回每个文件的 `markdownUrl`，这是最稳妥入口。
- 修复：
  - `3rd/CoralinkerKitDocs/README.md`
    - 新增“Agent 如何继续读取其他文档”。
    - 明确优先调用 `GET /api/docs/kit` 获取 `markdownUrl`。
    - 明确相对 Markdown 链接解析规则：`/api/docs/kit/md/{相对路径}`。
    - 给出 `00-system-overview.md`、`09-agent-workflows.md`、`10-agent-api.md` 的 API 路径示例。
    - 明确 `examples/` 和 `stubs/` 也按同一 API 前缀读取源文件。
- 验证：
  - `rg` 确认 README 源文件包含新增标题和 `/api/docs/kit/md/00-system-overview.md`。
  - `/api/docs/kit` 返回 `files[].markdownUrl`，包含 `/api/docs/kit/md/00-system-overview.md?...`。
  - `/api/docs/kit/md/README.md` 内容可匹配 `00-system-overview`。

### [publish-kit-agent-docs-auto] 生成 Auto 试验用 Host publish 包
- 用户要求：
  - 编译到新的 publish 中，用于后续 Auto 试验。
- 执行：
  - 运行 `3rd/CoralinkerHost/publish-host.ps1 -SkipNativeBuild -NoRestore`。
  - 未重新构建 native runtime，复用已有 native build 输出。
  - 前端 `npm run build` 成功。
  - 后端 `dotnet publish` 成功。
  - Kit docs 发布步骤成功。
- 输出：
  - `3rd/CoralinkerHost/Publish/CoralinkerHost_c7f234b_20260603-133618`
- 验证：
  - `res/docs/kit/md/README.md` 存在。
  - `wwwroot/docs/kit/README.html` 存在。
  - `res/docs/kit/md/_write_request` 不存在。
  - `simnode/CoralinkerSimNodeHost.dll` 存在。
  - `publish-info.json` 存在。
- 注意：
  - 构建输出仍有既有 Vite/Rollup warning 和既有 `DiverCompiler/Processor.cs` unreachable warning。

### [agent-host-api-auto-diff-drive] 从 Kit 文档入口真实执行差速底盘闭环
- 用户要求：
  - 作为全新 Agent，不假设已有上下文，只从 `http://localhost:4499/api/docs/kit/md/` 理解系统。
  - 通过 Host API 新建工程、添加子节点、写 Root/Child 用户逻辑、同步提交、编译、编程、启动，并读取变量、Variables Flow、日志、错误状态。
  - 不直接修改网页，不手工修改 generated 文件。
- 文档读取：
  - 入口：`GET /api/docs/kit/md/`。
  - 索引：`GET /api/docs/kit`。
  - 重点文档：
    - `/api/docs/kit/md/README.md`
    - `/api/docs/kit/md/00-system-overview.md`
    - `/api/docs/kit/md/01-quickstart.md`
    - `/api/docs/kit/md/09-agent-workflows.md`
    - `/api/docs/kit/md/10-agent-api.md`
    - `/api/docs/kit/md/02-logic-api.md`
    - `/api/docs/kit/md/03-build-and-deploy.md`
    - `/api/docs/kit/md/04-variables-and-io.md`
    - `/api/docs/kit/md/07-node-management.md`
    - `/api/docs/kit/md/08-faq.md`
    - `/api/docs/kit/md/examples/README.md`
- 执行：
  - 读取 `/api/agent/capabilities`、`/api/files/snapshot`、`/api/agent/state`。
  - 通过只读/OPTIONS 探测确认 `/api/project/new` 存在，随后调用 `POST /api/project/new` 新建/清空工程。
  - 调用 `POST /api/node/add-simulated` 新增模拟节点：
    - UUID：`6c44b44c-a25b-4424-9f01-fed1a597d32b`
    - URI：`sim://6c44b44c-a25b-4424-9f01-fed1a597d32b`
    - 名称：`Sim Diff Drive Child Agent Fresh`
  - 通过 `POST /api/files/sync` 写入并提交 `assets/inputs/DiffDriveAgentFresh.cs`。
  - 最终代码：
    - Root：`DiffDriveRootCart` + `DiffDriveRoot`
      - `[AsControlItem] joystickX`
      - `[AsControlItem] joystickY`
      - `[AsUpperIO] left_diff_speed`
      - `[AsUpperIO] right_diff_speed`
      - 差速计算：`left = joystickY + joystickX`，`right = joystickY - joystickX`，限制 `[-100, 100]`。
    - Child：`DiffDriveChildCart` + `DiffDriveChild`
      - 读取 `left_diff_speed/right_diff_speed`。
      - 输出 `received_left_diff_speed/received_right_diff_speed`。
      - 输出 `actual_left_speed/actual_right_speed`。
      - 输出 `statusCode/statusTextCode`。
      - `Console.WriteLine` 打印 `iteration/left/right/statusCode`。
  - 第一次尝试使用 `string statusText`，编译成功但 Host 变量表显示 `Unknown(12)` 且值为 `0`，节点日志插值为空；改为 `int statusTextCode` 后重新提交。
- 验证：
  - `POST /api/build` 成功：
    - buildId：`20260603-055629`
    - sourceCommit：`58d14f7a2a869324c5a7ca8808d3e0a4805ecd07`
    - artifacts：`DiffDriveChild`
    - rootLogics：`DiffDriveRoot`
  - `GET /api/logic/list` 显示 `DiffDriveChild`，bin `934` bytes。
  - `GET /api/root/logics` 显示 `DiffDriveRoot`，control fields 为 `joystickX/joystickY`。
  - `POST /api/node/{uuid}/program` 成功，`programSize=934`。
  - `POST /api/root/configure` 正确请求体为 `{ "logicName": "DiffDriveRoot" }`。
  - 干净顺序 `stop -> root configure -> start -> control set -> refresh` 成功：
    - `/api/session/state`：`Running`，`isRunning=true`。
    - `/api/nodes/state`：节点 `running`、`isConnected=true`、`isProgrammed=true`。
    - `/api/root/state`：`isRunning=true`，`logicName=DiffDriveRoot`。
    - `POST /api/root/control/set` 设置 `joystickX=10`、`joystickY=40` 成功。
    - `/api/variables`：
      - `joystickX=10`
      - `joystickY=40`
      - `left_diff_speed=50`
      - `right_diff_speed=30`
      - `received_left_diff_speed=50`
      - `received_right_diff_speed=30`
      - `actual_left_speed=50`
      - `actual_right_speed=30`
      - `statusCode=1`
      - `statusTextCode=1`
    - `/api/variables/flow`：
      - 包含 `root-runtime` 和模拟节点。
      - `left_diff_speed/right_diff_speed` 由 `root-runtime` 写，模拟节点读。
      - LowerIO 由模拟节点写。
    - `/api/logs/node/{uuid}`：
      - 日志包含 `DiffDriveChild iteration=... left=50 right=30 statusCode=1`。
    - `/api/errors/fatal`：`fatalError=null`。
- 困难/缺口：
  - `/api/project/new` 没有在 Kit docs 和 `/api/agent/capabilities` 中列出；只能通过 `/api/project/new` 的 405 行为判断 POST 端点存在。
  - `/api/root/configure` 文档只列了路径，没有请求体；错误字段名如 `{ "name": "DiffDriveRoot" }` 也返回 `ok=true`，但会清空 Root 配置。
  - `examples/hello.cs`、`examples/numeric.cs`、`examples/car_demo.cs`、`examples/dual_node_skeleton.cs`、`stubs/CartActivator.cs` 在当前发布包通过 Markdown API 返回 404。
  - 文档说 `string` 是支持类型，但 Host 对 `string` LowerIO 显示 `Unknown(12)` 且运行值/日志不可用，弱 Agent 会误用。
  - 如果在 session running 时交错执行 build/program/start，Root/control meta 会出现不完整状态；文档应强调 program 或 root configure 前先 stop。
- 建议文档修改：
  - 在 `10-agent-api.md` 增加 Project API：`GET /api/project`、`POST /api/project/new`，说明副作用、是否清空节点/文件、返回体为空是否代表成功。
  - 在 Root Runtime API 下给出 `POST /api/root/configure` 和 `POST /api/root/control/set` 的完整 JSON 示例，并说明错误字段名不应返回 `ok=true`。
  - 修复 examples/stubs 发布路径，或从 README 移除不可访问链接。
  - 在 `04-variables-and-io.md` 或 `02-logic-api.md` 标注当前 Host 对 `string` 变量的显示限制。
  - 在 Agent workflow 中补充推荐顺序：`stop -> build -> program -> root configure -> start -> root/control/set -> variables/flow/logs/fatal`。

## 2026-06-03 14:03 UTC+8 — 根据 Auto SubAgent 反馈修正 Agent Docs/API 缺口

- 背景：
  - 按用户要求新建无上下文 SubAgent，只给入口 `http://localhost:4499/api/docs/kit/md/` 和目标任务。
  - SubAgent 真实完成新工程、模拟子节点、Root joystick 控制、差速左右轮速、子节点接收轮速、文件同步提交、build/program/start、变量/flow/log/fatal 验证。
  - SubAgent 反馈出若干弱 Agent 会被卡住的问题，需要立即修正。
- 修正 Host API：
  - `3rd/CoralinkerHost/Web/ApiRoutes.cs`
    - `/api/root/configure` 改为读取 `JsonObject`。
    - 缺少 `logicName` 字段时返回 HTTP 400：`Missing logicName...`。
    - `{ "logicName": null }` 才表示显式清空 Root 配置。
    - 避免弱 Agent 写错字段名时仍返回 `ok=true` 并清空 Root。
  - `3rd/CoralinkerHost/Services/AgentStateService.cs`
    - `/api/agent/capabilities` 增加：
      - `POST /api/project/new`
      - `GET /api/project`
      - `POST /api/root/configure`
- 修正 Kit Docs 服务：
  - `3rd/CoralinkerHost/Services/KitDocsService.cs`
    - 文档源接口允许发布/读取 `.md/.cs/.txt/.json`。
    - `/api/docs/kit/md/stubs/CartActivator.cs` 和 `examples/*.cs` 不再返回 404。
    - `/docs/kit/stubs/CartActivator.cs` 这类非 Markdown HTML 路由按静态源文件返回，不再强行追加 `.html`。
    - index 中非 Markdown 文件的 `HtmlUrl` 指向原始静态路径，Markdown 文件仍指向 `.html`。
- 修正 Kit Docs 内容：
  - `3rd/CoralinkerKitDocs/10-agent-api.md`
    - 增加 Project API：`POST /api/project/new`、`GET /api/project`。
    - 写明新建/清空工程的副作用。
    - 增加 running 状态下可靠流程：
      - `stop -> files/sync -> build -> program -> root/configure -> start -> root/control/set -> agent/state`
    - 增加 `/api/root/configure` 请求体示例：
      - `{ "logicName": "DiffDriveRoot" }`
      - `{ "logicName": null }`
    - 明确字段名必须是 `logicName`，错误字段名返回 400。
  - `3rd/CoralinkerKitDocs/09-agent-workflows.md`
    - 增加 running 状态下的 stop-first 顺序。
    - Start 流程中写明先 configure Root，再 start，之后再 set control 和 refresh。
  - `3rd/CoralinkerKitDocs/02-logic-api.md`
    - 保留 `string` 是底层协议类型的事实。
    - 增加 Agent 调试建议：Host 变量表/API/Flow 对字符串显示可能不完整，自动验证优先用 `int/uint/float`。
  - `3rd/CoralinkerKitDocs/08-faq.md`
    - 增加 `string` 字段不建议用于 Agent 自动验证的 FAQ。
- 验证：
  - `dotnet build 3rd/CoralinkerHost/CoralinkerHost.csproj --no-restore` 成功，0 error。
  - 仍有既有 nullable/unreachable/unused field warning。
  - `ReadLints` 检查：
    - `Services/KitDocsService.cs`
    - `Services/AgentStateService.cs`
    - `Web/ApiRoutes.cs`
    - 均无错误。
  - `publish-host.ps1 -SkipNativeBuild -NoRestore` 成功。
  - 最终发布包：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_c7f234b_20260603-140210`
  - 已确认最终发布包包含：
    - `res/docs/kit/md/stubs/CartActivator.cs`
    - `wwwroot/docs/kit/stubs/CartActivator.cs`
    - `res/docs/kit/md/10-agent-api.md`
    - `wwwroot/docs/kit/10-agent-api.html`
    - `publish-info.json`

## 2026-06-03 15:38 UTC+8 — Graph 工具栏增加 Agent 编程入口

- 用户需求：
  - 在网页 Graph 栏最右边增加 Agent 按钮。
  - 点击后弹出 Agent 弹窗。
  - 弹窗中写明：
    - Agent编程。
    - 对人类接口文档地址。
    - Agent文档地址和建议Prompt。
- 实现：
  - 修改 `3rd/CoralinkerHost/ClientApp/src/views/HomeView.vue`。
  - Graph toolbar：
    - 在 `About` 按钮后增加 `Agent` 按钮，使其成为 Graph 工具组最右侧按钮。
    - 点击后设置 `showAgentDialog = true`。
  - 新增 Agent modal：
    - 使用现有 Naive UI `n-modal` + `n-card` 风格。
    - 标题：`Agent Programming`。
    - 第一段 `Agent编程`：
      - 说明外部 Agent 应读取 Markdown 文档入口，并通过 Host HTTP API 完成文件同步、提交、编译、节点操作、变量读取、日志和错误监控。
    - 第二段 `对人类接口文档地址`：
      - 使用当前页面 origin 生成 `${window.location.origin}/docs/kit/`。
    - 第三段 `Agent文档地址和建议Prompt`：
      - 使用当前页面 origin 生成 `${window.location.origin}/api/docs/kit/md/`。
      - 提供建议 Prompt，要求 Agent 从文档入口开始、只修改 `assets/inputs/*.cs`、通过文件同步 API 提交、按 stop-first 流程执行并用 Variables/Flow/log/fatal 验证。
  - 样式：
    - 新增 `.toolbar-btn.agent:hover`。
    - 新增 `.agent-dialog`、`.agent-section`、`.agent-prompt`。
- 验证：
  - `ReadLints` 检查 `HomeView.vue` 无错误。
  - `npm run build` 成功。
  - 剩余 warning 为既有 Vite/Rollup warning：
    - SignalR pure annotation comment warning。
    - `device.ts` 同时 dynamic/static import chunk warning。
    - 大 chunk size warning。

## 2026-06-03 15:42 UTC+8 — 发布包含 Agent 按钮的新 Host 包

- 用户需求：
  - 对刚才加入 Graph 工具栏 Agent 按钮和 Agent 弹窗的版本执行 publish。
- 执行：
  - 运行 `3rd/CoralinkerHost/publish-host.ps1 -SkipNativeBuild -NoRestore`。
  - 发布脚本完成：
    - 前端 `npm run build`。
    - `dotnet publish`。
    - Kit docs 发布。
    - `publish-info.json` 生成。
- 输出目录：
  - `3rd/CoralinkerHost/Publish/CoralinkerHost_c7f234b_20260603-154221`
- 验证：
  - 发布命令退出码为 0。
  - 已确认发布包关键文件存在：
    - `publish-info.json`
    - `CoralinkerHost.dll`
    - `wwwroot/index.html`
    - `wwwroot/docs/kit/README.html`
    - `res/docs/kit/md/README.md`
    - `simnode/CoralinkerSimNodeHost.dll`
- 注意：
  - 剩余 warning 为既有：
    - Vite/Rollup SignalR pure annotation warning。
    - `device.ts` dynamic/static import chunk warning。
    - 大 chunk size warning。
    - `DiverCompiler/Processor.cs` unreachable code warning。

## 2026-06-04 11:26 UTC+8 — 前后桥舵轮模拟工程示例

- 用户需求：
  - 从 `http://localhost:4499/api/docs/kit/md/README.md` 入口开始，按文档要求新建工程、添加虚拟节点、编写前后桥舵轮控制示例。
  - Root 接收摇杆输入并下发前/后桥目标速度和目标舵角。
  - 前桥、后桥节点分别执行本地控制，并包含急停、触边、通信停滞、故障状态、目标限幅等安全逻辑。
- 执行：
  - 下载并解压 Host docs bundle 到 `ai-deck/kit-docs/_bundle`。
  - 阅读 `resources.json`、`README.md`、`09-agent-workflows.md`、`10-agent-api.md`、`tools/README.md`、`02-logic-api.md`、`04-variables-and-io.md`、`06-remote-control.md`、`11-multinode-system-design-reference.md`、示例和 stub。
  - 创建 `ai-deck/agent_work/20260604-1126-steer-bridge/SteerBridgeAgvDemo.cs`，并通过 CLI 同步到 Host `assets/inputs/SteerBridgeAgvDemo.cs`。
  - 调用 `project new`，添加两个模拟节点：
    - 前桥：`e86098ef-40aa-445b-89fe-c8c08917ecb1`
    - 后桥：`7a88202e-b6d3-4c2d-8c9f-8604a8951be2`
  - Build 后分别 program `FrontSteerBridgeLogic`、`RearSteerBridgeLogic`，并配置 Root `SteerBridgeRootLogic`。
- 验证：
  - `logic list` 看到 `FrontSteerBridgeLogic` 与 `RearSteerBridgeLogic` 产物。
  - `variables flow` 看到 Root Runtime、前桥模拟节点、后桥模拟节点，UpperIO/LowerIO 读写方向符合设计。
  - 设置 `enable=true`、`speedLimit=600`、`angleLimit=300`、`joystickX=40`、`joystickY=70` 后，变量值显示：
    - `frontBridgeTargetSpeed=420`
    - `rearBridgeTargetSpeed=420`
    - `frontBridgeTargetAngle=120`
    - `rearBridgeTargetAngle=-120`
    - `frontBridgeActualSpeed=420`
    - `rearBridgeActualSpeed=420`
    - `frontBridgeActualAngle=120`
    - `rearBridgeActualAngle=-120`
  - 设置 `emergencyStop=true` 后，目标速度/角度清零，前后桥 enable 关闭，实际速度降为 0。
  - `errors fatal` 返回 `fatalError=null`。
- 【发现】当前 Host 的 `RootLogic<T>` 实际要求重写无参 `Operation()`；bundle 中 `agv_three_sim_demo.py` 的 Root 示例使用 `Operation(int iteration)`，会导致 build 失败。
- 【发现】Windows GBK 控制台下，CLI/workflow 在打印或读取包含 Unicode 字符的大 JSON 时可能报 `UnicodeEncodeError`/`UnicodeDecodeError`；设置 `$env:PYTHONUTF8='1'` 后直接调用 CLI 可绕过部分问题。
- 【注意】模拟节点中 `WriteSnapshot()` 输出会影响 `node states` 的 digital IO 数值；示例中安全 DI 低位和状态 DO 低位不要重叠，否则会自触发触边/故障。
- 反馈文件：
  - `ai-deck/agent_feedback/20260604-1126-steer-bridge-demo.md`
- 接下来：
  - 若接入真实硬件，需要确认前/后桥驱动器协议、速度和角度单位、急停/触边接线、电平有效方向、通信超时时间、故障复位策略和验证方式。

## 2026-06-04 11:03 UTC+8 — 补充 CLI 缺口反馈、iteration 语义和多节点设计参考

- 用户需求：
  - `10-agent-api.md` 的 curl 使用前置判断中，增加“缺少 Python CLI/workflow 导致必须写大量 curl 时，反馈给开发团队”的规则。
  - 明确 `iteration` 的真实含义：只有 `DIVERSession` 下发 IO 更新表并收到节点正常响应后才递增，停滞代表通信周期没有完成。
  - 新增多节点系统设计参考，帮助 SubAgent 理解 AGV 前后桥、差速、舵轮、Root 解算、节点本地安全和用户追问。
- 修改：
  - `3rd/CoralinkerKitDocs/10-agent-api.md`
    - 在“如果你准备写 curl”下增加 CLI/workflow 缺口反馈要求。
  - `3rd/CoralinkerKitDocs/02-logic-api.md`
    - 扩展 `iteration` 说明，明确其不是普通本地计时器，而是 Host 与节点通信健康度信号。
    - 更新通信安全检测示例，按 `scanInterval=50ms` 使用 `commLossCount > 20` 表示约 1 秒停滞。
  - `3rd/CoralinkerKitDocs/11-multinode-system-design-reference.md`
    - 新增多节点设计参考。
    - 覆盖 Root/MCU 职责、双差速桥 AGV、前后舵轮、中央安全 IO、同 Logic 多实例、变量命名、安全逻辑、必须向用户确认的问题和 Agent 执行建议。
  - `3rd/CoralinkerKitDocs/README.md`
    - 将 `11-multinode-system-design-reference.md` 加入必读顺序、文档索引和关键规则摘要。
  - `3rd/CoralinkerKitDocs/09-agent-workflows.md`
    - 在多节点编程工作流前增加先读 `11-multinode-system-design-reference.md` 的要求。
  - `3rd/CoralinkerHost/Services/KitDocsService.cs`
    - 将 `11-multinode-system-design-reference.md` 加入 API resources 推荐阅读顺序。
  - `3rd/CoralinkerHost/publish-host.ps1`
    - 将 `11-multinode-system-design-reference.md` 加入发布包 `resources.json` 推荐阅读顺序。
- 【发现】SubAgent 容易把多节点任务直接理解成“多个节点都 program 一段逻辑”，但 AGV 类系统更需要先拆清 Root 运动解算、节点本地控制和安全输入归属。
- 【注意】`iteration` 停滞是通信周期未完成的信号，文档中必须把它和安全回路绑定，避免 Agent 只把它当成普通循环计数器。
- 验证：
  - `ReadLints` 检查本次修改的 KitDocs Markdown、`KitDocsService.cs`、`publish-host.ps1` 无错误。
  - `rg` 确认新章节已被 README、09 工作流、Host resources 和发布脚本引用。

## 2026-06-04 11:08 UTC+8 — 修正 Logic 与节点的一对一编程规则

- 用户需求：
  - 与总设计师确认后，规则改为“同一个 Logic 不可以编程到多个节点上”。
  - 原因是同一个 Logic class 产生同一组 LowerIO 字段，多个节点同时写入会互相覆盖，导致变量和 Root 读取混乱。
  - 检查教程和实现，把 README、工作流和 Host 行为改为正确规则。
- 实现修改：
  - `3rd/CoralinkerHost/Web/ApiRoutes.cs`
    - `/api/node/{uuid}/program` 增加重复 `logicName` 检查。
    - 如果同一 `logicName` 已绑定到其他节点，返回 `400 BadRequest`，包含冲突节点信息和修复 hint。
    - `/api/nodes/import` 增加重复 `logicName` 检查，拒绝导入不合法节点集合。
    - `/api/start` 增加重复 `logicName` 检查，防止历史工程绕过新 program 规则直接启动。
  - `3rd/CoralinkerKitDocs/tools/workflows/deploy_logic.py`
  - `3rd/CoralinkerKitDocs/tools/workflows/safe_stop_build_program_start.py`
  - `3rd/CoralinkerKitDocs/tools/workflows/add_nodes_and_program.py`
    - 同一次 workflow 参数中，如果多个 UUID 使用同一个 LogicName，提前报错。
- 文档修改：
  - `README.md` 增加关键规则：一个 Logic class 只能编程到一个 MCU 节点。
  - `00-system-overview.md`、`02-logic-api.md`、`03-build-and-deploy.md`、`04-variables-and-io.md`、`07-node-management.md`、`09-agent-workflows.md`、`10-agent-api.md`、`11-multinode-system-design-reference.md` 全部改为一对一规则。
  - `tools/README.md` 和 workflow help 改为“多节点可编程，但每个节点必须使用 distinct LogicName”。
  - `_write_request/2026-06-03-agent-host-docs.md` 中旧写作原则也同步改正，避免后续文档再引用错误事实。
- 【发现】Variables Flow 能区分节点 UUID，但不能解决同一 Logic 多节点写同名 LowerIO 的冲突；正确做法是每个节点使用独立 Logic class 和角色化 LowerIO 字段名。
- 【注意】旧 publish 包中仍有历史文档内容；下次 publish 会由源文档重新生成并覆盖。
- 验证：
  - `rg` 检查源 KitDocs 中不再存在“同一个 Logic 可以/同一个 logicName 可以”等允许复用表述。
  - `ReadLints` 检查 `ApiRoutes.cs`、KitDocs 和修改的 Python workflows 无错误。
  - `python -m py_compile` 检查修改的 workflow 脚本成功。
  - `dotnet build 3rd/CoralinkerHost/CoralinkerHost.csproj --no-restore` 成功，0 error；剩余 warning 为既有 nullable/unreachable code warning。

## 2026-06-04 11:19 UTC+8 — 增加 Agent 目录规则、发布并启动新 Host、启动无上下文 SubAgent

- 用户需求：
  - 补充 Agent 本地目录规则到文档。
  - publish 一个新包。
  - 启动新的发布包。
  - 启动一个无上下文 SubAgent，Prompt 必须是中文，并且只包含网页上能看到的入口和普通用户任务，不额外灌输隐藏规则。
- 文档修改：
  - `3rd/CoralinkerKitDocs/README.md`
    - 新增“Agent 本地目录规则”。
    - 明确 `ai-deck/kit-docs/` 用于下载和解压文档 bundle、CLI、workflow。
    - 明确 `ai-deck/agent_work/<YYYYMMDD-HHMMSS-task>/` 用于临时草稿、代码、调试脚本、请求/响应样本和中间结果。
    - 明确 `ai-deck/agent_feedback/` 用于反馈文档、API、CLI、workflow 问题。
    - 要求每个 Agent 自建目录包含 `desc.md`。
  - `3rd/CoralinkerKitDocs/09-agent-workflows.md`
    - 下载命令改为 `--out ai-deck/kit-docs`。
    - 总规则增加 Agent 本地工作目录要求。
    - 新增 Agent 临时工作区说明。
  - `3rd/CoralinkerKitDocs/10-agent-api.md`
    - 顶部 Python 工具示例改为 `--out ai-deck/kit-docs`。
  - `3rd/CoralinkerKitDocs/tools/README.md`
    - 下载示例改为 `--out ai-deck/kit-docs`。
    - Agent Behavior Rules 中补充 `ai-deck/kit-docs`、`ai-deck/agent_work`、`ai-deck/agent_feedback` 用途。
- 发布：
  - 运行 `3rd/CoralinkerHost/publish-host.ps1 -SkipNativeBuild -NoRestore`。
  - 新发布目录：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_c7f234b_20260604-112133`
  - 发布成功，剩余 warning 为既有 Vite chunk warning、SignalR pure annotation warning、DiverCompiler unreachable code 和 nullable warning。
- 启动：
  - 第一次启动新包失败，原因是 4499 已被旧 Host 占用。
  - 确认旧 Host PID 10168 是 `dotnet .\CoralinkerHost.dll --urls http://localhost:4499`。
  - 停止旧 Host 后重新启动新包。
  - 当前 4499 已监听新发布包。
- 验证：
  - `ReadLints` 检查 README、09、10、tools README 无错误。
  - 发布包 `res/docs/kit/md/README.md` 已包含 `Agent 本地目录规则`。
  - `http://localhost:4499/api/docs/kit/md/README.md` 返回 200。
  - 接口内容包含 `ai-deck/kit-docs`，确认当前服务文档已刷新。
- SubAgent：
  - 启动无上下文 SubAgent。
  - Prompt 为中文，仅提供 `http://localhost:4499/api/docs/kit/md/README.md` 和普通用户任务：新建工程、添加虚拟节点、实现前后桥舵轮控制、Root 摇杆输入运动解算、前后桥节点控制、安全逻辑、验证和反馈。
- 【注意】这次 SubAgent Prompt 没有额外灌输隐藏操作规则，目的是验证网页文档自身能否教会 Agent 使用 bundle、工具、目录规则和反馈协议。

## 2026-06-04 11:37 UTC+8 — 根据前后桥舵轮 SubAgent 反馈修复工具和文档

- SubAgent 结果：
  - 已完成前后桥舵轮模拟工程，验证 build/program/start、Root 摇杆解算、急停清零和 fatal error。
  - 反馈文件：
    - `ai-deck/agent_feedback/20260604-1126-steer-bridge-demo.md`
- 反馈问题：
  - 官方 `agv_three_sim_demo.py` 的 Root 示例使用 `Operation(int iteration)`，但当前 Root 逻辑应使用无参 `Operation()`。
  - Windows GBK 控制台下，CLI/workflow 输出 Unicode JSON 可能触发编码错误。
  - workflow 最终 `state` 汇总失败时容易让 Agent 误判前面的 build/program/start 失败。
  - 模拟 Snapshot IO 示例可能低位 DI/DO 自触发；用户指出 SnapIO 主要是模拟用途，后续真实项目不重要，不需要过度强调。
  - 网页 Agent Prompt 和给 SubAgent 的 Prompt 不一致，规则应主要写在文档中。
  - 真实硬件会让车辆动起来时，Agent 不应默认直接 Start。
- 修改：
  - `3rd/CoralinkerKitDocs/tools/workflows/agv_three_sim_demo.py`
    - Root 示例改为无参 `Operation()`，内部用 `_iteration` 字段分频。
    - 三个 MCU Logic 改为各自独立 CartDefinition，避免 LowerIO 声明混用。
    - `subprocess.run` 增加 `encoding="utf-8"`、`errors="replace"`。
    - 输出 JSON 改为 `ensure_ascii=True`。
    - 最终 `state` 汇总失败时写入 `stateError`，不抹掉已完成步骤。
  - `3rd/CoralinkerKitDocs/tools/agent_cli/coral_agent.py`
    - stdout/stderr reconfigure 为 UTF-8 + replace。
    - `print_json` 改为 ASCII-safe JSON。
  - 其他 workflow：
    - `deploy_logic.py`
    - `safe_stop_build_program_start.py`
    - `add_nodes_and_program.py`
    - `configure_root_and_controls.py`
    - `debug_serial.py`
    - `debug_can_wiretap.py`
    - 增加 UTF-8 subprocess 解码和 ASCII-safe JSON 输出。
  - `safe_stop_build_program_start.py`
    - 新增 `--manual-start`，不调用 `/api/start`，等待人类在网页点击 Start。
    - 新增 `--wait-before-state`，便于人工 Start 后再读取状态。
  - `3rd/CoralinkerKitDocs/02-logic-api.md`
    - 新增 Root `Operation()` 与 MCU `Operation(int iteration)` 的签名差异说明。
    - Snapshot IO 增加模拟节点低位 DI/DO 自触发风险说明，但仅作为模拟验证注意事项。
  - `03-build-and-deploy.md`、`08-faq.md`
    - 修正 Operation 未实现的 FAQ：MCU 与 Root 签名不同。
  - `README.md`、`09-agent-workflows.md`、`10-agent-api.md`、`11-multinode-system-design-reference.md`
    - 增加真实硬件启动授权策略：模拟节点可自动 Start；真实硬件、车辆运动或危险输出任务必须先询问启动授权。
    - 默认无确认时，人类手动 Start。
  - `tools/README.md`
    - 说明真实硬件可使用 `safe_stop_build_program_start.py --manual-start --wait-before-state 30` 完成部署后等待人类点击 Start。
  - `3rd/CoralinkerHost/ClientApp/src/views/HomeView.vue`
    - Agent 弹窗建议 Prompt 改为入口式中文 Prompt，不重复具体 API 细节。
    - 明确真实硬件启动、车辆运动或危险输出前必须询问启动方式。
- 【发现】SubAgent 没追问硬件接线是合理的，因为测试任务明确使用虚拟节点；但文档必须明确“模拟可自动验证，真实硬件必须确认启动授权”。
- 【注意】真实车辆或执行器调试中，Start 和会导致运动的 control/UpperIO 写入都应视为危险操作，默认等待人类确认。
- 验证：
  - `ReadLints` 检查修改的 KitDocs、tools README 和 `HomeView.vue` 无错误。
  - `python -m py_compile` 覆盖 Agent CLI 和全部 workflow 脚本成功。

## 2026-06-04 11:57 UTC+8 — 增加硬件事实沉淀规则 fact.md

- 用户反馈：
  - 刚才交互式提问卡住，需要继续执行。
  - Agent 向用户提问并确认的硬件连接方式、型号、极性、端口、波特率、负载重量、安装位置、减速比、最大速度、控制协议、车辆构型、实际应用场景、节拍等信息，应整理后添加到 `ai-deck/fact.md`。
- 修改：
  - `3rd/CoralinkerKitDocs/README.md`
    - 关键规则摘要增加：确认过的硬件事实要整理到 `ai-deck/fact.md`。
  - `3rd/CoralinkerKitDocs/09-agent-workflows.md`
    - 总规则增加：后续 Agent 应优先读取 `ai-deck/fact.md`，不要重复追问已确认事实。
    - “用户补充输入和硬件信息”章节扩展硬件事实清单。
    - 明确 `fact.md` 只记录已确认事实、来源和待确认问题，不写猜测。
  - `3rd/CoralinkerKitDocs/11-multinode-system-design-reference.md`
    - 多节点真实硬件确认清单增加型号、安装位置、负载、减速比、轮径、速度/加速度/舵角限制、机械限位、车辆构型、应用场景和节拍。
    - 明确确认后的硬件事实必须沉淀到 `ai-deck/fact.md`。
  - `3rd/CoralinkerKitDocs/tools/README.md`
    - Agent Behavior Rules 增加真实硬件事实写入 `ai-deck/fact.md` 的说明。
  - `3rd/CoralinkerHost/ClientApp/src/views/HomeView.vue`
    - Agent 弹窗建议 Prompt 增加：把确认过的硬件事实整理到 `ai-deck/fact.md`。
  - `ai-deck/fact.md`
    - 新建硬件事实模板，分为设备与节点、接线与端口、机械与运动、控制协议、安全策略、应用场景与节拍。
- 【注意】`fact.md` 是现场事实来源，应只写已确认内容和待确认问题；不能把 Agent 猜测写成事实。
- 验证：
  - `ReadLints` 检查修改的 KitDocs、`HomeView.vue` 和 `ai-deck/fact.md` 无错误。
  - `python -m py_compile` 再次检查 Agent CLI 和全部 workflow 脚本成功。
  - `rg` 确认 README、09、11、tools README、网页 Prompt 和 `fact.md` 均包含新规则。

## 2026-06-04 12:00 UTC+8 — 将 fact 模板纳入 KitDocs bundle 并补全启动授权流程

- 用户反馈：
  - `ai-deck/fact.md` 放在当前仓库不会随 KitDocs bundle 打包给客户 Agent。
  - 启动授权章节还不够完整。
- 修改：
  - 新增 `3rd/CoralinkerKitDocs/runtime/fact-template.md`
    - 作为 `ai-deck/fact.md` 的分发模板，会随 KitDocs bundle/resources 发布。
    - 内容覆盖设备与节点、接线与端口、机械与运动、控制协议、安全策略、应用场景与节拍。
  - `3rd/CoralinkerKitDocs/README.md`
    - Agent 本地目录规则增加 `ai-deck/fact.md`。
    - 指向 `runtime/fact-template.md`。
    - 文档索引增加 `runtime/fact-template.md`。
  - `3rd/CoralinkerKitDocs/09-agent-workflows.md`
    - 补全启动授权流程：
      - 模拟节点可自动 Start。
      - 真实硬件/车辆运动/危险输出必须确认启动授权。
      - 选项 1：人类手动 Start，Agent 只部署到 build/program/root configure。
      - 选项 2：每次确认后 Agent 执行 Start/control。
      - 选项 3：本次会话授权 Agent 直接执行，但仍需说明计划和结果。
      - 未明确授权时默认人类手动 Start。
    - 明确 `ai-deck/fact.md` 不存在时，从 `runtime/fact-template.md` 创建。
  - `3rd/CoralinkerKitDocs/10-agent-api.md`
    - Start API 安全规则补充三种启动授权模式和默认行为。
  - `3rd/CoralinkerKitDocs/03-build-and-deploy.md`
    - 启动章节补充 `runtime/fact-template.md`。
  - `3rd/CoralinkerKitDocs/11-multinode-system-design-reference.md`
    - 将启动授权模式扩展为三种模式，并说明无明确授权时默认人类手动 Start。
  - `3rd/CoralinkerHost/Services/KitDocsService.cs`
  - `3rd/CoralinkerHost/publish-host.ps1`
    - 将 `runtime/fact-template.md` 加入 recommendedReadOrder。
- 【发现】`fact.md` 应是 Agent 工作区文件，模板必须在 KitDocs bundle 中分发；否则客户环境首次使用时没有来源。
- 【注意】启动授权不是一句提醒，而是可持久化现场事实；必须写入 `ai-deck/fact.md`，后续 Agent 按记录执行。
- 验证：
  - `ReadLints` 检查 README、09、10、11、03、runtime/fact-template、KitDocsService、publish-host 无错误。
  - `rg` 确认 `runtime/fact-template.md` 已出现在 README、09、11、KitDocsService 和 publish-host 推荐顺序中。

## 2026-06-04 12:04 UTC+8 — 发布包含 fact 模板和启动授权规则的新包

- 用户需求：
  - 直接打一个新包。
- 执行：
  - 运行 `3rd/CoralinkerHost/publish-host.ps1 -SkipNativeBuild -NoRestore`。
  - 发布成功。
- 输出目录：
  - `3rd/CoralinkerHost/Publish/CoralinkerHost_c7f234b_20260604-120452`
- 验证：
  - `publish-info.json` 存在，记录 `publishTime=2026-06-04T12:04:52.5705076+08:00`。
  - 发布包 `res/docs/kit/md/runtime/fact-template.md` 存在。
  - 发布包 `wwwroot/docs/kit/resources.json` 的 `recommendedReadOrder` 包含 `runtime/fact-template.md`。
  - 发布包 `wwwroot/docs/kit/bundle.zip` 存在（为二进制 zip，不能按文本读取）。
- 注意：
  - 剩余 warning 为既有：
    - Vite/Rollup SignalR pure annotation warning。
    - `device.ts` dynamic/static import chunk warning。
    - 大 chunk size warning。
    - `DiverCompiler/Processor.cs` unreachable code warning。
    - 既有 nullable warning。

## 2026-06-04 12:06 UTC+8 — 修复人类 HTML 文档入口路由与中文编码

- 用户反馈：
  - `http://127.0.0.1:4499/docs/kit/` 打不开。
  - 后续确认旧 Host 未停止导致新包无法运行。
  - 新包运行后 HTML 中文显示乱码，例如 `Coralinker Kit 寮€鍙戞枃妗?`。
- 问题定位：
  - `/docs/kit/` 命中多个 endpoint：
    - `/docs/kit`
    - `/docs/kit/`
    - `/docs/kit/{**path}`
    - 导致 ASP.NET Core `AmbiguousMatchException`，返回 500。
  - `publish-host.ps1` 使用 `Get-Content -Raw` 读取 Markdown，未指定 UTF-8，在 Windows PowerShell 下按默认编码读取，导致生成的 HTML 已经乱码。
- 修改：
  - `3rd/CoralinkerHost/Web/ApiRoutes.cs`
    - 删除显式 `/docs/kit/` 路由。
    - catch-all `/docs/kit/{**path}` 遇到空 path 时返回 `README.html`。
  - `3rd/CoralinkerHost/publish-host.ps1`
    - 生成 HTML 时读取 Markdown 改为 `Get-Content -Raw -Encoding UTF8`。
- 执行：
  - 停止占用 4499 的旧 Host：
    - PID 23536，旧包 `CoralinkerHost_c7f234b_20260604-120815`。
  - 重新发布：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_c7f234b_20260604-121453`
  - 启动新包到 `http://localhost:4499`。
- 验证：
  - `dotnet build 3rd/CoralinkerHost/CoralinkerHost.csproj --no-restore` 成功，0 error。
  - `http://127.0.0.1:4499/docs/kit/` 返回 200。
  - 返回内容包含 `Coralinker Kit 开发文档`，确认中文编码正常。
  - 发布包 `wwwroot/docs/kit/README.html` 标题为 `Coralinker Kit 开发文档 - Coralinker Kit Docs`。
- 【注意】如果用户启动旧包占住 4499，新包会启动失败；publish 后验证前应先确认端口上的 Host 路径。

## 2026-06-04 10:54 UTC+8 — 统一 Host 地址占位符并压缩 Agent 文档重复

- 用户反馈：
  - 文档示例写死 `localhost:4499`，客户使用其他 Host 地址时会误导 Agent。
  - `README.md` 前面要求 bundle-first，后面又写 GET 读取，语义容易混淆。
  - `README.md`、`09-agent-workflows.md`、`10-agent-api.md` 存在部分重复。
- 修改：
  - 将 KitDocs Markdown 示例中的 `http://localhost:4499` 统一替换为 `<HOST_URI>`。
  - `README.md` 明确 `<HOST_URI>` 是当前 CoralinkerHost 的 origin，并把逐个读取 Markdown 改成“bundle 下载失败时的 fallback”。
  - `README.md` 的规则区压缩为入口摘要，详细流程交给 `09-agent-workflows.md`，底层 API 交给 `10-agent-api.md`。
  - `10-agent-api.md` 顶部明确 `<HOST_URI>`，并把“Agent 启动后应先调用”改成底层 API 说明，避免和 Python 工具优先冲突。
  - 顺手修正相关 Markdown 表格分隔符和一个代码块语言标记，消除 markdownlint 诊断。
- 【发现】Host 地址不适合发布时动态替换 zip；文档使用 `<HOST_URI>` 更稳定，同一包可服务 localhost、局域网 IP、远程 VH 和客户自定义域名。
- 【注意】Python 工具源码仍保留 localhost 作为本机开发默认值，但教程示例显式要求 Agent 传入 `--host <HOST_URI>`。
- 验证：
  - `rg` 确认 KitDocs Markdown 中没有残留 `localhost:4499` 或 `http://localhost`。
  - `ReadLints` 检查本次修改的 KitDocs Markdown 无错误。

## 2026-06-04 10:22 UTC+8 — Agent 编程系统黑盒可用性测试

- 用户需求：
  - 作为全新 Agent，只从 `http://localhost:4499/api/docs/kit/md/` 开始，验证是否能仅凭文档和 API 完成 Agent programming workflow。
- 执行：
  - 阅读入口文档、`/api/docs/kit`、`/api/docs/kit/resources`、`00-system-overview.md`、`09-agent-workflows.md`、`10-agent-api.md`、`02-logic-api.md`、`04-variables-and-io.md`、`06-remote-control.md`、stub、示例和工具说明。
  - 在 `ai-deck/agent_programming_test_20260604/` 建立本次测试工作目录，包含 `desc.md`、`scripts/`、`logics/`、`results/`。
  - 通过 Host API 新建工程，添加 3 个 simulated MCU 节点。
  - 编写 `AgvAgentWorkflow.cs`，包含 3 个 MCU logic：drive、safety、actuator/status；以及 1 个 Host-side Root logic：`AgvJoystickRootLogic`。
  - 使用 bundled `coral_agent.py files sync` 将源码同步到 `assets/inputs/AgvAgentWorkflow.cs`，没有直接编辑 Host data folder。
  - Build 成功，产物包含 3 个 MCU artifacts 和 1 个 root logic。
  - 分别 program 3 个 simulated 节点，配置 Root，启动会话，设置 `enable`、`joystickX/Y`、`liftRequest` 等 ControlItem，刷新 state/variables/flow/logs/fatal errors。
  - 测试结束后使用 CLI stop 停止会话。
- 结果：
  - `start --require-all` 返回 `totalNodes=3`、`successNodes=3`。
  - Variables/flow 显示 Root 写入 `agv_drive_left_cmd`、`agv_drive_right_cmd`、`agv_safety_brake_cmd`、`agv_actuator_lift_cmd`，对应 MCU 节点读取。
  - 节点日志分别出现 `DRIVE`、`SAFETY`、`ACTUATOR`，行为差异可见。
  - Fatal error 为 null。
- 【发现】Root vs MCU 边界在 README/Agent workflows/tools 中足够明确：MCU 使用 `/api/node/{uuid}/program`，Root 使用 `/api/root/configure` 和 control API。
- 【注意】Root 的具体 C# 写法需要从 stub、`04-variables-and-io.md`、`06-remote-control.md` 组合推断；入口推荐阅读顺序未显式包含 `04`/`06`，弱 Agent 可能遗漏。
- 【发现】MCU 日志频率与示例中 `iteration % N` 的直觉不完全一致，drive/safety 日志比代码条件预期更频繁，可能是 iteration 语义或 sim runtime 行为需要进一步说明。
- 接下来：
  - 根据最终报告中的 blocker/confusing points，考虑补充一份“一键 AGV 三节点 + Root”完整示例和 workflow 脚本。

## 2026-06-04 10:04 UTC+8 — Agent 工具链、Root 编程差异和 API 反馈修复

- 用户需求：
  - 继续完成 Agent 反馈修复计划。
  - 本次使用中其他 Agent 对 Root 节点“编程”理解有误，需要强调 Root 与 MCU 节点差异。
  - 不要遗忘 Root 配置、Root 控制示例和工具链。
- 修改：
  - 新增和完善 Agent Python 工具：
    - `3rd/CoralinkerKitDocs/tools/agent_cli/coral_agent.py`
      - 增加 `node add`、`node add-simulated`、`node info`。
      - 增加 `root meta`、`root set-control`。
      - 保留 `files sync` 自动 snapshot/baseHash/JSON 序列化，避免手写转义 payload。
    - `3rd/CoralinkerKitDocs/tools/workflows/add_nodes_and_program.py`
      - 支持 0/1/多真实节点或模拟节点。
      - 支持重复 `--program UUID=LogicName`。
      - 增加 `--root-logic`，并在输出中注明 Root 不走 `/api/node/{uuid}/program`。
    - `3rd/CoralinkerKitDocs/tools/workflows/configure_root_and_controls.py`
      - 专门演示 Root configure、读取 control meta、设置 ControlItem。
    - `3rd/CoralinkerKitDocs/tools/README.md`
      - 说明 CLI、workflow、Root 与 MCU program 差异。
    - 修复 `debug_can_wiretap.py` 被重复拼接导致 `from __future__` 位置错误的问题。
  - 离线文档包：
    - `KitDocsService` 支持 `.py` 文档资源、`GetResources()` 和 `BuildBundle()`。
    - `ApiRoutes` 增加：
      - `GET /api/docs/kit/resources`
      - `GET /api/docs/kit/bundle.zip`
    - `publish-host.ps1` 生成 `resources.json` 和静态 `bundle.zip`。
    - `AgentStateService` capabilities 增加 docs resources/bundle。
  - API 行为修复：
    - `/api/files/sync` 对旧 `files[]` 格式明确返回 400，提示使用 `changes[]`。
    - `/api/start` 部分节点失败时返回 `ok=false/status=PartialFailure/sessionRunning=true`，避免 Agent 只看 `ok` 误判。
    - `/api/node/{uuid}/configure` 的 `portConfigs` 支持按 `index`/`name`/顺序局部合并，保留未提交端口配置。
    - `PortConfigSnapshot` 增加 `index`，节点信息和 probe layout 都能暴露端口 index。
    - probe/add 失败返回 `mcuUri` 和 `hint`，方便 Agent 排查 URI、设备占用、供电和接线。
    - `DIVERSession.StopBackgroundWorkers()` 改为局部交换 CTS/worker 引用；worker 超时未退出时延迟 dispose，并捕获 `token.WaitHandle` 的 `ObjectDisposedException`，降低 stop/start 竞态崩溃风险。
  - 文档更新：
    - `README.md`
      - 补充 bundle/resources/tools 入口。
      - 明确不要手写 sync JSON，Root 不使用 MCU node program。
    - `09-agent-workflows.md`
      - 增加工具优先的 sync 流程。
      - 新增 Root 配置和控制工作流。
      - 明确 `PartialFailure` 处理规则。
    - `10-agent-api.md`
      - 补充 docs bundle/resources。
      - 补充 sync CLI 示例、旧 `files[]` 400 说明。
      - 补充 start partial failure 返回示例。
      - 补充 portConfigs 局部合并和 Root control 示例。
    - `08-faq.md`
      - 新增 Agent 常见阻塞：sync、probe/add、Root 编程误解、start partial、stop 冷却、portConfigs、WireTap。
- 【发现】Root Logic 的正确模型是 Host 侧 .NET runtime 逻辑：Build 发现并生成 Root metadata，`/api/root/configure` 只负责选择绑定；Root ControlItem 通过 `/api/root/control/meta` 和 `/api/root/control/set` 操作。不能把 Root 当 MCU 节点调用 `/api/node/{uuid}/program`。
- 【注意】Agent 开发文件时应优先使用 `coral_agent.py files sync` 或语言内置 JSON 序列化。手写 shell 转义 JSON 很容易造成 payload 形状错误或 silently no-op。
- 【注意】`/api/start` 现在 `ok=true` 只表示全部节点启动成功。部分成功会返回 `ok=false/status=PartialFailure`，但 `sessionRunning=true` 表示已有节点在运行，排查前通常应先 stop。
- 验证：
  - `python -m py_compile` 覆盖：
    - `tools/agent_cli/coral_agent.py`
    - `tools/workflows/add_nodes_and_program.py`
    - `tools/workflows/configure_root_and_controls.py`
    - `tools/workflows/debug_can_wiretap.py`
    - `tools/workflows/debug_serial.py`
    - `tools/workflows/deploy_logic.py`
    - `tools/workflows/safe_stop_build_program_start.py`
  - `dotnet build` 在 `3rd/CoralinkerHost` 成功，0 error。
  - `ReadLints` 检查修改的 C# 文件无错误。
  - 剩余 warning 为项目既有 nullable/未使用/unreachable code warning。
- 接下来：
  - 如需给 Auto/其他 Agent 试用，建议发布 Host 后让新 Agent 先下载 `/api/docs/kit/bundle.zip`，再使用 `tools/agent_cli/coral_agent.py` 完成 sync/build/program/root/start。

## 2026-06-04 10:19 UTC+8 — 发布新包并启动无上下文 Agent 试用

- 用户需求：
  - 确认前一轮任务是否全部做完。
  - 发布一个全新 CoralinkerHost 包。
  - 从新包启动后台 Host。
  - 启动一个无上下文 SubAgent，要求它仅按 Host 文档和 API 完成：
    - 添加 3 个虚拟/模拟 MCU 节点。
    - 为每个节点写不同逻辑。
    - 写 Root logic，将 joystick X/Y 分解并下发给子节点执行。
    - 跑常见 AGV 逻辑测试。
    - 调试整个 Agent 编程系统，并提出问题和建议。
- 执行：
  - 运行 `3rd/CoralinkerHost/publish-host.ps1 -SkipNativeBuild -NoRestore`。
  - 新发布包目录：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_c7f234b_20260604-101955`
  - 从发布目录启动后台 Host：
    - `dotnet .\CoralinkerHost.dll --urls http://localhost:4499`
  - 使用 `Invoke-RestMethod http://localhost:4499/api/ping` 验证 Host 可访问，返回 `ok=true`。
  - 启动无上下文 SubAgent，入口只给：
    - `http://localhost:4499/api/docs/kit/md/`
- 【注意】当前后台 Host 使用的是新发布包目录，不是源码目录直接运行。
- 【注意】SubAgent 任务要求它不能直接编辑 Host data folder，只能使用 Host API 或文档包内 Agent 工具。
- 接下来：
  - 等待 SubAgent 返回中文报告后，根据其反馈继续修正文档/API/工具链。

## 2026-06-04 10:35 UTC+8 — 根据黑盒 Agent 反馈强化教程约束

- 用户反馈：
  - SubAgent 虽然完成了测试，但仍大量用 curl 逐个读取文档，而不是下载 `bundle.zip` 解压。
  - SubAgent 仍倾向手写 Python/HTTP 流程，没有优先发现和使用已有 `coral_agent.py` 与 workflows。
  - 对整个系统缺少一次性理解。
  - 之前给 SubAgent 的许多 prompt 约束应写入网页 Agent 初始 Prompt 和文档，而不是依赖用户每次提醒。
  - Agent 应在每次任务中把准备做什么、做了什么、下一步做什么讲清楚，不能隐藏过程。
- 修改：
  - `3rd/CoralinkerKitDocs/README.md`
    - 新增“Agent 启动后的强制步骤”。
    - 明确所有任务先 `/api/docs/kit/resources`，再下载 `/api/docs/kit/bundle.zip` 解压。
    - 明确先读 `resources.json`、`README.md`、`09-agent-workflows.md`、`10-agent-api.md`、`tools/README.md`。
    - 明确 Root/变量/遥控任务继续读 `04-variables-and-io.md` 和 `06-remote-control.md`。
    - 明确优先使用 bundle 内 `tools/agent_cli/coral_agent.py` 和 `tools/workflows/*.py`，不要默认 curl 或手写脚本。
    - 增加透明度要求：开始前讲计划，执行中讲关键结果，结束讲验证证据和风险。
  - `3rd/CoralinkerKitDocs/09-agent-workflows.md`
    - 总规则改成 bundle-first、tools-first。
    - 新增“Agent 对用户的透明度要求”。
    - 新增“工具优先规则”，明确 workflow > CLI > direct HTTP > 临时脚本。
    - 禁止为 sync 手写 shell 转义 JSON，禁止明明有工具还用 curl 上传大段源码。
  - `3rd/CoralinkerKitDocs/10-agent-api.md`
    - 文档 API 中明确 Agent 默认应下载 bundle，而不是逐个 curl Markdown。
  - `3rd/CoralinkerKitDocs/tools/README.md`
    - 强化 Agent 行为规则。
    - 明确 direct HTTP 和临时 Python 只是 fallback。
    - 增加官方 AGV smoke test 命令。
  - `3rd/CoralinkerHost/ClientApp/src/views/HomeView.vue`
    - 扩展 Agent 弹窗建议 Prompt。
    - 初始 Prompt 现在直接要求：
      - 先 `/api/docs/kit/resources`。
      - 下载并解压 `/api/docs/kit/bundle.zip`。
      - 先读 resources 和关键文档。
      - 优先使用 `agent_cli` 和 workflows。
      - 不要优先 curl、不要手写 sync JSON、不要已有 workflow 还从零写 Python。
      - 执行前/中/后向用户透明说明计划、进度、结果、验证和风险。
  - `3rd/CoralinkerHost/Services/KitDocsService.cs`
    - `RecommendedReadOrder` 增加 `04-variables-and-io.md`、`06-remote-control.md`。
  - `3rd/CoralinkerHost/publish-host.ps1`
    - 静态 `resources.json` 的 `recommendedReadOrder` 同步增加 04/06。
  - 新增 `3rd/CoralinkerKitDocs/tools/workflows/agv_three_sim_demo.py`
    - 官方一键三模拟节点 AGV workflow。
    - 流程：new project -> add 3 simulated nodes -> sync AGV C# -> build -> program 3 MCU nodes -> configure Root -> start -> set joystick/lift -> dump state -> 可选 stop。
    - 目的：给弱 Agent 一个完整可跑样例，避免从零手写整个流程。
- 【发现】只在文档深处写“可以下载 bundle/可以用工具”不够，Agent 会沿用通用 curl/临时脚本习惯。必须在入口 README、workflow 总规则、tools README、网页初始 Prompt 同时写成强约束。
- 【注意】文档角色从“API 说明”升级为“Agent 行为教程”。要约束 Agent 先理解系统、先用工具、向用户透明报告，而不是只列接口。
- 验证：
  - `python -m py_compile tools/workflows/agv_three_sim_demo.py tools/agent_cli/coral_agent.py` 成功。
  - `dotnet build` 在 `3rd/CoralinkerHost` 成功，0 error。
  - `ReadLints` 检查 `HomeView.vue`、`KitDocsService.cs` 无错误。
  - 剩余 warning 为项目既有 nullable/未使用/unreachable code warning。
- 接下来：
  - 后续发布新包后，可再次启动无上下文 Agent，观察它是否先下载 bundle、是否优先运行 `tools/workflows/agv_three_sim_demo.py`，以及是否主动向用户报告计划/进度/验证。

## 2026-06-04 10:43 UTC+8 — 让工具入口和反馈协议在 09/10 文档中更显眼

- 用户反馈：
  - `09-agent-workflows.md` 和 `10-agent-api.md` 中仍有大量 GET/POST/JSON 内容，弱 Agent 可能直接照抄 curl。
  - Python 工具路径不够显眼，SubAgent 不容易发现已有 `coral_agent.py` 和 workflows。
  - 需要教 SubAgent 主动发现 workflow 和系统问题，知道意见建议记录到哪里、怎么反馈给开发团队。
- 修改：
  - `3rd/CoralinkerKitDocs/09-agent-workflows.md`
    - 在“工具优先规则”中新增固定工具清单表：
      - `tools/agent_cli/coral_agent.py`
      - `tools/workflows/deploy_logic.py`
      - `tools/workflows/safe_stop_build_program_start.py`
      - `tools/workflows/add_nodes_and_program.py`
      - `tools/workflows/configure_root_and_controls.py`
      - `tools/workflows/agv_three_sim_demo.py`
      - `tools/workflows/debug_serial.py`
      - `tools/workflows/debug_can_wiretap.py`
    - 增加常见命令示例，特别是 `state`、`files sync`、`agv_three_sim_demo.py`。
    - 新增“Agent 反馈协议”：
      - 主动检查文档、工具、Root/MCU 边界、API 错误、warning、刷新顺序、用户可理解性。
      - 反馈文件写入 `ai-deck/agent_feedback/YYYYMMDD-HHMMSS-brief-topic.md`。
      - 提供反馈文件模板和最终回复要求。
    - 修正有序列表编号风格，消除 markdownlint warning。
  - `3rd/CoralinkerKitDocs/10-agent-api.md`
    - 开头明确本文是 API 参考，不是默认操作教程。
    - 明确默认流程是下载 bundle -> 读 tools README -> 优先 CLI/workflow -> 最后才直接 GET/POST。
    - 第一屏新增 “Python 工具入口” 和常用 CLI/workflow 命令。
    - 明确不要因为文档列出 GET/POST/JSON 就默认 curl 或手写 HTTP。
  - `3rd/CoralinkerKitDocs/tools/README.md`
    - 新增 “Feedback To Developers”。
    - 说明复杂任务后应把问题和建议写入 `ai-deck/agent_feedback/...` 并在最终回复中总结。
  - 新增 `ai-deck/agent_feedback/desc.md`
    - 说明本目录用于记录外部 Agent/SubAgent 使用文档、API、CLI、workflow 的问题、建议和复现步骤。
- 【发现】API 参考文档必须主动声明“不要从这里开始操作”，否则 Agent 会把参考文档当教程，按 GET/POST 顺序写 curl。
- 【注意】反馈协议不能只写在最终 prompt 里，必须发布到 docs/tools 中，让客户 Agent 自己知道如何把问题带回开发团队。
- 验证：
  - `ReadLints` 检查 `09-agent-workflows.md`、`10-agent-api.md`、`tools/README.md`、`ai-deck/agent_feedback/desc.md` 无错误。
  - `python -m py_compile tools/agent_cli/coral_agent.py tools/workflows/agv_three_sim_demo.py` 成功。

## 2026-06-04 10:47 UTC+8 — 全目录审查 GET/POST 并改为 Python 工具优先

- 用户反馈：
  - 需要检查 `3rd/CoralinkerKitDocs` 全部文档里仍存在 `GET`/`POST` 的地方。
  - 判断是否已有 Python 脚本覆盖；如果 Python 能节省大量 token，则教程应优先调用 Python。
- 执行：
  - 使用全文搜索扫描发布文档中的 `GET /api...`、`POST /api...`、`/api/...`。
  - 分类结果：
    - `10-agent-api.md` 是 API 参考，保留 GET/POST，但第一屏必须明确 CLI/workflow 优先。
    - `_write_request/` 是不发布的写作记录，保留历史 API 需求。
    - `README.md` 的少量 GET/POST 是入口和 fallback 说明。
    - `03/04/07/08/09/tools README` 中可由 Python 覆盖的操作流程应改成 Python 优先。
- 修改：
  - `3rd/CoralinkerKitDocs/tools/agent_cli/coral_agent.py`
    - 新增 `project new`。
    - 新增 `node probe/list/states/state/remove`。
    - 新增 `variables meta/values/flow/set`。
    - 新增 `logs terminal/build/root`。
    - 新增 `errors fatal`。
    - 新增 `logic list`。
    - 这些命令覆盖大部分原先文档中的 GET/POST 查询和常见动作。
  - `3rd/CoralinkerKitDocs/tools/README.md`
    - 常用命令增加 project/state/files snapshot/node/variables/logs/errors。
    - Root 和 MCU 差异改成 CLI 命令示例，不再直接展示 POST/GET。
  - `3rd/CoralinkerKitDocs/04-variables-and-io.md`
    - 变量 meta/values/flow、变量 set、节点 states/state 改为 `coral_agent.py` 命令优先。
  - `3rd/CoralinkerKitDocs/07-node-management.md`
    - 真实节点 probe/add/configure/info/states 改为 CLI 优先。
    - 模拟节点 add-simulated 改为 CLI 优先。
    - 多节点刷新、删除节点和刷新变量 flow 改为 CLI 优先。
  - `3rd/CoralinkerKitDocs/08-faq.md`
    - Root 编程误解、PartialFailure、stop 等排障步骤改为 CLI/workflow 优先。
    - portConfigs 局部修改改为 `node configure-port` 示例。
  - `3rd/CoralinkerKitDocs/00-system-overview.md`
    - 文件 sync/snapshot、节点 layout 查看、聚合刷新改为 CLI 命令描述。
  - `3rd/CoralinkerKitDocs/03-build-and-deploy.md`
    - 从“Agent API 流程”改为“Agent 工具流程”。
    - 保存、build、program、start、stop、刷新全部改为 CLI 命令。
  - `3rd/CoralinkerKitDocs/09-agent-workflows.md`
    - 读取系统、文件 sync、build、真实/模拟节点、多节点 program、Root configure/control、start/stop、变量、WireTap、fatal error 全部改为 CLI/workflow 优先。
    - 只保留少量 `/api/...` 作为 bundle fallback、ping 排查和 Root 禁止 node program 的概念说明。
  - `3rd/CoralinkerKitDocs/10-agent-api.md`
    - 保持 API 参考定位。
    - 第一屏补充更完整的 CLI 覆盖说明，明确 CLI 覆盖文档下载、项目新建、状态聚合、文件、build、logic list、节点、Root、variables、logs、wiretap、fatal errors。
  - `3rd/CoralinkerKitDocs/README.md`
    - 启动强制步骤改为先给 `coral_agent.py docs download --bundle` 命令。
- 【发现】仅仅在文档中写“不要 curl”不够；必须把教程正文中的操作步骤替换成 Python 命令，否则 Agent 看到 GET/POST 会自然模仿。
- 【注意】`10-agent-api.md` 仍会包含大量 GET/POST，这是它作为 API reference 的职责；关键是它第一屏必须声明“不是默认教程”，并给出 Python 等价入口。
- 验证：
  - `ReadLints` 检查 `3rd/CoralinkerKitDocs` 和 `ai-deck/agent_feedback/desc.md` 无错误。
  - `python -m py_compile` 覆盖全部 Agent CLI/workflow 脚本成功。
  - `dotnet build` 在 `3rd/CoralinkerHost` 成功，0 error，仅剩既有 `DiverCompiler/Processor.cs` unreachable code warning。

## 2026-06-03 15:48 UTC+8 — 缩短 Agent Prompt 并补充硬件信息追问规则

- 用户反馈：
  - 网页 Agent 弹窗里的建议 Prompt 与 Docs 内容有重复，整体偏长。
  - 实际使用时用户还会继续输入任务补充。
  - 例如真实连接的灯、按键、驱动器等硬件信息，需要其他 Agent 按用户补充输入工作。
  - 如果用户补充不够，Agent 应该要求用户继续补充，而不是猜测。
- 修改：
  - `3rd/CoralinkerHost/ClientApp/src/views/HomeView.vue`
    - 缩短 `agentSuggestedPrompt`。
    - Prompt 现在只保留核心导向：
      - 从 `${agentDocsUrl.value}` 开始阅读文档并按 Agent 工作流操作。
      - 用户后续补充的实际任务和硬件连接信息优先。
      - 硬件信息不足时先追问，不猜测接线、端口参数和控制方向。
    - 去掉原来与 Docs 重复的逐条 API/流程说明。
  - `3rd/CoralinkerKitDocs/09-agent-workflows.md`
    - 总规则增加：
      - 用户补充输入优先于示例代码。
      - 涉及真实硬件时不要猜测灯、按键、驱动器、传感器、端口、协议、电平、方向和安全限制。
      - 信息不足以安全编程、编程节点或验证结果时，先向用户追问。
    - 新增“用户补充输入和硬件信息”章节。
    - 明确真实硬件任务至少确认：
      - 设备类型。
      - 节点、端口、通道。
      - 串口/CAN/IO 参数。
      - 控制变量和反馈变量命名。
      - 安全限制。
      - 验证方式。
    - 增加具体追问示例。
  - `3rd/CoralinkerKitDocs/README.md`
    - Agent 必须知道的规则中增加：
      - 用户补充的实际任务和硬件连接信息是任务输入的一部分。
      - 信息不足时先追问，不要猜测真实硬件参数。
- 验证：
  - `ReadLints` 检查 `HomeView.vue` 无错误。
  - `npm run build` 成功。
  - 剩余 warning 为既有：
    - SignalR pure annotation comment warning。
    - `device.ts` 同时 dynamic/static import chunk warning。
    - 大 chunk size warning。

## 2026-06-03 15:50 UTC+8 — 发布包含缩短 Prompt 和硬件补充规则的新 Host 包

- 用户需求：
  - 对刚才缩短 Agent Prompt、补充硬件信息追问规则的版本执行 publish。
- 执行：
  - 运行 `3rd/CoralinkerHost/publish-host.ps1 -SkipNativeBuild -NoRestore`。
  - 发布脚本完成：
    - 前端 `npm run build`。
    - `dotnet publish`。
    - Kit docs 发布。
    - `publish-info.json` 生成。
- 输出目录：
  - `3rd/CoralinkerHost/Publish/CoralinkerHost_c7f234b_20260603-155042`
- 验证：
  - 发布命令退出码为 0。
  - 已确认发布包关键文件存在：
    - `publish-info.json`
    - `CoralinkerHost.dll`
    - `wwwroot/index.html`
    - `wwwroot/docs/kit/README.html`
    - `wwwroot/docs/kit/09-agent-workflows.html`
    - `res/docs/kit/md/README.md`
    - `res/docs/kit/md/09-agent-workflows.md`
    - `simnode/CoralinkerSimNodeHost.dll`
- 注意：
  - 剩余 warning 为既有：
    - Vite/Rollup SignalR pure annotation warning。
    - `device.ts` dynamic/static import chunk warning。
    - 大 chunk size warning。
    - `DiverCompiler/Processor.cs` unreachable code warning。

## 2026-06-04 16:48 UTC+8 — VarFlow 自动排序与同关系变量分组框

- 用户需求：
  - 取消变量拖动排序功能，避免用户手动排列不清楚。
  - 新变量出现后自动触发重新排序和布局计算。
  - Root 与节点之间的横排变量按关系优化，例如双节点时希望接近 `ToA FromA ToB FromB`，减少线交叉。
  - 多个同方向、同来源、同去向变量不要一字排开；改为竖向堆叠，外面加一个大框；每个大框最多 6 个变量。
  - 出现合并变量大框时，Root 和节点之间的竖向间距需要随分组高度自动增大。
- 修改：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`
    - 新增 `VariableFlowGroup` 输出，描述横排变量的分组框。
    - 固定布局入口改为 `autoOrderVariables()` 自动排序，不再依赖 `projectStore.variableFlowOrder`。
    - 自动排序按变量关系计算 key：优先按相邻节点通信、节点顺序、Root 写/Root 读角色、变量方向排序。
    - `layoutExternalVariables()` 改为输出 `{ items, groups, height }`。
    - 同 `direction + writerIds + readerIds` 的变量会形成同一列，每列最多 6 个变量；超过 6 个拆成下一列。
    - 分组列使用 `FLOW_GROUP_PADDING`、`FLOW_GROUP_ITEM_GAP`、`FLOW_GROUP_GAP` 控制内部堆叠和列间距。
    - 主关系线的路由目标改为 `groups + 未分组 items`；带 `groupId` 的变量不再单独产生主关系线，避免同一组重复拉多条长线。
    - 分组高度参与节点层 Y 坐标计算，使 Root 与节点之间间距随竖排变量自动增大。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`
    - 新增 `.variable-flow-group` 渲染和颜色样式。
    - 移除变量卡片 `draggable`、`dragstart/drop/dragend`、`draggedVariableName` 和 `saveVariableFlowOrder()`。
    - 移除变量拖拽排序样式。
    - 移除监听 `variableFlowLayout.value.variableOrder` 后写回项目的 watcher。
    - `flowCanvasSize` 纳入分组框边界，避免分组框超出画布尺寸计算。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts`、`GraphCanvas.vue` 无错误。
  - `npx vue-tsc -b` 通过。
  - `npx vite build` 通过。
  - `git diff --check -- "3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts" "3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue"` 通过，仅有 LF/CRLF 提示。
- 注意：
  - Vite warning 为既有构建提示：
    - SignalR pure annotation comment。
    - `device.ts` dynamic/static import chunk warning。
    - 大 chunk size warning。

## 2026-06-04 16:53 UTC+8 — 使用 Kit Docs Python CLI 创建双模拟节点 VarFlow 测试工程

- 用户需求：
  - 在 `localhost:4499` 新建一个工程试一下。
  - 使用两个模拟节点，每个节点多弄几个变量。
  - 按 `3rd/CoralinkerKitDocs` 的 Python API/CLI 工作流操作。
- 执行：
  - 下载/刷新 Kit Docs 工具包：
    - `python 3rd/CoralinkerKitDocs/tools/agent_cli/coral_agent.py --host http://localhost:4499 docs download --out ai-deck/kit-docs --bundle`
  - 读取状态：
    - `python ai-deck/kit-docs/tools/agent_cli/coral_agent.py --host http://localhost:4499 state`
  - 创建临时工作目录：
    - `ai-deck/agent_work/20260604-1653-varflow-two-sim/`
  - 新增临时草稿：
    - `ai-deck/agent_work/20260604-1653-varflow-two-sim/desc.md`
    - `ai-deck/agent_work/20260604-1653-varflow-two-sim/VarFlowTwoSim.cs`
  - 使用 CLI 新建工程、同步源码、编译：
    - `project new`
    - `files sync --path assets/inputs/VarFlowTwoSim.cs --from-file ai-deck/agent_work/20260604-1653-varflow-two-sim/VarFlowTwoSim.cs --message "add varflow two sim test"`
    - `build`
  - Build 结果：
    - `sourceCommitShort=48e3559`
    - `buildId=20260604-085616`
    - MCU artifacts：`VarFlowNodeALogic`、`VarFlowNodeBLogic`
    - Root logics：`VarFlowRootLogic`
  - 添加模拟节点：
    - `VarFlow Sim A`：`04b7b024-8a6e-49f1-ad99-0602956df4ef`
    - `VarFlow Sim B`：`2832b8fb-f371-46f6-a744-996b47ca93ee`
  - 编程和启动：
    - A -> `VarFlowNodeALogic`，`programSize=785`
    - B -> `VarFlowNodeBLogic`，`programSize=586`
    - Root -> `VarFlowRootLogic`
    - `start --require-all` 成功，`successNodes=2/2`
- 验证：
  - `variables flow` 返回 Root Runtime、两个模拟节点和多组变量关系。
  - Root -> A 变量：`ToA1`...`ToA7` 由 `root-runtime` 写、A 读；`ToA8` 是 Root 发布但无人读，用于测试无消费者布局。
  - Root -> B 变量：`ToB1`...`ToB6` 由 `root-runtime` 写、B 读；`ToB7` 是 Root 发布但无人读。
  - A -> Root 变量：`FromA1`...`FromA5` 由 A 写、Root 读。
  - B -> Root 变量：`FromB1`...`FromB6` 由 B 写、Root 读。
  - A -> B 变量：`AToB1`...`AToB3` 由 A 写、B 读。
  - `node states` 返回两个模拟节点均：
    - `runState=running`
    - `isConnected=true`
    - `isConfigured=true`
    - `isProgrammed=true`
- 注意：
  - `/api/about` 显示当前 `localhost:4499` 是 `Published` 布局，frontend/backend buildTime 为 `2026-06-04T12:14:53`。
  - 该运行包早于 16:48 的 VarFlow 前端分组框修改；后端工程和变量关系已准备好，但网页要看到最新分组框布局，需要重新发布/重启 Host 到最新前端构建。

## 2026-06-04 17:00 UTC+8 — 修复 VarFlow 分组后 Root 槽位顺序导致的线交叉

- 用户反馈：
  - 截图中 `ToA7` 和 `FromA1..FromA5` 聚合之间发生线交叉。
  - 用户判断为 `ToA7` 与 `FromA1..FromA5` 聚合的顺序不对。
- 根因：
  - `routeVariableLines()` 为主关系线分配 Root 底边槽位时，使用 `groups + items.filter(!groupId)` 的数据顺序。
  - 分组框和未分组单变量列在 DOM/视觉上已经按 X 排列，但连线槽位没有按 X 重新排序。
  - 当同一关系超过 6 个变量被拆成多列时，例如 `ToA1..ToA6` 聚合框 + `ToA7` 单变量，`ToA7` 的视觉位置在 `FromA` 之前，但 Root 出线槽位可能被排到 `FromA` 之后，于是线交叉。
- 修改：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`
    - `routeItems` 改为：
      - 合并 `groups` 与未分组 items。
      - 通过 `compareRouteItemPosition()` 按 `x + width / 2` 从左到右排序。
      - 同 X 时按 `y` 排序。
    - Root bottom slots 与 node top slots 都基于排序后的视觉顺序分配。
- 验证：
  - `ReadLints` 检查 `variableFlowLayout.ts` 无错误。
  - `npx vue-tsc -b` 通过。
- 注意：
  - 这是前端源码修复；如果用户正在看 `localhost:4499` 的旧 Published 包，需要重新构建/发布或启动最新前端后才能看到效果。

## 2026-06-04 17:03 UTC+8 — 取消 Graph 节点拖动换序和顺序持久化

- 用户需求：
  - 取消 Node 自由拖动特性。
  - Project 不再保存和解析节点顺序。
  - 节点直接按照创建顺序渲染。
- 修改：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`
    - `VueFlow` 的 `nodes-draggable` 从 `!isRunning` 改为固定 `false`。
    - 删除 `@node-drag-stop` 事件绑定。
    - `applyFixedLayout()` 中 Coral Node 固定 `draggable: false`，运行/停止状态不再改变节点拖动能力。
    - 固定布局输入中不再传节点 `order`，`nodeOrder` 固定为空数组。
    - 删除 `readNodeOrder()` 和 `currentNodeOrder()`，不再读取 `extraInfo.order`。
    - 删除 `saveNodeOrder()`，不再通过 `configureNode(uuid, { extraInfo })` 保存排序。
    - 删除 `targetNodeOrderIndex()` 和 `applyNodeOrderLocally()`，不再支持拖动交换顺序。
    - `addNode()` 不再写入 `extraInfo: { order }`，添加节点后只按数组追加顺序布局。
    - `loadFromStore()` 不再把后端 `extraInfo` 放入节点 data，也不再解析历史排序信息。
    - `removeNode()` 删除节点后只重新应用固定布局，不再重写剩余节点顺序。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue` 无错误。
  - `npx vue-tsc -b` 通过。
  - `npx vite build` 通过。
  - `rg` 确认 `GraphCanvas.vue` 中无 `readNodeOrder/currentNodeOrder/saveNodeOrder/targetNodeOrderIndex/applyNodeOrderLocally/onNodeDragStop/extraInfo/NODE_SIZE` 残留引用。
  - `git diff --check -- "3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue"` 通过，仅有 LF/CRLF 提示。
- 注意：
  - Vite warning 为既有构建提示：
    - SignalR pure annotation comment。
    - `device.ts` dynamic/static import chunk warning。
    - 大 chunk size warning。

## 2026-06-04 17:12 UTC+8 — ControlPanel 绑定变量特殊样式与变量框尺寸统一

- 用户需求：
  - 被遥控器面板（ControlPanel）绑定的变量需要特殊表示，方便直观看到哪些变量受遥控面板绑定。
  - 样式可以是变量框背景颜色带条纹渐变。
  - 变量框最小宽度需要加宽，避免 float 值变长时频繁重新调整宽度。
  - ControlItem 和普通变量的高度、间距 CSS 不一致，需要统一。
- 修改：
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts`
    - `FLOW_ITEM_WIDTH`：`210 -> 220`。
    - `CONTROL_ITEM_MIN_WIDTH`：`124 -> 168`。
    - `CONTROL_ITEM_MAX_WIDTH`：`220 -> 260`。
    - `FLOW_ITEM_MIN_WIDTH`：`132 -> 168`。
    - 保持 `FLOW_ITEM_HEIGHT=38`，通过更宽 min width 减少短变量名 + float 值变化导致的宽度抖动。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`
    - 新增 `controlPanelBoundVariables` computed。
    - 从 `projectStore.controlLayout.widgets` 收集绑定变量：
      - `config.variable`：slider/switch/gauge/lamp 等。
      - `config.variableX`、`config.variableY`：joystick。
    - 模板中对绑定变量框添加 `control-panel-bound` class。
    - `.variable-flow-item.control-panel-bound` 增加更亮描边和外发光。
    - `.variable-flow-item.control-panel-bound::before` 增加斜向 repeating-linear-gradient 条纹覆盖层。
    - `.variable-flow-item` 增加 `overflow: hidden`，保证条纹被圆角裁剪。
    - `.var-type/.var-name/.var-value` 增加相对定位和 `z-index: 1`，确保文字在条纹层上方。
    - 移除 `.variable-flow-item.control-io` 的 `min-height: 30px`、`grid-template-rows: 13px 13px`、`font-size: 10px`，让 ControlItem 与普通变量使用一致高度、行高和字号。
- 验证：
  - `ReadLints` 检查 `GraphCanvas.vue`、`variableFlowLayout.ts` 无错误。
  - `npx vue-tsc -b` 通过。
  - `npx vite build` 通过。
  - `git diff --check -- "3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue" "3rd/CoralinkerHost/ClientApp/src/components/graph/variableFlowLayout.ts"` 通过，仅有 LF/CRLF 提示。
- 注意：
  - Vite warning 为既有构建提示：
    - SignalR pure annotation comment。
    - `device.ts` dynamic/static import chunk warning。
    - 大 chunk size warning。

## 2026-06-04 17:18 UTC+8 — ControlPanel 变量支持从 Graph 变量框拾取

- 用户需求：
  - 遥控器绑定变量时变量列表太长，实际项目可能有 40 多个变量。
  - 变量列表右侧增加 `Select From Graph` 按钮。
  - 点击后 ControlPanel 更透明，焦点来到 Graph。
  - 允许选择 Graph 中的变量框作为绑定对象。
  - 能绑定的变量保持彩色，不能绑定的变量变灰。
  - Hover 时颜色更加加深。
  - 单击变量后绑定成功；点击别的地方绑定失败/取消。
- 修改：
  - `3rd/CoralinkerHost/ClientApp/src/stores/ui.ts`
    - 新增 `GraphVariablePickRequest` / `GraphVariablePickResult` 类型。
    - 新增 `graphVariablePickRequest` / `graphVariablePickResult` 状态。
    - 新增 `startGraphVariablePick(allowedNames, label)`：
      - 创建 request id。
      - 保存可绑定变量名集合。
      - 切换到 Graph view。
    - 新增 `finishGraphVariablePick(variableName)`：
      - 写入 pick result。
      - 清除 active request。
    - 新增 `cancelGraphVariablePick()` 和 `clearGraphVariablePickResult()`。
  - `3rd/CoralinkerHost/ClientApp/src/components/control/ControlWindow.vue`
    - 在以下变量选择行右侧添加 `Select From Graph`：
      - joystick `variableX`
      - joystick `variableY`
      - slider `variable`
      - switch `variable`
      - gauge `variable`
      - lamp `variable`
    - 新增 `pendingGraphPick`，记录 request id 和待写入字段。
    - 新增 `startGraphPick(field, scope)`：
      - `scope='controllable'` 使用 `controllableVarList`，用于 joystick/slider/switch。
      - `scope='all'` 使用 `allVarList`，用于 gauge/lamp。
    - 新增 `applyGraphPickedVariable()`：
      - 写回 `editingWidget.config[field]`。
      - joystick X/Y 与 slider 会刷新默认范围。
    - 监听 `uiStore.graphVariablePickResult`，匹配 request id 后写回变量或取消。
    - 关闭配置弹窗、关闭 ControlWindow、组件卸载时取消未完成拾取。
    - 拾取期间 `.control-window` 和 `.config-dialog-overlay` 半透明，配置弹窗不再截获点击。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`
    - 新增 `graphVariablePickActive` 和 `graphVariablePickAllowedNames`。
    - Variables Flow layer 在拾取模式下开启 pointer events。
    - 变量框增加：
      - `graph-pick-bindable`
      - `graph-pick-disabled`
    - 可绑定变量保持原色，hover 增亮、轻微上移并加高亮阴影。
    - 不可绑定变量 grayscale/变暗，hover 仅轻微恢复亮度。
    - 点击可绑定变量调用 `finishGraphVariablePick(name)`。
    - 点击不可绑定变量或空白区域调用 `finishGraphVariablePick(null)`。
- 验证：
  - `ReadLints` 检查 `ui.ts`、`ControlWindow.vue`、`GraphCanvas.vue` 无错误。
  - `npx vue-tsc -b` 通过。
  - `npx vite build` 通过。
  - `git diff --check -- "3rd/CoralinkerHost/ClientApp/src/stores/ui.ts" "3rd/CoralinkerHost/ClientApp/src/components/control/ControlWindow.vue" "3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue"` 通过，仅有 LF/CRLF 提示。
- 注意：
  - Vite warning 为既有构建提示：
    - SignalR pure annotation comment。
    - `device.ts` dynamic/static import chunk warning。
    - 大 chunk size warning。

## 2026-06-04 17:42 UTC+8 — 修正 Graph 变量拾取模式遮挡与缩放控件

- 用户反馈：
  - 进入拾取模式后，ControlPanel 仍在前台，遮住了 Graph。
  - 应该全面隐藏 ControlPanel 和 Configure Panel 配置页面。
  - Graph 内除了变量框以外的东西需要加一层蒙影。
  - 左下角 `+/-/[]` 缩放控件在进入拾取模式后不可用了，需要保持可用。
- 根因：
  - 之前只把 ControlPanel 和配置弹窗做半透明，但它们仍然保留在高 z-index 前台。
  - Variables Flow layer 在拾取模式下启用了整层 `pointer-events: auto`，覆盖了 VueFlow 的缩放控件。
- 修改：
  - `3rd/CoralinkerHost/ClientApp/src/components/control/ControlWindow.vue`
    - ControlWindow 根节点改为 `v-if="visible && !graphVariablePickActive"`。
    - 拾取模式中 ControlPanel 与配置弹窗完整不渲染，不再遮挡 Graph。
  - `3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue`
    - Graph wrapper 增加 `@click.capture="handleGraphCanvasClickCapture"`。
    - 新增 `graph-pick-dim` 蒙影层：
      - z-index 位于 VueFlow 内容之上、变量层之下。
      - `pointer-events: none`，只负责视觉变暗，不拦截交互。
    - Variables Flow layer 在拾取模式下保持 `pointer-events: none`，避免覆盖左下角缩放控件。
    - 变量框自身仍保持 `pointer-events: auto`，可以点击选择。
    - `handleGraphCanvasClickCapture()`：
      - 点击变量框：不取消，交给变量框 click 处理。
      - 点击 `.vue-flow__controls` / `.vue-flow__minimap`：不取消，保证缩放/视图控件可用。
      - 点击其他区域：取消拾取。
- 验证：
  - `ReadLints` 检查 `ControlWindow.vue`、`GraphCanvas.vue` 无错误。
  - `npx vue-tsc -b` 通过。
  - `npx vite build` 通过。
  - `git diff --check -- "3rd/CoralinkerHost/ClientApp/src/components/control/ControlWindow.vue" "3rd/CoralinkerHost/ClientApp/src/components/graph/GraphCanvas.vue"` 通过，仅有 LF/CRLF 提示。
- 注意：
  - Vite warning 为既有构建提示：
    - SignalR pure annotation comment。
    - `device.ts` dynamic/static import chunk warning。
    - 大 chunk size warning。

## 2026-06-04 18:35 UTC+8 — 发布最新 CoralinkerHost 包

- 用户需求：
  - “现在打包一个新的publish”。
- 执行：
  - 在 `3rd/CoralinkerHost` 运行：
    - `.\publish-host.ps1 -SkipNativeBuild`
  - 选择 `-SkipNativeBuild` 的原因：
    - 本轮后续修改主要是前端交互和 Host 层逻辑。
    - 没有修改 native runtime / sim node native shim。
    - 复用已有 SimNode native runtime 产物，缩短发布时间。
- 发布输出：
  - `3rd/CoralinkerHost/Publish/CoralinkerHost_c7f234b_20260604-183507`
- 发布信息：
  - `configuration=Release`
  - `tag=c7f234b-dirty`
  - `commit=c7f234b`
  - `commitTime=2026-06-03T12:37:46+08:00`
  - `publishTime=2026-06-04T18:35:07.2618241+08:00`
  - `skipNativeBuild=true`
  - `nativeBridgeRuntimes=win-x64, linux-x64, linux-arm64`
  - `nativeSimNodeRuntimes=win-x64, linux-x64, linux-arm64`
- 验证：
  - 发布命令退出码为 0。
  - 已读取 `publish-info.json`。
  - 已确认关键文件存在：
    - `publish-info.json`
    - `CoralinkerHost.dll`
    - `wwwroot/index.html`
    - `wwwroot/assets`
    - `res/docs/kit/md/README.md`
    - `wwwroot/docs/kit/README.html`
    - `simnode/CoralinkerSimNodeHost.dll`
- 注意：
  - Vite warning 为既有构建提示：
    - SignalR pure annotation comment。
    - `device.ts` dynamic/static import chunk warning。
    - 大 chunk size warning。
  - dotnet publish 仍有既有 nullable/unused field warnings，未阻塞发布。

## 2026-06-05 12:08 UTC+8 — 修复全新离线部署 Host Build restore 缺包

- 用户需求：
  - 分析并解决 `ai-deck/HOST_BUILD_RESTORE_ISSUE.md` 中同事 Agent 反馈的问题：
    - `192.168.1.101` 只部署 0605 离线包后 Build restore 失败。
    - `192.168.1.102` 同源代码/同发布包可成功，区别是 1.102 曾经运行过在线 restore 版本。
- 现象：
  - 1.101 restore 报 `NU1101`，找不到多个 `runtime.<platform>.runtime.native.System.IO.Ports` 包。
  - 旧发布包 `publish-info.json` 只列出：
    - `system.io.ports/9.0.3`
    - `runtime.native.system.io.ports/9.0.3`
- 根因：
  - `runtime.native.System.IO.Ports/9.0.3` 是 meta package。
  - 它还依赖多个 runtime-specific package：
    - `runtime.linux-x64.runtime.native.system.io.ports/9.0.3`
    - `runtime.linux-arm64.runtime.native.system.io.ports/9.0.3`
    - 以及 Android、Linux musl/bionic、macOS、Mac Catalyst 等同版本 runtime 包。
  - 1.102 成功是因为历史在线 restore 已把这些包放进全局 NuGet cache。
  - 1.101 全新离线部署只有发布包内离线源，所以无法解析这些 transitive runtime 包。
- 修改：
  - `3rd/CoralinkerHost/publish-host.ps1`
    - `offlinePackageSpecs` 增加全部 16 个 `runtime.*.runtime.native.system.io.ports/9.0.3` 包。
    - 发布时把这些包复制到 `res/compiler/nuget-packages/`。
    - `publish-info.json` 的 `offlineNuGetPackages` 会同步记录这些包。
- 发布：
  - 在 `3rd/CoralinkerHost` 执行：
    - `.\publish-host.ps1 -SkipNativeBuild`
  - 输出：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_7b57695_20260605-120558`
  - 说明：
    - 本次只修改 Host 发布包的离线 NuGet 包收集逻辑，未改 native runtime，因此使用 `-SkipNativeBuild`。
- 验证：
  - 本机 NuGet cache 中确认 16 个 runtime-specific `System.IO.Ports` 包存在。
  - 读取新包 `publish-info.json`，确认 `offlineNuGetPackages` 已列出这些 runtime-specific 包。
  - `Glob` 确认新发布包 `res/compiler/nuget-packages` 下存在 17 个 `runtime.*system.io.ports` nuspec（含 meta package）。
  - 新建临时探针：
    - `ai-deck/agent_work/20260605-offline-restore-probe/OfflineRestoreProbe.csproj`
    - `ai-deck/agent_work/20260605-offline-restore-probe/desc.md`
  - 使用干净包目录验证：
    - `NUGET_PACKAGES` 指向全新空目录。
    - NuGet source 只指定 `CoralinkerHost_7b57695_20260605-120558/res/compiler/nuget-packages`。
    - `dotnet restore` 成功。
- 注意：
  - 尝试更新 `ai-deck/HOST_BUILD_RESTORE_ISSUE.md` 时遇到 permission denied，未强行修改。
  - 发布过程中 Vite warning 仍为既有提示：
    - SignalR pure annotation comment。
    - `device.ts` dynamic/static import chunk warning。
    - 大 chunk size warning。
  - dotnet publish 仍有既有 nullable/unused field warnings，未阻塞发布。

## 2026-06-05 12:12 UTC+8 — 离线 Build 包闭包与日志友好度评估

- 用户问题：
  - 离线编译用户代码是否还会遇到其他 NuGet 包。
  - 当前把离线包列表直接写在 `publish-host.ps1` 是否合理，是否应该单独配置。
  - 用户 Agent 反复多次才发现缺包，是否能让 Build 日志更友好。
- 当前观察：
  - `DiverBuildService` 生成临时 `LogicBuild.csproj` 时，通过 `res/compiler/build-packages.json` 加载顶层 `PackageReference`。
  - `publish-host.ps1` 同时维护一份 `$defaultBuildPackages` 和一份 `$offlinePackageSpecs`。
  - `$defaultBuildPackages` 是顶层依赖：
    - `Fody/6.6.4`
    - `Newtonsoft.Json/13.0.3`
    - `System.IO.Ports/9.0.3`
    - `System.Management/9.0.4`
  - `$offlinePackageSpecs` 是离线源复制清单，目前仍需要手写 transitive/runtime 包。
  - `DiverBuildService` 的 restore 阶段当前没有把 stdout/stderr 写入 `build.log`，失败时只输出 `RESTORE FAILED with exit code ...`，并抛出空 log tail。
- 风险判断：
  - 用户逻辑中直接使用 .NET BCL 类型通常不需要额外 NuGet。
  - 离线缺包风险主要来自固定顶层包的 transitive dependencies、runtime-specific packages、build/analyzer packages。
  - 继续手写 `$offlinePackageSpecs` 容易再次漏包，尤其是类似 `runtime.native.*` 的 meta package。
- 建议：
  - 将 Build 顶层包定义移到独立配置文件，例如 `3rd/CoralinkerHost/packaging/build-packages.json`。
  - 发布脚本读取该文件并生成发布包内 `res/compiler/build-packages.json`。
  - 发布时创建 probe project，执行一次 restore，读取 `project.assets.json` 或 lock file 自动得到完整 package closure。
  - 按 assets 中的包闭包复制 `~/.nuget/packages/<id>/<version>`，不再手写 transitive 包。
  - Restore 失败时：
    - 把 restore stdout/stderr 写入 `build.log`。
    - ring buffer 传入 `BuildFailedException`，让 API/前端能显示最后关键错误。
    - 解析 `NU1101` 等错误，输出“缺少离线 NuGet 包”的摘要，包括缺包 ID、离线源路径、发布包重新生成建议。

## 2026-06-05 12:31 UTC+8 — 实现离线 NuGet 包闭包自动发布与友好 restore 日志

- 用户需求：
  - “好现在修改，验证，发布，验证做完”。
  - 不再长期手写离线 transitive 包清单。
  - 让用户 Agent 更快从 Build 日志中定位 NuGet restore 缺包问题。
- 修改：
  - 新增 `3rd/CoralinkerHost/packaging/build-packages.json`：
    - 作为 Host Build 用户逻辑临时工程的顶层包配置源。
    - 当前包含：
      - `Fody/6.6.4`
      - `Newtonsoft.Json/13.0.3`
      - `System.IO.Ports/9.0.3`
      - `System.Management/9.0.4`
  - 修改 `3rd/CoralinkerHost/publish-host.ps1`：
    - 删除长期手写维护的 runtime-specific `$offlinePackageSpecs` 清单。
    - 读取 `packaging/build-packages.json`。
    - 生成临时 `CoralinkerBuildPackageProbe.csproj`。
    - 执行真实 `dotnet restore`。
    - 解析 `obj/project.assets.json`：
      - 遍历 `libraries` 中 `type=package` 的条目。
      - 读取 package `id/version/path`。
      - 在 NuGet package folders 中定位实际包目录。
    - 复制完整 package closure 到发布包：
      - `res/compiler/nuget-packages/<id>/<version>/`
    - `publish-info.json` 继续记录：
      - 顶层 `buildPackages`
      - 完整 `offlineNuGetPackages`
    - 修复 PowerShell 函数输出污染问题：
      - `dotnet restore` 输出通过 `Write-Host` 打印，不作为函数返回值混入 package list。
    - 修复 PowerShell JSON 数组展开问题：
      - 对 `ConvertFrom-Json` 结果进行显式 array flatten。
  - 修改 `3rd/CoralinkerHost/Services/DiverBuildService.cs`：
    - `build.log` 创建提前到 restore 阶段之前。
    - restore stdout/stderr 写入 `build.log`。
    - restore 和 build 复用同一个日志文件。
    - restore 失败时不再丢弃 tail，而是传入 `BuildFailedException`。
    - 新增 restore 失败提示：
      - 解析 `Unable to find package <id>`。
      - 输出缺失包列表。
      - 输出当前 NuGet source。
      - 输出重新发布 Host 的修复建议。
      - 输出完整日志路径。
- 中途问题与处理：
  - 第一次发布失败：
    - 原因：函数返回中混入 `dotnet restore` 输出，导致 `$offlinePackageSpecs` 包含非 package 对象。
    - 修复：restore 输出管道到 `Write-Host`。
  - 第二次发布失败：
    - 原因：PowerShell 把 JSON 数组作为嵌套数组传入 `ConvertTo-PackageReferenceXml()`，生成错误 `PackageReference Include="Fody Newtonsoft.Json ..."`。
    - 修复：函数入口和 JSON 读取后都做显式 array flatten。
  - 失败的半成品发布目录已删除。
- 验证：
  - `ReadLints` 检查 `DiverBuildService.cs` 无错误。
  - `dotnet build "3rd\CoralinkerHost\CoralinkerHost.csproj" -c Release --no-restore` 通过，仅既有 warning。
  - `publish-host.ps1` PowerShell parser 语法检查通过。
  - 快速 package closure probe：
    - 使用 `packaging/build-packages.json` 生成 probe project。
    - `dotnet restore` 成功。
    - `PACKAGE_CLOSURE_COUNT=22`。
  - 完整发布：
    - `.\publish-host.ps1 -SkipNativeBuild` 成功。
    - 最终包：
      - `3rd/CoralinkerHost/Publish/CoralinkerHost_7b57695_20260605-122827`
  - 最终发布包验证：
    - `publish-info.json` 中 `buildPackages` 为正常数组。
    - `offlineNuGetPackages` 列出 22 个包。
    - `Glob` 确认 `res/compiler/nuget-packages` 下有 22 个 `.nuspec`。
    - 包含 `runtime.linux-x64.runtime.native.system.io.ports/9.0.3`、`runtime.linux-arm64.runtime.native.system.io.ports/9.0.3` 等全部 `System.IO.Ports` runtime-specific 依赖。
  - 干净离线 restore：
    - `NUGET_PACKAGES` 指向全新空目录。
    - NuGet source 仅指定最终发布包 `res/compiler/nuget-packages`。
    - `dotnet restore ai-deck/agent_work/20260605-offline-restore-probe/OfflineRestoreProbe.csproj` 成功。
  - `git diff --check` 检查相关文件通过，仅有 LF/CRLF 提示。
- 注意：
  - 发布过程中的 Vite warning 为既有提示：
    - SignalR pure annotation comment。
    - `device.ts` dynamic/static import chunk warning。
    - 大 chunk size warning。
  - dotnet build/publish nullable、platform compatibility warning 为既有提示，未阻塞。

## 2026-06-05 12:52 UTC+8 — 发布 Host 最小端到端验证

- 用户需求：
  - 在 Windows 工作区 `d:\Documents\Coral\DIVER` 中验证父 Agent 已启动的发布 Host。
  - Host 地址：`http://localhost:4499`。
  - 发布目录：`3rd/CoralinkerHost/Publish/CoralinkerHost_7b57695_20260605-124439`。
  - 使用 CoralinkerKitDocs 的 `agent_cli/coral_agent.py` 完成最小端到端流程。
- 执行：
  - 确认 `ai-deck/kit-docs/tools/agent_cli/coral_agent.py` 不存在，因此使用 `3rd/CoralinkerKitDocs/tools/agent_cli/coral_agent.py`。
  - 创建临时工作目录 `ai-deck/agent_work/20260605-1248-host-e2e/`。
  - 写入 `desc.md` 说明目录用途和文件用途。
  - 写入 `MinimalE2E.cs`，包含：
    - `MinimalE2ECart`：`target` UpperIO，`observed`/`ticks` LowerIO。
    - `MinimalE2ENodeLogic`：读取 `target`，写回 `observed` 和 `ticks`。
    - `MinimalE2ERootLogic`：写入 `target`，读取 `observed` 并更新 `statusText`。
  - 写入 `run-e2e.ps1` 串行调用 CLI，并保存每一步 JSON 响应。
  - 执行流程：
    - `state`
    - `project new`
    - `state`
    - `files sync --path assets/inputs/MinimalE2E.cs`
    - `build`
    - `logic list`
    - `node add-simulated --name "Agent E2E Sim"`
    - `node program --logic MinimalE2ENodeLogic`
    - `root configure --logic MinimalE2ERootLogic`
    - `start --require-all`
    - `state`
    - `node states`
    - `variables flow`
    - `errors fatal`
    - `logs build/root/node`
- 验证结果：
  - build 成功：
    - artifacts: `MinimalE2ENodeLogic`
    - rootLogics: `MinimalE2ERootLogic`
    - 0 Error(s)，45 Warning(s)。
  - 模拟节点 UUID：`835ebf2c-45b9-4e8a-a263-089b63ccad58`。
  - program 成功，`programSize=294`。
  - `start --require-all` 成功，`totalNodes=1`，`successNodes=1`。
  - final state：
    - session `Running` / `isRunning=true`。
    - Root `isRunning=true`，logic `MinimalE2ERootLogic`，`statusText=observed=8`。
    - node `isConnected=true`，`runState=running`，`isConfigured=true`，`isProgrammed=true`。
  - variables flow：
    - Root 写 `target=7`。
    - 节点读 `target`。
    - 节点写 `observed=8` 和 `ticks=8400`。
  - fatal error 为 `null`。
- 【发现】发布包 Host 的 build、模拟节点编程、Root 配置、`start --require-all` 和变量流闭环均可用。
- 【注意】Build 日志仍有 45 条 warning，主要为 DIVER/Fody 编译器信息、stub 字段未赋值、ARM linker RWX segment warning；本次无错误且不阻塞验证。
- 【注意】Root 日志和节点日志为空，但状态、变量流和 fatal error 已足以确认运行成功。
- 接下来：
  - 若需要进一步验证真实硬件，需要用户补充硬件型号、连接、端口、协议和安全限制。
  - 若需要发布包验收归档，可将 `ai-deck/agent_work/20260605-1248-host-e2e/*.json` 作为证据样本保留。

## 2026-06-08 20:20 UTC+8 — 修复 SimNode runtime fatal 静默退出

- 用户反馈：
  - `ai-deck/crash_code.cs` 在真实硬件中会触发 `Null reference`/`Null point` fatal，并且网页会弹窗。
  - Virtual SimNode 跑同类代码时网页没有弹出错误。
  - Snapshot 也不工作，说明 SimNode 节点实际已经不在运行。
- 排查：
  - `CoralinkerSimNodeHost/native/sim_node_runtime.c` 的 `report_error()` 当前行为是：
    - 通过 console callback 输出错误字符串。
    - `exit(2)` 直接退出子进程。
  - `CoralinkerSimNodeHost/Program.cs` 只捕获 P/Invoke 装载类异常，不会拦截 native `exit(2)`。
  - `3rd/CoralinkerSDK/SimulatedMcuNode.cs` 原先只读取 stdout/stderr 事件，没有监控子进程退出，也不会把非预期退出转换为 `OnFatalError`。
  - 真实硬件路径中 `MCUNode` 会经 `RegisterFatalErrorCallback` 触发 `IRuntimeNode.OnFatalError`，再由 `DIVERSession.HandleFatalError()` 统一推送 SignalR `fatalError`。
- 修改：
  - 在 `SimulatedMcuNode` 中增加子进程退出监控 `MonitorProcessExitAsync()`。
  - 在 `Connect()` 时清空 `_expectingProcessExit`、`_fatalReported`、`_lastConsoleMessage`。
  - 在 `Disconnect()` 和主动 kill 前设置 `_expectingProcessExit=true`，避免正常关闭误报 fatal。
  - 收到 `console` event 时保存最后一条 runtime message。
  - 子进程非预期退出时：
    - 更新 `IsRunning=false`。
    - 更新 `State=Error`。
    - 记录 `LastError` 并触发 `OnError`。
    - 构造字符串型 `ErrorPayload`，`CoreDumpLayout=String`，错误字符串包含 exit code 和最后 runtime message。
    - 触发 `OnFatalError`，复用现有 fatal dialog / FatalErrorStore / 节点断开路径。
- 验证：
  - `ReadLints` 检查 `3rd/CoralinkerSDK/SimulatedMcuNode.cs` 无错误。
  - `dotnet build "3rd\CoralinkerHost\CoralinkerHost.csproj" -c Release --no-restore` 成功，只有既有 warning。
  - 创建验证目录：
    - `ai-deck/agent_work/20260608-simnode-fatal-repro/`
  - 写入 `CrashSimRepro.cs`，包含：
    - `CrashCart`
    - `StatusLightLogic`：调用 `RunOnMCU.WriteSnapshot(Snapshots[step])`
    - `CrashRootLogic`
  - 使用隔离 Host 数据目录启动：
    - `CORALINKER_DATA_DIR=ai-deck/agent_work/20260608-simnode-fatal-repro/host-data`
  - 用 `3rd/CoralinkerKitDocs/tools/agent_cli/coral_agent.py` 执行：
    - `project new`
    - `files sync`
    - `build`
    - `node add-simulated`
    - `node program --logic StatusLightLogic`
    - `root configure --logic CrashRootLogic`
    - `start --require-all`
    - `errors fatal`
    - `node states`
    - `logs node`
  - 验证结果：
    - `buildOk=true`
    - `fatalError.seq=1`
    - `errorType=String`
    - `errorString=Simulated node host exited unexpectedly with code 2. Last runtime message: Null reference`
    - node `runState=error`
    - node `isConnected=false`
    - node log 包含：
      - `Null reference`
      - `[Transport] Simulated node host exited unexpectedly with code 2. Last runtime message: Null reference`
      - `FATAL ERROR: Simulated node host exited unexpectedly with code 2. Last runtime message: Null reference`
- 注意：
  - 验证初期旧 Debug Host 进程仍占用 `localhost:4499`，且旧项目数据中的 `LogicBuild_*.dll` 被锁，导致 `project new` 报 `Access to the path ... is denied`。
  - 已通过进程命令行清理所有 `CoralinkerHost` / `CoralinkerSimNodeHost` 相关进程，再用隔离数据目录完成验证。
  - 验证后已停止后台 Host/SimNode 进程。

## 2026-06-08 20:24 UTC+8 — SimNode fatal 内容改为结构化 runtime error

- 用户反馈：
  - 首版修复后网页会弹 fatal，但错误文本显示为：
    - `Simulated node host exited unexpectedly with code 2. Last runtime message: Null reference`
  - 这不应该显示给用户；应该直接显示 `Null reference`。
  - Debug Location 的 `IL Offset=-1` 也不对，真实 MCU 会带 IL Offset，前端可据此跳转源代码。
- 进一步排查：
  - native `report_error(int il_offset, uchar* error_str, int line_no)` 本身已经有真实 `il_offset` 和 `line_no`。
  - 首版 C# 侧只能在进程退出后兜底上报，因此丢失了 IL Offset，并把 transport exit code 拼进了用户错误文本。
- 修改：
  - `3rd/CoralinkerSimNodeHost/native/sim_node_runtime.c`
    - 新增 `SimFatalCb`。
    - `sim_set_callbacks(...)` 追加 `fatal_cb`。
    - `report_error()` 中先调用 `sim_fatal_cb(il_offset, error_str, line_no, sim_tick_ms)`，再保留 console callback 和 `exit(2)`。
  - `3rd/CoralinkerSimNodeHost/McuRuntimeNative.cs`
    - 新增 `FatalCallback(int ilOffset, IntPtr message, int lineNo, uint timestampMs)`。
    - `SetCallbacks()` 追加 fatal callback 参数。
  - `3rd/CoralinkerSimNodeHost/Program.cs`
    - 注册 `FatalCallback`。
    - 新增 `OnFatal()`，输出 NDJSON：
      - `event=fatal`
      - `message`
      - `ilOffset`
      - `lineNo`
      - `mcuTimestampMs`
  - `3rd/CoralinkerSDK/SimulatedMcuNode.cs`
    - `HandleLine()` 增加 `case "fatal"`。
    - `HandleFatalEvent()` 用原始 runtime message、IL Offset、LineNo 构造字符串型 `ErrorPayload`。
    - `MonitorProcessExitAsync()` 在已经收到 fatal 时直接返回，避免 transport exit code 覆盖用户可见 fatal 文本。
- Native 构建：
  - 执行：
    - `powershell -ExecutionPolicy Bypass -File "3rd\CoralinkerSimNodeHost\build-native.ps1"`
  - 结果：
    - `win-x64` 使用 MSVC 成功生成 `sim_node_runtime.dll`。
    - `linux-x64` 使用 Zig target `x86_64-linux-gnu` 成功生成 `.so`。
    - `linux-arm64` 使用 Zig target `aarch64-linux-gnu` 成功生成 `.so`。
  - 为本地开发验证，将 `build/runtimes` 同步到：
    - `3rd/CoralinkerSimNodeHost/bin/Release/net8.0/runtimes`
    - `3rd/CoralinkerHost/bin/Release/net8.0/simnode/runtimes`
- 验证：
  - C# build：
    - `dotnet build "3rd\CoralinkerHost\CoralinkerHost.csproj" -c Release --no-restore`
    - 成功，只有既有 warning。
  - 用隔离数据目录启动 Host：
    - `CORALINKER_DATA_DIR=ai-deck/agent_work/20260608-simnode-fatal-repro/host-data-structured2`
  - 使用 `CrashSimRepro.cs` 和 CLI 复现。
  - 最终 `errors fatal` 结果：
    - `errorString=Null reference`
    - `debugInfo.ilOffset=133`
    - `debugInfo.lineNo=2502`
    - `errorType=String`
    - node `runState=error`
    - node `isConnected=false`
  - node log：
    - `FATAL ERROR: Null reference`
    - `Disconnecting node due to fatal error...`
    - `Null reference`
- 结论：
  - SimNode runtime fatal 已与真实 MCU fatal 路径更一致：用户弹窗显示 runtime 错误本身，并保留 IL Offset 供前端跳源代码。
  - 进程退出监控仍保留作为 native 崩溃或 helper 异常退出的兜底。

## 2026-06-08 22:10 UTC+8 — 生成 Host 发布包和 CORAL-NODE-V2.1 UPG

- 用户需求：
  - 另一个 Agent 已解决 NPE。
  - 重新打包 Host 新版本。
  - 打包一个新的 `CORAL-NODE-V2.1` UPG，用于实际硬件测试。
- 执行前检查：
  - 检查 `CoralinkerHost`、`CoralinkerSimNodeHost`、`scons` 相关进程。
  - 未发现占用进程。
  - 确认目录存在：
    - `3rd/CoralinkerHost`
    - `MCUSerialBridge`
- Host 发布：
  - 首次执行：
    - `powershell -ExecutionPolicy Bypass -File ".\publish-host.ps1"`
    - 工作目录：`3rd/CoralinkerHost`
  - 结果：
    - 失败于 `NETSDK1152`。
  - 原因：
    - 之前本地 SimNode fatal 验证时，手动把 `3rd/CoralinkerSimNodeHost/build/runtimes` 同步到了 `3rd/CoralinkerSimNodeHost/bin/Release/net8.0/runtimes`。
    - `dotnet publish` 同时从 `build/runtimes` 和 `bin/Release/.../runtimes` 收集相同相对路径的 native runtime：
      - `sim_node_runtime.dll`
      - `libsim_node_runtime.so`
    - 因此触发重复发布文件错误。
  - 处理：
    - 删除临时验证目录：
      - `3rd/CoralinkerSimNodeHost/bin/Release/net8.0/runtimes`
  - 第二次执行：
    - `powershell -ExecutionPolicy Bypass -File ".\publish-host.ps1"`
  - 成功输出：
    - `3rd/CoralinkerHost/Publish/CoralinkerHost_66025b5_20260608-220511`
  - 发布信息：
    - `Commit: 66025b5`
    - `Tag: 66025b5-dirty`
    - `Publish time: 2026-06-08T22:05:11.5081610+08:00`
    - `Skip native build: False`
- Host 包验证：
  - 执行：
    - `powershell -ExecutionPolicy Bypass -File ".\start-host.ps1" -CheckOnly`
    - 工作目录：`3rd/CoralinkerHost/Publish/CoralinkerHost_66025b5_20260608-220511`
  - 结果：
    - `.NET runtime and SDK` 检查通过。
    - `Git` 检查通过。
    - required package files 检查通过。
    - package integrity 检查通过。
    - 输出 `Startup checks passed.`
  - 包内容：
    - 根目录：`README.md`、`publish-info.json`、`start-host.bat`、`start-host.ps1`、`start-host.sh`、`app/`、`setup/`、`meta/`
    - `publish-info.json` 大小：4568 bytes。
    - `meta/package-manifest.sha256` 大小：128130 bytes。
    - `app/CoralinkerHost.dll` 大小：470016 bytes。
    - `app/res/compiler/nuget-packages` 下 `.nuspec` 数量：22。
    - `app/simnode/runtimes` 中 SimNode native runtime 数量：3（win-x64、linux-x64、linux-arm64）。
- V2.1 UPG 构建：
  - 执行：
    - `scons BUILD_MCU=1 PDN=CORAL-NODE-V2.1 ENABLE_DIVER_RUNTIME=1 -j 12 debug=1 upg`
    - 工作目录：`MCUSerialBridge`
  - 结果：
    - 构建成功。
    - `firmware.elf/.hex/.bin` 重新生成。
    - UPG 输出：
      - `MCUSerialBridge/build/MCUSerialBridge_CORAL-NODE-V2.1_66025b5__20260608_220615.upg`
  - 构建元数据：
    - `PDN = CORAL-NODE-V2.1`
    - `COMMIT = 66025b5`
    - `BUILD_TIME = 2026-06-08 22:06:15`
    - `LENGTH = 222768`
    - `ADDRESS = 0x00010000`
    - `CRC32 CODE = 0x4ABB2EE2`
  - 固件尺寸：
    - `firmware.elf`: `text=220548`, `data=2204`, `bss=114196`, `dec=336948`, `hex=52434`
    - `firmware.hex`: `222752` bytes reported by `arm-none-eabi-size`
  - UPG 文件：
    - 文件大小：223056 bytes。
    - LastWriteTime：2026-06-08 22:06:15。
    - SHA256：`1B34B0E4D122F4793107A62318C2BF1CE78CD1CD018C45566732E5CFB73BF4C0`
- 注意：
  - Host 发布过程中仍有既有 Vite warning：
    - SignalR pure annotation comment。
    - `device.ts` dynamic/static import chunk warning。
    - chunk size warning。
  - dotnet build 仍有既有 nullable/platform warning。
  - MCU 链接仍有既有 `_close/_fstat/_getpid/_isatty/_kill/_lseek/_read/_write` stub warning 和 RWX segment warning。
  - 以上 warning 均未阻塞发布、完整性检查或 UPG 生成。

---

## 2026-06-08 20:29 — crash_code.cs `Null reference` 根因定位

### [analysis] StatusLightLogic 为什么触发 Null reference
- 现象：`ai-deck/crash_code.cs`（`StatusLightLogic`）在 DIVER MCU runtime 运行即报 `Null reference`，复现记录 `ilOffset=133 / lineNo=2502`。
- 定位：
  - `mcu_runtime.c:2502` 是 `Ldelem`(0x90) 分支里的 `ASSERT_RT(arr_id != 0, "Null reference")`，即数组引用为 0（null）。
  - 对应用户代码 `RunOnMCU.WriteSnapshot(Snapshots[step])` 中的 `Snapshots[step]`：`Snapshots` 本身是 null。
- 根因：
  - 【发现】`DiverCompiler` 完全不处理静态构造函数 `.cctor`/类型初始化器：在 `DiverCompiler` 目录搜索 `cctor`/`TypeInitializer`/static 初始化 均无任何实现。
  - 【发现】`statics_descriptor` 只写静态字段 `typeid + aux`，不写初始值；引用型静态字段默认就是 0（null reference id）。
  - 因此 `private static readonly byte[][] Snapshots = new byte[][]{...}` 的初始化（C# 编译进 `.cctor`）在 MCU 上不会执行，`Snapshots` 运行时恒为 null，`Snapshots[step]` 必崩。
  - 【注意】实例字段初始化器（`.ctor`）是会被运行的——见 `MCURuntime/BUGS.md` 案例 3 的 `private byte[] buffer = new byte[8]` 能成功分配。只有 `static` 字段的 `.cctor` 不跑。
- 建议修复（未改代码，待用户确认）：
  - 方案 A：去掉 `static`，把 `Snapshots` 改成实例字段，让初始化落到 `.ctor`。
  - 方案 B：不用查表，`Operation` 内直接按 `step` 拼出 4 字节 snapshot。
- 本轮仅做根因分析与日志记录，未改用户逻辑代码、runtime、编译器。

---

## 2026-06-08 20:55 — 实现 `.cctor`（静态构造函数）支持，彻底修复静态字段初始化器

### 接下来要做
- （可选）真机 MCU 固件（MCUSerialBridge，`ENABLE_DIVER_RUNTIME=1`）重新编译并下发，使硬件端也获得 `.cctor` 能力（本轮已改的是共享的 `MCURuntime/mcu_runtime.c`，固件与 SimNode 都编它）。
- （可选）把 `ai-deck/crash_code.cs` 原样跑一遍真机/SimNode 端到端复测。

### 做了哪些事
- 不改用户写法，直接在编译器 + runtime 实现 `.cctor`，让 `private static readonly byte[][] X = new byte[][]{...}` 这种静态字段初始化器在 MCU 上真正执行。
- 编译器 `DiverCompiler/Processor.cs`：
  - 在 `isRoot` 处理完实例 `.ctor` 后，新增静态构造函数发现逻辑：先处理逻辑类自身的 `.cctor`，再对“被引用到的静态字段所属类型”做定点迭代（处理一个 `.cctor` 可能引出更多静态字段），把所有相关 `.cctor` 编进方法表。
  - 新增 `.cctor` 方法号表 chunk：`[count 2B][method_id 2B]*count`，追加到 DLL 末尾（native_chunk 之后、statics 值区之前）。
  - meta 头从 9 个 int 扩到 10 个：在 `native_chunk_size` 与 `this_clsID` 之间插入 `cctor_chunk_size`；`.diver` map 的 `headerLen` 同步从 `9*4` 改成 `10*4`。
  - 版本助记词：`ModuleWeaver` `ver` 从 `v0.36e` → `v0.37-cctor`。
- runtime `MCURuntime/mcu_runtime.c`（固件 + SimNode 共用同一文件）：
  - `vm_set_program` 多读一个 `cctor_chunk_sz`，算出 `cctor_ptr`；`statics_val_ptr` 顺延。
  - `parse_statics()` 之后、跑实例 `.ctor`(`init_method_id`) 之前，循环跑每个 `.cctor`：静态方法无 `this`，用 `vm_push_stack(cm, -1, &caller_eptr)`（注意必须传 `-1` 而非 `0`，否则局部变量初始化循环 `new_obj_id>=0?1:0` 会跳过第 0 个 var）。每个 `.cctor` 跑完调一次 `clean_up()`。
  - 更新内存布局/`meta_data` 注释。
- 验证（`DiverTest` 本机端到端：重编 `DiverCompiler` → Fody 织入 → `cl.exe` 编 `mcu_runtime.c` → 进程内跑真 runtime）：
  - 临时在 `TestLogic` 加 `static readonly byte[][] Snapshots`（含 4 条内层 `byte[]`）+ `static readonly int Magic=0xABCD`，`Operation` 读 `Snapshots[step]`，跑 10 轮；测完已还原 `TestLogic.cs`。
  - 结果：DIVER 与“纯 .NET”输出逐字节一致：step0 b1=7/b2=0、step1 56/17、step2 192/34、step3 0/51、`magic=43981`；10 轮无任何 `Null reference`/fault。
- 重新编译 SimNode 三平台 native：`win-x64`/`linux-x64`/`linux-arm64` 的 `*sim_node_runtime*` 均已更新。

### 重要发现/技能
- 【发现】runtime 早就把“静态字段”当 GC root：`clean_up()` 会遍历 `statics_val_ptr` 标记并修正引用 id（含堆对象内部互引，如锯齿数组外层指向内层）。所以 `.cctor` 里 new 出来的数组只要写进静态字段就能跨迭代存活，不需要额外改 GC。
- 【发现】runtime 早有 `init_method_id`（实例 `.ctor`）的“启动期跑一次”机制（`vm_set_program` 里用临时 root_frame + 深度 1 调用），`.cctor` 完全复用此模式即可——唯一区别是静态方法 `new_obj_id` 传 `-1`。
- 【注意】`.cctor` 必须在实例 `.ctor` / 首个 `Operation` 之前执行（.NET 语义：类型初始化器先于任何实例/静态访问）。本实现顺序：`parse_statics` → 跑所有 `.cctor` → 跑实例 `.ctor` → 进入迭代。
- 【注意】`vm_push_stack` 里局部变量初始化起始下标用的是 `new_obj_id>=0?1:0`（注意是 `>=0`），静态方法若误传 `0` 会漏初始化第 0 个局部变量；务必传负值。
- 【注意】meta 头是定长 10×int，后面 chunk 顺序固定，新增 chunk 只能放在末尾（statics 值区之前），且 `.diver` map 的 `headerLen`/`methodDetailBase` 要同步，否则故障定位行号会整体错位。
- 【发现】`DiverTest` 是最快的本机闭环：能同时重编译器、织入、编 runtime、进程内跑，强烈建议后续 runtime/编译器改动都先在这里复测。
- 【注意】`DiverTest` 跑时 native 转译（`code.c`）对实例 `.ctor` 的 `this` 字段写入会报 `C2036 void* unknown size`，这是既有问题、与本次无关：失败后整体回退到字节码解释执行（即真实 MCU 路径），功能不受影响。

---

## 2026-06-08 21:30 UTC+8 — ABI 版本号改为 SemVer X.Y.Z（4 字节打包），落实到全项目

### 接下来要做
- （可选）真机 MCU 固件（MCUSerialBridge，`ENABLE_DIVER_RUNTIME=1`）重编下发，使硬件端也带上 ABI gate（本轮改的是共享 `MCURuntime/mcu_runtime.c` + `.h`，固件与 SimNode 共用）。
- （可选）Host 下发端按 MCU 上报的 runtime ABI 预校验，拒绝下发不兼容程序，保护「尚未刷过 gate 固件」的旧机（见 `DIVER_ABI_VERSIONING.md` 末节）。

### 做了哪些事
- 按用户要求，把 ABI 版本从「单整数」改成 SemVer 风格 `X.Y.Z`，打包进 4 字节：`abi_version = 0x00_XX_YY_ZZ`（最高字节恒为 0，X/Y/Z 各 1 字节）。
  - `X` 主版本：不兼容变更（二进制 Layout 变化、执行方式大变）。主版本不同→拒绝运行。
  - `Y` 次版本：新增 BuiltIn / OpCode 等附加能力。规则 `runtime.minor >= program.minor` 才能跑。
  - `Z` 修订号：仅 Bug 修复，双向兼容。
  - 兼容判定（runtime 强制）：`magic 相同 且 major 相同 且 runtime.minor >= program.minor`。
  - 本次（magic/version 前缀 + cctor 表 + 静态构造执行）属于 Layout 变化，故定为 **2.0.0**（legacy 无 gate 概念上算 "1.x"）。
- `MCURuntime/mcu_runtime.h`（权威定义）：
  - 新增 `DIVER_ABI_MAKE(x,y,z)` 打包宏 + `DIVER_ABI_MAJOR/MINOR/PATCH(v)` 解包宏。
  - `#define DIVER_ABI_VERSION DIVER_ABI_MAKE(2,0,0)`；补全完整版本规则注释 + history。
- `MCURuntime/mcu_runtime.c` `vm_set_program` ABI gate：
  - 读 `magic`/`abi`，按 SemVer 规则判定 `magic_ok / major_ok / minor_ok`（注意用 `int` 不用 `bool`——该 .c 按 C 编译，无 `<stdbool.h>`）。
  - 不兼容时 `report_error` 输出形如 `program v2.1.0 / runtime v2.0.0 ... (runtime too old for program minor)`，并 `return -1` 拒绝加载。
- `DiverCompiler/Processor.cs`（与 runtime 保持同步）：
  - `MakeAbiVersion(x,y,z)` + `DiverAbiVersion = MakeAbiVersion(2,0,0)`；补全 SemVer 规则注释。
  - 编译期 `WriteWarning` 打印 `DIVER ABI vX.Y.Z, magic=0x...`，便于核对织入产物版本。
  - 版本助记词 `ModuleWeaver.ver`：`v0.38-abi` → `v0.39-semver`。
- 新增项目级文档 `DIVER_ABI_VERSIONING.md`（仓库根）：完整描述 header/ABI gate 布局、X.Y.Z 含义、兼容规则、版本权威位置（runtime 头文件）与 history，并提示旧固件需靠 Host 端预校验保护。

### 验证（DiverTest 本机闭环，全部通过）
- 正例：program 2.0.0 vs runtime 2.0.0 → 正常跑满 10 轮，无 fault。
- 反例：program 0.0.2(旧整数) vs runtime 2.0.0 → 拒绝，提示 `major mismatch`。
- 反例：program 2.1.0 vs runtime 2.0.0 → 拒绝，提示 `runtime too old for program minor`。
- 正例：program 2.0.5 vs runtime 2.0.0 → 正常跑（patch 差异双向兼容）。

### 重要发现/技能
- 【注意】`mcu_runtime.c` 按 **C**（非 C++）编译，没有自动 `bool`。新增逻辑一律用 `int` 表示布尔，否则 `cl.exe` 报 `C2065 "bool" 未声明`。
- 【注意】改了 `DiverCompiler/Processor.cs` 后，`dotnet run` 默认增量编译会跳过 `DiverTest` 的 C# 重编译，于是 **不会重新织入**，跑的还是上次织入的旧程序（旧 ABI）。必须 `dotnet build -c Debug --no-incremental` 强制重织，再 `dotnet run --no-build` 才能看到新 ABI。这是这次「runtime 已是 2.0.0 但 program 仍报 0.0.2」误判的根因。
- 【技能】net48 老式 `DiverCompiler.csproj` 用 VS 自带 `MSBuild.exe`（`...\2022\Community\MSBuild\Current\Bin\MSBuild.exe`）`-t:Rebuild` 单独重编最稳；PostBuildEvent 会 `xcopy` 到 `..\DiverTest\`。`dotnet` SDK 路径也能编，但必须配合 `--no-incremental` 触发 DiverTest 重织。
- 【发现】SemVer 兼容是**非对称**的：major 必须严格相等；minor 只要 `runtime >= program`（runtime 是超集即可）；patch 完全不参与判定。这条规则同时写进了 runtime、compiler、`DIVER_ABI_VERSIONING.md` 三处，改其一务必三处同步。

---

## 2026-06-08 21:55 UTC+8 — 新增 CommandGetAbi(0x08)：Probe 时上报 DIVER 运行时 ABI

### 接下来要做
- （可选）真机：刷入本轮重编的 V2.1 固件后，用 `node probe` 验证 `abi.semVer=2.0.0`；旧固件应回 `hasDiverRuntime=false`（Proto_UnknownCommand）。
- （建议）Host 下发程序前用 probe 拿到的 `abi` 做兼容预校验（major 不等/ minor 过新直接拒绝下发），保护旧机；本轮只打通了「上报」，尚未做「下发前拦截」。

### 背景
- 上一轮加了 DIVER 程序二进制 ABI（SemVer X.Y.Z）+ runtime 启动期 ABI gate。但 ProbeMCU 只回报 Git Tag/Commit/BuildTime（`VersionInfoC`），**没有 ABI 回报**，Host 无从在下发前判断兼容性。本轮新增一条串口命令把 ABI 报上来。

### 做了哪些事（端到端贯穿所有相关层）
- 协议 `MCUSerialBridge/c_core/include/msb_protocol.h`：
  - 新增 `CommandGetAbi = 0x08`（响应 0x88）。
  - 新增 `AbiInfoC { u32 magic; u32 abi_version; }` + `STATIC_ASSERT(==8)`；含完整 SemVer 注释。
- 固件 `MCUSerialBridge/mcu/appl/source/packet.c`：
  - `#include "mcu_runtime.h"`（`HAS_DIVER_RUNTIME` 守卫）。
  - `case CommandGetAbi`：填 `DIVER_PROGRAM_MAGIC` / `DIVER_ABI_VERSION`（无 DIVER 运行时则填 0）回传。
- C core `msb_bridge.c/.h`：新增 `msb_get_abi()`，并加入函数指针表 `MCUSerialBridgeAPI`（`mcu_serial_bridge_get_api` 填充）。
- C# wrapper `MCUSerialBridgeCLR.cs`：新增 `AbiInfo` 结构（含 `HasDiverRuntime/Major/Minor/Patch/SemVer` 辅助）+ `msb_get_abi` DllImport + `GetAbi()` 方法。
- SDK：
  - `IRuntimeNode` 加 `AbiInfo? Abi`。
  - `MCUNode.Connect()` 在 GetLayout 后 `GetAbi`（旧固件 Proto_UnknownCommand → 留空）。
  - `SimulatedMcuNode` 加 `Abi` 属性 + `CreateAbiInfo()`（硬编码 2.0.0，与 runtime 同步）。
  - `DIVERSession`：`NodeProbeResult` 加 `Abi`；`ProbeNode`（真机+模拟）填充；`NodeEntry` 存 `Abi`；`AddNode`/add-simulated/`StartNode` 贯穿；`NodeFullInfo` 加 `AbiInfoSnapshot`（`BuildAbiSnapshot`）。
- Host `ApiRoutes.cs`：`/api/node/probe`、`/api/node/add`、`/api/node/add-simulated` 响应加 `abi`。
- 前端 `types/index.ts`：新增 `AbiInfo`，并加到 `NodeProbeResult/NodeFullInfo/AddNodeResult`；`AddNodeDialog.vue` 探测结果显示 `DIVER ABI: vX.Y.Z`（旧固件显示 N/A）。CLI `coral_agent.py` 原样 print_json，无需改。
- 教程 `3rd/CoralinkerKitDocs`：`07-node-management.md` 新增「DIVER ABI 版本」小节 + 探测说明；`10-agent-api.md` 补 probe 响应 `abi` 字段说明。
- 版本：DIVER ABI 仍 2.0.0（本轮只加通信命令，未改程序二进制布局，故不动 ABI 号；但属于「新增命令」，是串口协议的加法）。

### 验证（三套工具链全绿）
- C# Host（含 SDK + wrapper）：`dotnet build CoralinkerHost.csproj -c Release --no-restore` 成功，仅既有 warning。
- 原生 PC 端 DLL：`scons -j12 build/mcu_serial_bridge.dll` 成功（`msb_bridge.c` 编过、导出 `msb_get_abi`）。
- ARM 固件：`scons PDN=CORAL-NODE-V2.1 BUILD_MCU=1 ENABLE_DIVER_RUNTIME=1` 成功，`packet.o` 带 `-DHAS_DIVER_RUNTIME=1 -I...\MCURuntime` 编过，产出 `firmware.elf/hex/bin`。

### 重要发现/技能
- 【发现】PC 端 native bridge DLL 与 ARM 固件**都**用 `c_core`，但固件特有的 `packet.c` 只在固件侧编；`mcu_runtime.h` 只在 `ENABLE_DIVER_RUNTIME` 时进 CPPPATH（SConscript 第 96~103 行）+ 定义 `HAS_DIVER_RUNTIME=1`，所以 packet.c 的 ABI 上报必须用该宏守卫，否则非 DIVER 固件编不过。
- 【注意】旧固件不认识 `0x08`，固件 `default` 分支回 `MSB_Error_Proto_UnknownCommand`；上位机（`MCUNode`/`ProbeNode`）必须把它当「legacy 固件、无 ABI」而**不是**探测失败，否则会误判旧机不可用。
- 【技能】本机快速验证 C 改动：`scons -j12 build/mcu_serial_bridge.dll`（PC DLL，用 MSVC `cl`，秒级）+ 完整固件 `scons PDN=... BUILD_MCU=1 ENABLE_DIVER_RUNTIME=1`（ARM GCC，几秒）。两者都不需要硬件即可验证编译。
- 【注意】`AbiInfoC` 是新加的线协议结构，长度固定 8B；`STATIC_ASSERT` 守住，C# `AbiInfo` 用 `[StructLayout(Sequential)]` 两个 `uint` 对齐，Marshal 直接 out。
- 【发现】函数指针表 `MCUSerialBridgeAPI` 里我把 `msb_get_abi` 插在 `mcu_get_layout` 与 `msb_configure` 之间——因为所有消费者都随头文件重编（C# 走直接 DllImport 不用表），中插安全；但若将来有跨版本二进制消费该表，应改为只在末尾追加。

---

## 2026-06-08 21:53 UTC+8 — 【注意】DEV_LOG 被并发 Agent 截断，已用 git 恢复

- 现象：另一个 Agent 改写了 `ai-deck/DEV_LOG.md`，把 `20:24 SimNode fatal` 之后的内容整段删掉（`git diff` 显示相对 HEAD 仅 -19 行，即丢了已提交的 `20:29` 根因条目；我本轮未提交的 `20:55`/`21:30`/`21:55` 三条也一并消失）。
- 恢复：`git checkout HEAD -- ai-deck/DEV_LOG.md` 拿回已提交基线（含 `20:29` 条目），再把 `20:55`/`21:30`/`21:55` 三条按对话记录原样补回。
- 【技能】DEV_LOG「只增不减」，被并发改写时先 `git diff --stat -- <file>` 看增删行数，再 `git show HEAD:<file>` / `git checkout HEAD -- <file>` 恢复已提交部分，未提交部分从会话/转录补回，不要凭空重写。

---

## 2026-06-08 22:00 UTC+8 — 教程去掉对仓库根 `DIVER_ABI_VERSIONING.md` 的外链

- 用户指出：`3rd/CoralinkerKitDocs` 打包分发后**无法访问外层源码**，所以教程里「详见仓库根 `DIVER_ABI_VERSIONING.md`」这种外链是死链。
- 改了两处（KitDocs 必须自包含）：
  - `07-node-management.md`「DIVER ABI 版本」：删掉对 `DIVER_ABI_VERSIONING.md` 的引用，改为内联说明 `abiVersion = 0x00_XX_YY_ZZ` 的 SemVer 打包格式，并补上运行时兼容判定规则（`magic 相同 且 主版本相同 且 运行时次版本 >= 程序次版本`），信息不丢。
  - `10-agent-api.md`：把「详见 `07-node-management.md` 与仓库根 `DIVER_ABI_VERSIONING.md`」改为只指向同包内 `[07-node-management.md](07-node-management.md)`。
- 校验：`rg DIVER_ABI_VERSIONING 3rd/CoralinkerKitDocs` 已无命中（剩余「仓库根」匹配是无关的临时文件存放规则）。
- 【注意】`3rd/CoralinkerKitDocs` 是随 Kit 单独打包的文档集，里面只能互链同目录 `.md`，**严禁**引用仓库根 / 外层源码路径（如 `DIVER_ABI_VERSIONING.md`、`MCURuntime/*`）。需要的内容要内联进文档。

---

## 2026-06-08 22:30 UTC+8 — 【注意】cctor + ABI 核心改动被并发 Agent 回退，已重新实现并验证

### 背景 / 现象
- 用户用新发布包（`Publish/CoralinkerHost_66025b5_20260608-220511`）跑 crash_code 仍报错，且生成的 bin 头**没有 DIVR 和 ABI**。
- 排查：`66025b5` 就是 git HEAD，我之前的 ABI/cctor 改动**从未提交**；更糟的是那个搞坏 DEV_LOG 的并发改动把两个**核心文件回退到了 HEAD**：
  - `DiverCompiler/Processor.cs`（写 DIVR+ABI、生成 cctor 表）—— 零痕迹。
  - `MCURuntime/mcu_runtime.c`（校验头、执行 .cctor）—— 零痕迹。
  - 而 `mcu_runtime.h` / `ModuleWeaver.cs` / `CommandGetAbi` 那套外围改动还在 → 工作区半残（头文件声明了 ABI，但编译器不写、运行时不读）。
- `git stash` / `reflog` / 备份均无法恢复（是未提交的工作区改动）。只能重做这两个文件。

### 做了哪些事（重新实现，已端到端验证通过）
- `MCURuntime/mcu_runtime.c` `vm_set_program`：
  - 文件最前面加 ABI gate：先读 `magic`(4B)+`abi`(4B)，按 SemVer 判 `magic_ok/major_ok/minor_ok`（用 `int` 不用 `bool`，该 .c 按 C 编），不兼容则 `report_error`+`return -1`。
  - meta 头多读 1 个 int `cctor_chunk_sz`（插在 `native_chunk_sz` 与 `this_clsid` 之间，共 10 个 int）。
  - `statics_val_ptr = cctor_ptr + cctor_chunk_sz`（cctor_ptr 在 native_chunk 之后）。
  - `parse_statics()` 之后、跑实例 `.ctor` 之前，循环跑每个 `.cctor`：复用 root_frame + `vm_push_stack(mid, -1, ...)`（静态方法传 `-1` 让局部变量从 0 初始化，因 var-init 循环用 `new_obj_id>=0?1:0`），每个跑完 `clean_up()`。
  - 更新内存布局注释。
- `DiverCompiler/Processor.cs`：
  - 加 ABI 常量 `DiverProgramMagic=0x52564944` / `MakeAbiVersion` / `DiverAbiVersion=2.0.0`。
  - `isRoot` 实例 `.ctor` 循环之后、`re_link:` 之前，加 `.cctor` 发现：先处理逻辑类自身 `.cctor`，再对 `referenced_typefield` 中**静态字段所属类型**做定点迭代（处理一个 `.cctor` 会引出更多静态字段），全部 `Process` 进方法表，方法号收集进 `cctorMethodKeys`。
  - dll 字节流头部加 `magic`(4B)+`abi`(4B)；meta 头加 `cctor_chunk.Length`；末尾追加 cctor 表 `[count 2B][registry 2B]*count`。
  - `.diver` map 的 `headerLen` 从 `9*4` 改成 `8 + 10*4`（8B 前缀 + 10 个 int），保证 `methodDetailBase` 与运行时 `ptr-mem0` 报错偏移对齐。
- 【发现】发布用的运行时编译器是 `DiverCompilerPortable`（`AssemblyName=DiverCompiler`），它 `<Compile Include>` link 了 `..\DiverCompiler\Processor.cs` 等，所以改 `DiverCompiler/Processor.cs` 一处即可，发布会带上。
- 【发现】`mcu_runtime.c` 在非 MCU(`#ifndef IS_MCU`) 直接 `#include "mcu_runtime.h"`；MCU 侧 `appl/vm.h` 第 4 行也 `#include "mcu_runtime.h"`，所以 ABI gate 宏在 PC/SimNode/固件三套构建都可用，无需额外加 include。

### 验证（DiverTest 本机闭环，重新织入 + 编 mcu_runtime.c + 进程内跑真 runtime）
- 临时在 `TestLogic` 加 `static readonly byte[][] Snapshots`（4 条内层）+ `static readonly int Magic=0xABCD`，`Operation` 读 `Snapshots[step]` 写进 3 个 LowerIO；测完已 `git checkout` 还原 `TestLogic.cs` 与 `DIVERInterface.cs`。
- 编译期日志：`cctor table: 1 method(s) [8]`、`DIVER ABI v2.0.0, magic=0x52564944`。
- 运行 10 轮无任何 `Null reference`/fault；`staticB1/B2` 逐轮稳定循环 7/0,56/17,192/34,0/51（192 经 byte→int 显示为 -64，是既有有符号字节展示问题，与 cctor 无关），`staticMagic=43981`。说明 .cctor 在加载期分配了锯齿数组、值正确、并跨迭代经 clean_up 存活。
- 直接 dump 织入产物 `TestLogic.bin` 前 12B：`44 49 56 52 | 00 00 02 00 | 64 00 00 00` = 'DIVR' + ABI 2.0.0 + interval 100。**bin 头现在确有 DIVR + ABI。**
- `DiverCompilerPortable`（发布用）`dotnet build -c Release` 成功（仅既有 CS0162 警告）。

### 接下来要做 / 给用户
- 用户需**重新打包发布**（重跑发布脚本），让 `res/compiler/DiverCompiler.dll` 用上新的 Processor，下发的 bin 才会带 DIVR+ABI 且支持 cctor。
- 【强烈建议】尽快 `git commit` 这批改动（Processor.cs / mcu_runtime.c / .h / 协议 / SDK / 文档），避免再次被并发 Agent 回退后无法恢复。
- 【注意】ABI 升到 2.0.0 是**不兼容**大版本：新 bin 不能跑在「未刷过 gate 固件」的旧 MCU 上（旧固件无 gate，会误解析新头）。刷过一次 gate-aware 固件后即自保护；Host 端下发前预校验（用 probe 的 `abi`）仍是 TODO。

### 重要发现/技能
- 【技能】核心改动务必及时 commit。本次教训：未提交的工作区改动被并发 Agent 回退后，stash/reflog 都救不回，只能靠 DEV_LOG + 源码理解重做。DEV_LOG 把设计写细（chunk 布局、偏移、`vm_push_stack` 的 `-1` 语义）这次救了命，得以 1:1 重建。
- 【发现】重建时确认「半残状态」的判定法：`git status` 看哪些文件**没有**出现在 modified 列表里——`Processor.cs`/`mcu_runtime.c` 不在列表 = 已被回退到 HEAD；而 `.h`/外围文件在列表 = 改动还在。两者不一致即为被部分回退。
- 【注意】runtime 报错偏移是 `cur_il_offset = ptr - mem0`（含 8B 前缀 + 10 int 头），所以 compiler 的 `.diver` map `headerLen` 必须同步成 `8 + 10*4`，否则故障定位行号整体错位。

---

## 2026-06-08 23:30 UTC+8 — 前端显示 ABI：重建被回退的 ABI 上报链（wrapper→SDK→Host→前端）+ About 显示编译器 ABI

### 背景 / 现象
- 用户反馈：Probe 模拟节点只显示 Product/Commit/BuildTime/Ports，**没有 ABI 版本**；网页 About 也只有 Front/Backend，**没有编译器版本**。
- 排查（`git status` 比对法）：上一轮 `CommandGetAbi` 那批改动里，**只有 C 协议侧幸存**（`msb_protocol.h`/`msb_bridge.h`/`packet.c` 在 modified 列表里且含 abi）。被并发 Agent 回退到 HEAD 的有：
  - `MCUSerialBridgeCLR.cs`（`AbiInfo`/`GetAbi`/DllImport）—— 注意它仍显示 "M"，但其实只是 CRLF 行尾差异，`numstat` 为空、abi 内容已没了。
  - `msb_bridge.c`（`msb_get_abi` 实现 + API 表注册）—— 不在 modified 列表 = 被回退；而 `msb_bridge.h` 的声明+表字段还在 → 又一处「半残」。
  - 全部 SDK 消费方（`IRuntimeNode`/`MCUNode`/`SimulatedMcuNode`/`DIVERSession`）、Host `ApiRoutes.cs`、前端 `types/index.ts`/`AddNodeDialog.vue` —— 全被回退。
- 结论：上报链从 wrapper 往上整段没了，所以前端拿不到 abi。

### 做了哪些事（端到端重建 + About 新增）
- wrapper `MCUSerialBridge/wrapper/MCUSerialBridgeCLR.cs`：重加 `AbiInfo` 结构（`Magic/AbiVersion`+`HasDiverRuntime/Major/Minor/Patch/SemVer`，并加常量 `DiverMagic=0x52564944`、`CurrentAbiVersion=0x00020000`）、`msb_get_abi` DllImport、`GetAbi()` 方法。
- C core `msb_bridge.c`：重加 `msb_get_abi()` 实现（`mcu_send_packet_and_wait(CommandGetAbi, ... sizeof(AbiInfoC))`）+ `api->msb_get_abi = msb_get_abi;` 注册（`.h` 声明/表字段本就幸存）。
- SDK：
  - `IRuntimeNode` 加 `AbiInfo? Abi`。
  - `MCUNode`：加 `Abi` 属性，`Connect()` 在 GetLayout 后 `GetAbi`（旧固件 Proto_UnknownCommand → 留空），`Disconnect` 清空。
  - `SimulatedMcuNode`：加 `Abi` + `CreateAbiInfo()`（用 `AbiInfo.DiverMagic`/`CurrentAbiVersion`）。
  - `DIVERSession`：`NodeProbeResult` 加 `AbiInfo? Abi`；新增 `AbiInfoSnapshot` record；`NodeFullInfo` 加 `Abi`（默认 null）；`NodeEntry` 加 `Abi`；`ProbeNode`（真机 `bridge.GetAbi` + 模拟 `CreateAbiInfo`）/`AddNode`/`AddSimulatedNode`/`StartNode`/`BuildNodeFullInfo`(+`BuildAbiSnapshot`) 全程贯穿。
- Host `ApiRoutes.cs`：`/api/node/probe` 响应加 `abi`（从 `result.Abi`）；`/api/node/add`、`/api/node/add-simulated` 加 `abi = info?.Abi`。
- Host About `HostAboutService.cs`：`HostAboutSnapshot` 加 `DiverAbiInfo DiverAbi`（取 `AbiInfo.CurrentAbiVersion`，渲染 SemVer + magic），`/api/about` 自动带出。
- 前端：`types/index.ts` 加 `AbiInfo`/`DiverAbiInfo`，并入 `NodeProbeResult`/`NodeFullInfo`/`AddNodeResult`/`HostAboutSnapshot`；`AddNodeDialog.vue` 探测结果加「DIVER ABI: vX.Y.Z」（旧固件显示 N/A）；`HomeView.vue` About 弹窗新增「DIVER Compiler」段（Program ABI + Magic），加 `formatAbiMagic`。

### 验证
- C# `dotnet build CoralinkerHost.csproj -c Release` → 0 error（仅既有 DiverTest 警告）。
- 前端 `npm run build`（`vue-tsc -b && vite build`）→ 通过，产物已出到 `wwwroot/`（含 About/Probe 改动）。
- 原生 `scons -j12 build/mcu_serial_bridge.dll` → 成功；DLL 导出表确含 `msb_get_abi`（二进制 grep 命中）。

### 重要发现 / 注意
- 【技能】判定「被回退/半残」最快的办法：`git status` 看文件在不在 modified 列表 + `git diff --numstat`（空=仅行尾差异，实际内容已回 HEAD）+ `git diff | grep abi` 看关键内容是否还在。三招组合定位出 wrapper/msb_bridge.c 被回退而其 .h 幸存的半残态。
- 【注意】原生 DLL 有两份：`MCUSerialBridge/build/mcu_serial_bridge.dll`（scons 直接产出）与 `build/runtimes/win-x64/native/mcu_serial_bridge.dll`（**CoralinkerSDK.csproj 实际引用并 copy 的源**）。scons 只更新前者，后者需手动同步——否则重编/发布会把**旧** DLL（无 `msb_get_abi` 导出）拷进去，真机 `GetAbi` 会 EntryPointNotFound。本轮已手动 `Copy-Item` 同步。
- 【发现】模拟节点 SimNode **不经过** native DLL/wrapper，`Abi` 由 `SimulatedMcuNode.CreateAbiInfo()` 直接硬编码上报；所以用户当前用模拟节点就能看到 ABI，无需原生 DLL。真机路径才依赖 wrapper+native。
- 【注意】`AbiInfo.CurrentAbiVersion`（wrapper）现在是 Host/About 显示编译器 ABI 的来源，须与 `MCURuntime/mcu_runtime.h` 的 `DIVER_ABI_VERSION` 及 `DiverCompiler` 的 `DiverAbiVersion` 三处同步（共 4 处：runtime 头、compiler、wrapper、文档）。

### 接下来要做 / 给用户
- 重新打包发布后：模拟节点 Probe 应显示「DIVER ABI: v2.0.0」；About 弹窗应出现「DIVER Compiler → Program ABI v2.0.0 / Magic 0x52564944」。
- 【强烈建议】尽快 `git commit` 这批 + 上一轮 cctor/ABI 改动，避免再次被并发 Agent 回退后无法恢复。
- （仍 TODO）Host 下发前用 probe 的 `abi` 做兼容预校验拦截。

---

## 2026-06-09 00:10 UTC+8 — 三连修：拒绝旧固件添加 / Logic 单占用 UX / 重复添加明确提示

### 接下来要做
- （可选）真机验证：旧固件节点应在「添加」时被拒并提示升级；已添加节点重复添加应提示「已添加」。
- （可选）把 Host 下发前 ABI 预校验也接到 Program 路径（目前在 AddNode 处拦截；Program 端旧逻辑仍依赖 runtime gate）。

### 做了哪些事
1) **阻止 Legacy / 不兼容固件被添加（用户任务 1）**——后端在 `AddNode` 加 ABI 闸门：
   - `DIVERSession`：新增 `AddNodeStatus`{Ok/ProbeFailed/AlreadyExists/IncompatibleFirmware} + `AddNodeOutcome(Status,Uuid,Message)`；`AddNode` 返回类型从 `string?` 改为 `AddNodeOutcome`。
   - 新增 `CheckFirmwareAbi(AbiInfo?)`：`abi==null||!HasDiverRuntime` → 旧固件拒绝；`major!=host || minor<host` → 不兼容拒绝（规则与 `mcu_runtime.c` 一致：magic 同、major 同、runtime.minor>=program.minor）。host 版本取 `AbiInfo.CurrentAbiVersion`(2.0.0)。
   - 消息：「固件未上报 DIVER 运行时 ABI（旧固件）…请先升级固件后再添加」/「固件 DIVER ABI vX.Y.Z 与上位机编译器 v2.0.0 不兼容（固件过旧）…」。
   - 模拟节点走 `AddSimulatedNode`，不过闸门，照常可用。
2) **重复添加明确提示（用户任务 3）**——`AddNode` 的「相同 mcuUri 已存在」分支返回 `AlreadyExists` + 「该节点已添加…无需重复添加」；`RuntimeSessionService.AddNodeAsync` 改返回 `AddNodeOutcome`；`ApiRoutes /api/node/add` 失败时回 `{ok:false, error:outcome.Message, reason:Status}`（前端 AddNodeDialog 本就显示 `addResult.error`，于是不再是笼统「Probe Failed」）。
3) **Logic 单占用前端 UX（用户任务 2）**——后端早已在 `/program` 拦截重复占用（返回 409 文案），问题在前端无提示、可错选：
   - `CoralNodeView.vue`：用 `useVueFlow().getNodes` 计算 `usedLogicsByOthers`（排除自己、type==coral-node、取 data.logicName），`logicOptions` 对「被他人占用」的项 `disabled:true` 且 label 标注「(已被 X 占用)」。跨节点响应式：他节点 program/unprogram 改 data.logicName → getNodes 响应式 → 选项实时禁用/释放。
   - n-select 加 `clearable`，`updateLogicName(newLogic|null)`：选空→调用新 `unprogramNode` 释放 Logic 回空态；program 失败（冲突/产物缺失，axios 拦截器把后端 error 文案转成 Error.message）→ `message.error` 提示 + 回退选择（localLogicName/updateNodeData 还原 prevLogic）。
   - 节点删除即释放：后端 `RemoveNode` 删 entry → `/program` 用 `ExportNodes()` 实时判定，自动释放；前端 used 集合来自 vue-flow nodes，节点移除后自动重算。
   - 允许空逻辑：新增 `DIVERSession.UnprogramNode(uuid)`（清 ProgramBytes/MetaJson/LogicName/BuildInfo/CartFields + PruneUndeclaredVariables）+ `RuntimeSessionService.UnprogramNodeAsync` + `POST /api/node/{uuid}/unprogram` + 前端 `unprogramNode` API。
   - 同步更新 CLI `3rd/CoralinkerSDK/Program.cs` 的 `AddNode` 调用方（用 `addOutcome.Uuid/.Success/.Message`）。

### 验证
- `dotnet build CoralinkerHost.csproj -c Release` → 0 error（仅既有可空性警告）。
- 前端 `npm run build`（vue-tsc + vite）→ 通过，产物出到 `wwwroot/`。

### 重要发现 / 注意
- 【发现】后端 `/program` **早就**有「一个 Logic 只能编程到一个节点（否则 LowerIO 字段冲突）」的硬校验（ApiRoutes ~586 行返回 409）。本次任务 2 主要是补前端 UX，让占用可见、可释放、可回空，而非新增后端约束。
- 【发现】`axios` 响应拦截器（`api/index.ts`）会把后端 4xx 的 `data.error` 提取成 `Error.message` 并 reject——所以 `programNode` 冲突时前端走 `catch`，`String(error)` 即后端文案，可直接 `message.error` 展示。
- 【注意】`AddNode` 改了返回类型（`string?`→`AddNodeOutcome`），共 3 个调用方：`RuntimeSessionService`、CLI `Program.cs`、（ApiRoutes 经 service）。改签名时务必全改，否则 CLI 编不过。
- 【注意】ABI 闸门只在真机 `AddNode` 生效；模拟节点与「导入已保存工程」路径不过闸门（导入的是历史节点，不重新 probe）。如需对导入节点也校验，需在加载时补 probe+校验（暂未做）。

---

## 2026-06-09 00:35 UTC+8 — 所有网页可见提示改英文

### 做了哪些事
- 按用户要求把所有「网页可见」中文提示改成英文（仅可见 UI 文本/提示，不动代码注释/对象 key/console.log）。
- 后端（经 API 透传到网页的提示）`DIVERSession.cs`：AddNode 的 AlreadyExists/ProbeFailed 文案、CheckFirmwareAbi 旧固件/不兼容文案全部英文化。
- 前端：
  - `CoralNodeView.vue`：Logic 选项「(in use by X)」、清除逻辑失败 message。
  - `HomeView.vue`：远端新版本提示横幅、Agent 弹窗三个小标题+段落、覆盖保存 confirm 文案。
  - `ControlWindow.vue`：添加控件菜单分组标题与各控件名（Joystick/Slider/Switch/Gauge/Lamp 及显示模式/方向 option）。
  - `PortView.vue`：WireTap TX/RX 监听 title 提示。
  - `protocol/canopen/dictionary.ts` + `cia402.ts`：对象字典 description（设备类型/错误寄存器/心跳/控制字状态字/位置速度扭矩目标值/回原点/数字 IO 等）全部英文化（CANopen 标准术语）。
- 验证：`dotnet build CoralinkerHost.csproj -c Release` 0 error；前端 `npm run build`（vue-tsc+vite）通过，产物已出 `wwwroot/`。

### 注意
- 【发现】定位「网页可见」中文用 regex：属性值 `="[^"]*[\u4e00-\u9fff]`、模板文本 `>[^<>]*[\u4e00-\u9fff]`、字符串字面量 `'…中文…'`/`` `…中文…` ``；排除掉只在 `//`、`/* */`、`<!-- -->` 注释里的中文（不显示，未改）。AddNodeDialog 等文件的中文其实全是注释，UI 早已英文。
- 【注意】重新打包发布后这些英文提示才会进 `wwwroot`；本轮已重建前端产物。

---

## 2026-06-09 01:30 UTC+8 — 回退 CCM 优化（保留 telemetry）+ 备好前后端给用户自测 + 基准测试程序

### 背景 / 用户意图
- 用户叫停 CCM 优化路线：模拟节点跑在 PC 上、无 CCM 概念，`lastCycles`=0，**不能用于性能测试**。
- 用户要：① 一个能跑在物理节点上的测试程序；② 把上一轮的 CCM 改动**回退**（telemetry 保留）；③ 用户自己编译 upg(2.1) 并自测。
- 后续又追加：**upg 不用我编**，只要把**前后端弄好**告诉他即可，他自己跑测试。

### 做了哪些事
1) **回退 CCM 改动（保留 telemetry）**
   - `MCUSerialBridge/mcu/appl/include/appl/control.h`：`PROGRAM_BUFFER_MAX_SIZE` 从 `48*1024` 退回 `20*1024`，注释退回原版。
   - `MCUSerialBridge/mcu/appl/source/control.c`：`program_buffer_storage` 去掉 `CCM_RAM`，退回普通 SRAM 静态数组。
   - `MCURuntime/mcu_runtime.c`：直接 `git checkout HEAD --` 整文件回退（清掉 `MCU_FASTMEM` 宏修复 + 所有 `MCU_FASTMEM` 标注），然后**只重新插回** telemetry 两个函数 `vm_get_heap_used()` / `vm_get_heap_obj_count()`（放在 `vm_get_lower_memory_size()` 之后）。`mcu_runtime.h` 的两个声明本就属 telemetry，保留。
   - 结论：固件源码现在 = **baseline（无 CCM）+ telemetry**。用户从当前源码编 upg 即得 v2.1 基线固件。

2) **基准测试程序**（给物理节点跑）：`ai-deck/test_programs/BenchLogic.cs`（+ `desc.md`）
   - 故意狂打 CCM 要加速的热点：`int[256]` 数组元素读写（走 heap_obj 表）、`Mix()` 方法调用（栈帧）、static 字段 `_buf/_state`。
   - 每 cycle 工作量固定（由 UpperIO `rounds` 调，默认 40 → 40×256 内循环），所以 **`lastCycles` 稳定可比**；`checksum`(LowerIO) 对同一 iteration 在两套固件上**必须一致** → 证明 CCM 没改变行为。
   - 用法：网页上传为 asset → Save → Build → Program 到节点 → Start → 看 CPU Load 图 / telemetry 的 `lastCycles`；之后刷 CCM 版固件同 `rounds` 再跑对比。

3) **备好前后端（用户自测用）**
   - c_core 原生 DLL：`scons -j8`（MCUSerialBridge）→ `build/mcu_serial_bridge.dll` 已是最新（含 0x72 `CommandUploadLowerIoAndVmStats` 分发 + `msb_register_vm_stats_callback` 导出，二进制 grep 命中）。**手动同步**到 `build/runtimes/win-x64/native/mcu_serial_bridge.dll`（CoralinkerSDK.csproj 实际引用此路径）。
   - 后端：`dotnet build CoralinkerHost.csproj -c Release` → 0 error（仅既有可空性警告）。Host 输出目录的 `runtimes/win-x64/native/mcu_serial_bridge.dll` 为最新（01:05）。
   - 前端：`npm run build`（vue-tsc+vite）→ 14s 通过，产物出到 `wwwroot/`；bundle 里能 grep 到 `vmstats`（CpuLoadChart telemetry 代码已打进去）。

### 接下来要做 / 给用户
- 用户自行编译 baseline upg（建议从 `MCUSerialBridge` 跑）：
  `scons BUILD_MCU=1 PDN=CORAL-NODE-V2.1 ENABLE_DIVER_RUNTIME=1 upg`
  产物在 `MCUSerialBridge/build/*.upg`。
- 【注意】upg 的 Tag 字段来自 `git describe --tags --abbrev=0`；当前**仓库无任何 tag**，所以上次 upg 文件名里 Tag 为空（`..._66025b5__时间.upg`）。要让固件版本显示 **2.1**，编 upg 前先 `git tag 2.1`（或带助记词 `2.1-tlm`，≤8 字节）再 `scons ... upg`。
- 流程：刷 v2.1(baseline) → Program `BenchLogic` → 记录 `lastCycles`；再刷 CCM 版(v2.2) → 同 `rounds` 跑 → 对比周期数；`checksum` 两版同 iteration 应一致。

### 重要发现 / 注意
- 【技能】只回退「某一类改动」而保留同文件里的另一类改动：对**混了两类改动的文件**（mcu_runtime.c 同时有 CCM 标注 + telemetry 函数），最干净的做法是 `git checkout HEAD -- <file>` 整体回到基线，再用 StrReplace 把要保留的那部分**重新插回**，比一处处手撕 `MCU_FASTMEM` 更不易残留。
- 【发现】固件版本助记词落点：upg 头里有 Tag(8B)/Commit(8B)/BuildTime(24B)/PDN(16B)。Commit=`git rev-parse --short HEAD`（**不含未提交改动**，所以靠 commit 区分调试版本不可靠），Tag=最近 git tag。要可靠区分「在跑哪一版」，用 **git tag 承载 version+助记词**（如 `2.1-tlm`/`2.2-ccm`），probe 时即可见。
- 【注意】CoralinkerSDK.csproj 引用的原生 DLL 路径是 `MCUSerialBridge/build/runtimes/win-x64/native/`，而 scons c_core 只产出到 `MCUSerialBridge/build/`。每次重编 c_core 必须手动 `Copy-Item` 同步，否则后端会带**旧** DLL（无 0x72/vm_stats 导出），真机 telemetry 收不到。
- 【注意】模拟节点不经过原生 DLL，其 `vmstats` 由 SimNodeHost 直接发 JSON，`lastCycles=0`（只有 PC wall-clock μs）。**性能对比必须用物理节点**——这正是本轮叫停模拟节点路线的原因。

---

## 2026-06-09 01:58 UTC+8 — Memory Load 遥测（high-water mark）+ CPU Load 改用 DWT（micros 兜底）+ 双 gauge UI

### 背景 / 用户意图
- 用户看到 telemetry 卡片只有 CPU，且 `micros` 精度只有 1ms（亚毫秒反应不出来）；想同时看 CPU Load 和 Memory Load。
- 先确认了 VM 内存模型：每 cycle 结束 **栈完全回退≈0**，**堆是 mark-sweep（按 statics 为根保活，不是清零）**——所以 cycle 末的 heap_used 只是「静止 statics 足迹」（如 BenchLogic 的 `static int[256]`=1KiB），看不到 cycle 内的瞬时峰值。结论：内存值得统计，但要统计**cycle 内峰值 high-water mark**（峰值栈 + 峰值堆 vs 缓冲区总量），这才是会撑爆 20KiB 单缓冲区的真正风险量。
- 用户指示：Do it；上传 micros+interval+cpuHz，若 DWT 结果与 micros 显著不一致就退回 micros；Memory 遥测**不能太影响性能和代码一致性**。

### 做了哪些事（端到端）
1) **runtime 高水位追踪（`MCURuntime/mcu_runtime.c`，零侵入热路径）**
   - 新增全局 `uchar* mem_stack_hi`（本 cycle 栈到过的最高地址）、`uchar* mem_heap_lo`（堆到过的最低地址）。
   - `vm_run()` 入口（push entry frame 前）重置：`mem_stack_hi=stack0`、`mem_heap_lo = heap_newobj_id>1 ? heap_obj[last].pointer : heap_tail`（基线含静止 statics 足迹）。
   - **栈**：只在 `vm_push_stack` 算出 `evaluation_st_ptr`/`max_stack` 后，更新一次 `frame_top = evaluation_st_ptr + max_stack*STACK_STRIDE`（每次方法调用 1 次比较，**不进 opcode 循环**）。
   - **堆**：只在 3 个分配器 `newobj/newstr/newarr` 算出 `my_ptr` 后各加一行 `if(my_ptr<mem_heap_lo)mem_heap_lo=my_ptr;`（仅 `new` 时）。
   - 新增访问器 `vm_get_mem_capacity()`(=heap_tail-mem0=缓冲区总量) / `vm_get_mem_peak_used()`(=（mem_stack_hi-mem0)+(heap_tail-mem_heap_lo)，保守上界，clamp 到 cap)。`.h` 同步声明。
2) **协议**（`msb_protocol.h`）：`VmStatsC` 加 `u32 mem_capacity`+`u32 mem_peak_used`，**28B→36B**，`STATIC_ASSERT` 同步改 36。
3) **固件**（`upload.c`）：`upload_lower_io_and_vm_stats` 填 `mem_capacity`/`mem_peak_used`（HAS_DIVER_RUNTIME 下调访问器，否则 0）。
4) **c_core**（`msb_thread.c`）：0x72 解析全程用 `sizeof(VmStatsC)`，结构体长大**自动适配**，仅需重编。
5) **wrapper**（`MCUSerialBridgeCLR.cs`）：`VmStats` 加 `MemCapacity`/`MemPeakUsed`；新增 `EffectiveMicros`（优先 `cycles*1e6/CpuHz`，与 `LastMicros` 偏差 > max(2000us, micros*50%) 才退回 micros）；`LoadPercent` 改用 `EffectiveMicros`；新增 `MemLoadPercent=100*peak/cap`。
6) **SDK**：`VmStatsSample` record 加 3 字段（MemCapacity/MemPeakUsed/MemLoadPercent），`VmStatsHistory.Add` 映射；`SimulatedMcuNode` 解析 `memCapacity`/`memPeakUsed`；`CoralinkerSimNodeHost/Program.cs` 存 `_memorySize` 并在 vmstats JSON 里发 `memCapacity=_memorySize`、`memPeakUsed=0`（模拟节点无真实峰值）。
7) **Host**（`ApiRoutes.cs`）：`/vmstats` JSON 加 `memCapacity`/`memPeakUsed`/`memLoadPercent`。
8) **前端**：`runtime.ts` 的 `VmStatsSample` 加 3 字段；`CpuLoadChart.vue` 重写成**上下两个 sparkline**——CPU LOAD（蓝，自动量程≥100%）+ MEM LOAD（紫，固定 0~100%），底部 stats 显示 `cycles / micros / memPeakUsed/memCapacity`。

### 验证（仅编译，真机由用户自测）
- c_core `scons -j8` 重编成功（msb_thread/bridge 重新编译，证明吃到了 36B 结构体）；DLL 已 `Copy-Item` 同步到 `build/runtimes/win-x64/native/`。
- 后端 `dotnet build -c Release` → 0 error；Host 输出 `runtimes/win-x64/native/mcu_serial_bridge.dll` 时间戳为最新（01:57）。
- 前端 `npm run build`（vue-tsc 类型检查通过，证明新加字段类型对齐）；bundle grep 到 `memLoadPercent`。

### 接下来要做 / 给用户
- 用户需**重新编固件 upg 并刷机**（这批协议改了 `VmStatsC` 36B + runtime 新访问器），否则旧固件发 28B 包、新 c_core 按 36B 解析会长度对不上被丢弃（`mem_packet->data_len` 校验不过 → 整包跳过，telemetry 收不到）。
- 用户当前正运行的旧 Host（`dotnet run` Debug, pid 18712）是旧版；要看新 UI/字段需重启用新构建（Release 产物已就绪）。
- BenchLogic 跑起来后：MEM LOAD 会显示「程序+statics+峰值栈+峰值堆」占 20KiB 的比例；`rounds` 调大主要增加 CPU，不太增内存（buf 是固定 static）。

### 重要发现 / 注意
- 【发现】VM 是单缓冲区 [mem0..heap_tail)：低地址 program+statics，stack 从 stack0 往上长，heap 从 heap_tail 往下长，中间空隙 = 余量。撑爆 = stack 顶撞上 heap 头。故 Memory Load 的分母是**整块缓冲区**，分子是**cycle 内同时占用峰值**。
- 【技能】高水位埋点要选「值真正变化的唯一地点」：栈只在 `vm_push_stack` 长（用 frame 的 `max_stack` 预留上界，无需逐 opcode 跟踪 eval 指针），堆只在 3 个分配器长。这样**完全不碰 opcode switch 主循环**，性能影响可忽略、代码一致性好。
- 【注意】`mem_peak_used` 是**保守上界**：峰值栈与峰值堆可能不同时发生，二者相加可能略高于真实瞬时峰值——对「会不会溢出」的预警来说偏保守是安全的。
- 【注意】`VmStatsC` 改成 36B 是 telemetry 包的**隐式 ABI**（固件↔c_core↔wrapper 三方必须同长）。这是本会话新增的 0x72，无历史包袱；但固件与上位机必须**同批次**部署。
- 【发现】`dotnet run`（默认 Debug→bin/Debug）与 `dotnet build -c Release`（bin/Release）输出目录不同，所以用户开着 Host 自测时我仍能编 Release 不被文件占用锁住。

---

## 2026-06-09 02:12 UTC+8 — CPU/Mem 负载图改成固定 15s 时间窗 + 数字右对齐布局

### 做了哪些事
- **布局**（用户要求）：每行从左到右 = 标题 · cycles · 时间 · 占用率%，把原来底部的「cycles / us」挪到百分比**前面**。所有数字用固定宽度列（`num-cyc`62/`num-time`50/`num-mem`76/`num-pct`38 px）+ `text-align:right` + `tabular-nums`，使 1ms↔100ms、1%↔100%、1M↔100M 右边缘不跳动。时间显示改 ms（DWT cycles 换算优先，micros 兜底；≥100ms 无小数，否则 1 位）。
- **固定 15s 时间窗**（用户要求 last 15s）：`CpuLoadChart.vue` x 轴从「按 index 等分」改成「按 timestamp 映射」。窗口锚定到**最新样本时间**（右边缘=now，避免浏览器/服务器时钟偏差），`[tMax-15000ms .. tMax]`。poll 合并后按时间裁剪（保留窗口内 + 4000 硬上限）。每 5s 一条竖向虚线网格，标题旁显示「15s」。
- **SDK 缓冲扩容**：`DIVERSession` 每节点 `VmStatsHistory` 从 240 → **1024**。原因：BenchLogic 50ms/轮 → 20样本/秒，15s 需 300 样本，旧 240 只够 12s。1024 条 50ms 下覆盖 ~51s，10ms 下也有 ~10s。

### 验证
- 后端 `dotnet build -c Release` 0 error；前端 `npm run build` vue-tsc 通过（修了 `noUncheckedIndexedAccess` 下索引访问可空：`samples[n-1]!`、`merged[len-1]!`）。

### 注意
- 【注意】这次只改了 SDK(后端) + 前端，**没动协议/固件**。用户要看 15s 窗口需用新 Release 重启 Host + 刷新页面；固件仍是上一条记录里的 36B telemetry 版（需自行编 upg 刷机）。
- 【发现】时间窗锚定「最新样本时间」而非浏览器 `Date.now()`：数据在流时每秒右移（滚动），停了就冻结在最后状态——对遥测可接受，且免去两端时钟不同步导致的曲线偏移。

---

## 2026-06-09 02:25 UTC+8 — 重新加回 CCM_RAM 优化（在 telemetry 版之上），待用户重测性能

### 接下来要做 / 给用户
- 用户自行编固件 upg + 刷机后重测：对比基线（v2.1 无 CCM）vs 本次 CCM 版的 cycles。BenchLogic 同一程序、同 rounds 下看 CPU LOAD 的 cycles 列下降幅度即为加速比。
- 【建议】打 tag 助记词：基线已是 `2.1`，本次建议 `2.2-ccm`（单助记词 `ccm`），方便确认刷的是 CCM 版。本次**不需要 bump ABI**（见下「注意」）。

### 做了哪些事（全部在 MCU 固件侧，上位机/前端不变）
- **修复 `MCU_FASTMEM` 宏**（`mcu_runtime.c` 顶部）：原定义是坏的 `attribute((section(".ccmram")))`（漏了 `__attribute__`，等于无效→静默没进 CCM）。改成带条件守卫的正确写法：仅当 `defined(CCMRAM_SIZE_KB) && CCMRAM_SIZE_KB>0` 时展开为 `__attribute__((section(".ccmram")))`，否则空（PC/sim 构建无影响）。
- **热点全局移入 CCM**（`mcu_runtime.c`）：`cur_il_offset`、`methods_table`、`method_detail_pointer`、`stack_ptr[32]`、`stack0`、`new_stack_depth`、`cart_IO_stored[]`、`program_desc_ptr/code_ptr/virt_ptr/statics_desc_ptr/statics_val_ptr`、`instanceable_class_layout_ptr`(结构体)、`instanceable_class_per_layout_ptr`、`cartIO_layout_ptr`、`mem0`、`heap_tail`、`heap_newobj_id`、`heap_obj[1024]`（8KiB，最大头），以及本会话新增的 telemetry 高水位 `mem_stack_hi`/`mem_heap_lo`。
- **VM 工作缓冲扩容 + 进 CCM**：`control.h` `PROGRAM_BUFFER_MAX_SIZE` 20KiB→48KiB；`control.c` `program_buffer_storage` 加 `CCM_RAM`。

### 验证（静态推理，无法本地跑 arm-gcc）
- CCM 预算 64KiB：48KiB(buffer) + 8KiB(heap_obj) + cart_IO_stored 128B + stack_ptr 128B + 各指针/标量 <100B + 既有 `upload_console_writeline_buffer`(CCM_RAM, ~PACKET_MAX_DATALEN) ≈ **57KiB < 64KiB**，留有余量。若超出链接器会直接报错。
- `mempool pool[32KiB]`（`mempool.c`）**未**带 CCM_RAM，仍在主 RAM，不占 CCM 预算——已确认，否则 57+32 会爆。
- `CCMRAM_SIZE_KB=64`/`USE_CCM_RAM=True` 来自 `bsp_config.py`(CORAL-NODE-V2.1) + `embedded.py` 注入 `env`；`mcu_runtime_env = env.Clone()`（SConscript:110，在 unilib 注入之后），故 `mcu_runtime.c` 一定拿到该宏定义，守卫成立、CCM 生效。

### 重要发现 / 注意
- 【注意】**CCM 不被 startup 拷贝/清零**（`flash.ld.in` 有 `_siccmram` 符号但启动汇编未引用 → 既不拷 .data 初值也不 zero-fill）。因此放入 CCM 的变量**绝不能依赖初值**。已逐一核对：`heap_newobj_id=1`/`mem0`/`heap_tail` 在 `vm_set_program`(1064/1067/1113) 赋值；`new_stack_depth` 在 vm_run 设置；`cart_IO_stored` 每周期 `reset_cart_IO_stored()`(3204) memset；`heap_obj[i]` 写后才读（首次分配 id==1 走 heap_tail 分支不读数组）——**全部先写后读，安全**。
- 【注意】本次**不 bump ABI**：只改了内存放置 + 缓冲容量，没动字节码 layout / opcode / 执行方式。注意 48KiB 是单机内存上限提升，不是跨节点兼容性问题。
- 【技能】给「热」变量加 CCM 要选 STM32F4 的 CCM RAM（Core-Coupled，零等待、与 DMA/总线矩阵隔离），把解释器最高频读写的表/指针/标量放进去即可提速；但务必配合「先写后读」核查，因为 CCM 无 startup 初始化。
- 【发现】坏宏 `attribute((...))` 在 GCC 下不是语法错误（被当成无名声明/被忽略），所以之前「以为开了 CCM」其实没生效——这类静默失效要靠看 `.map`/`objdump` 段分布或 `arm-none-eabi-size` 才能发现。

---

## 2026-06-09 02:33 UTC+8 — CCM 固件跑飞 HardFault：根因=CCM 不可取指 / native blob 在 CCM 执行

### 现象
- 刷入 CCM 版固件跑 BenchLogic → HardFault。VM 故障面板：`Method: DiverBench.BenchLogic..cctor()`，`Telemetry.cs:46`，`CFSR=0x00000100`，PC=`0x08017DE7`，寄存器里 R2/R12=`0x1000036E/0x100003A5`（**CCM 区 0x10000000**）。

### 诊断（关键技能）
- 【技能】`CFSR=0x100` → BFSR bit0 = **IBUSERR（取指总线错误）**，说明 CPU 去取指令时失败，而不是数据访问。
- 【技能】用 `arm-none-eabi-addr2line -f -i -p -e build/firmware.elf 0x08017DE7` 直接把故障 PC 反解到源码 → **`native_try_execute at mcu_runtime.c:916`**，即 `fn(arg_buffer)` 调用 native 函数指针的那行。一步定位，省去猜测。
- 用户原以为「没启用 native」，实测程序里确实带了 `native_arch_id==1` 的 raw ARM blob（DIVER 编译器为某方法发了 native 代码）。

### 根因
- 【发现】**STM32F405 的 CCM RAM（0x10000000）只接到 CPU 的 D-bus，不接 I-bus → 无法从 CCM 取指令执行**（也不可被 DMA 访问）。native blob 内嵌在「程序缓冲区」里，原来缓冲在主 SRAM 可就地执行；我把缓冲移进 CCM 后，`func_entry = native_exec_blob + offset` 落在 CCM，`fn(...)` 一调用就 IBUSERR。
- 注意：CCM 存「数据」完全没问题（栈/堆/heap_obj/指针表照常用 D-bus 读写），**唯独不能放要被执行的 native 代码**。

### 修复（`mcu_runtime.c`，保留 CCM 数据加速）
- 仿照 `_WIN32` 路径（VirtualAlloc+PAGE_EXECUTE 把 blob 拷到可执行内存），给 MCU 新增 `#elif defined(IS_MCU)` 分支：`parse_native_chunk` 里把 native blob 从程序缓冲（CCM）`malloc` 一块普通 SRAM（堆在主 RAM，I-bus 可取指）并 `memcpy` 过去，按 `native_alignment` 对齐，加 `dsb/isb`（Cortex-M4 无 I-cache，仅需保证写序先于取指），`native_exec_blob` 指向 SRAM 副本。
- 新增 `native_exec_blob_raw` 记录 malloc 原始基址；`release_native_metadata` 加 `#elif defined(IS_MCU)` 分支 `free(native_exec_blob_raw)`。
- 失败兜底：malloc 失败 → `native_exec_blob=NULL` → `native_try_execute` 走 `goto cleanup` 退回解释执行，**不崩**。

### 给用户
- 重编固件 upg + 刷机重测。这次 CCM 数据加速保留（栈/堆/缓冲读写在 CCM），native 方法从 SRAM 副本执行，二者兼得。
- 【建议】tag 助记词 `2.3-ccmfix`（或在 `2.2-ccm` 基础上 `ccmfix`）。

### 注意
- 【注意】blob 可被任意重定位执行的前提是它**位置无关（相对跳转）**——`_WIN32` 路径早就拷到任意地址跑，且 MCU 原来就地址随缓冲落点而变，已证明 PIC，故拷到 malloc 地址安全。
- 【注意】native 函数运行时访问的 VM 堆对象在 CCM 里 → 那是**数据**访问，走 D-bus 完全正常；只有「取指」不能在 CCM。

---

## 2026-06-09 02:44 UTC+8 — native blob 改用 mempool_malloc + 编译 size 余量盘点

### 改动（按用户要求）
- native blob 重定位的目标内存从 newlib `malloc` 改为用户自己的 `mempool_malloc`（`mcu_runtime.c` IS_MCU 分支 + `#include "util/mempool.h"`）。去掉 `native_exec_blob_raw`/`free` 路径。
- 理由：`mempool_malloc` 是 bump 分配器（`allocated_size` 只增、无 free）。用户确认**每次 Load 前会发 Reset（`control_on_reset`→`hal_nvic_reset`→`NVIC_SystemReset` 整机复位）**，复位后 `allocated_size`(.bss) 归零、外设重新 boot 分配，所以 per-load 分配天然无泄漏，不需要 free。
- 兜底：`mempool_malloc` 满返回 0 → `native_exec_blob=NULL` → `native_try_execute` 退回解释执行，不崩。
- 时机澄清：`parse_native_chunk` 在 `vm_set_program()` 里调用，**每次下载程序一次**，不是每周期；所以不是「每周期 malloc」。

### 编译 size 余量（`arm-none-eabi-size -A -x build/firmware.elf`，CCM 版）
- **CCM（`.ccmram` @0x10000000，共 64KiB）**：已用 `0xDD4C`=55.3KiB → **剩 ≈8.7KiB**。
- **主 SRAM（@0x20000000，共 128KiB）**：`.bss`=83.7KiB（含 32KiB `pool[]`）+ `.data`≈1.1KiB + heap_stack 预留，静态到 `0x20015968` → **到栈顶剩 ≈41.6KiB**（newlib 堆+主栈增长区）。程序缓冲 48KiB 挪去 CCM 后主 RAM 反而宽裕。
- native blob 拷进 `pool[]`（在 .bss/主 SRAM，I-bus 可执行 ✓）；blob 实测一般几百 B~几 KB，mempool 32KiB 够用。

### 注意
- 【发现】`init_nvic()`(nvic.c:12) 里 `RCC->AHB1ENR |= RCC_AHB1ENR_CCMDATARAMEN` 使能了 CCM 数据 RAM 时钟——这是 CCM **数据**可访问的前提（否则连读写都 BusFault）。但使能也只给 D-bus，**取指照样不行**。
- 【注意】runtime 里其余 `malloc/free`（native 元数据 663-666、每次 native 调用的 `arg_buffer` 886）仍走 newlib，且都有配对 free，未动；只把「需可执行」的 blob 换成 mempool。
