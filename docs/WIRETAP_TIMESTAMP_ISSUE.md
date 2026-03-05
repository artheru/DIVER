# WireTap 时间戳问题分析与改进

## 问题现象

WireTap 日志和 Console.WriteLine 日志的时间戳出现大量聚集现象：

```
12:25:30.737  Variable Initialized
12:25:30.737  Motor4 Reset Sent
12:25:30.737  Motor4 Bootup Received
12:25:30.738  Motor4 Start Sent
12:25:30.738  TPDO1 OK M4 speed=276160 ...
12:25:30.738  Still Waiting
12:25:30.738  Still Waiting
12:25:30.738  Still Waiting
...
```

CAN 报文的 recentFrames 时间戳也有类似聚集 -- 多条报文共享同一毫秒。

## 根因

所有时间戳都是 **Host PC 端的 `DateTime.Now`**，USB 批量传输导致一批数据到达时所有帧共享同一毫秒时间戳。

协议中 `PayloadHeader.timestamp_ms` 字段已存在但 MCU 端始终填 0，桥接层完全忽略该字段。

## 解决方案：MCU 端硬件时间戳端到端贯通

### 设计原则

1. 不做兼容性处理 — 如果收到 `timestamp_ms=0`，说明链路有地方没改对
2. 时间戳在 caller 第一行保存 — 所有发送数据包的调用方，在函数入口第一行就保存 `g_hal_timestamp_us / 1000`
3. fatal_error.c 手动编码处也写入时间戳

### 改动文件清单

| 层 | 文件 | 改动 |
|----|------|------|
| MCU | `MCUSerialBridge/mcu/appl/source/upload.c` | `upload_port_data` / `upload_can_port_data` 增加 timestamp_ms 入参；4 个 caller 第一行保存时间戳；`upload_console_writeline` 写入时间戳 |
| MCU | `MCUSerialBridge/mcu/appl/source/control.c` | `control_upload_memory_lower_io` 写入时间戳 |
| MCU | `MCUSerialBridge/mcu/appl/source/fatal_error.c` | `fatal_error_send_impl` 增加 timestamp_ms 参数；两个 caller 禁中断前保存时间戳；`fatal_error_send_console_writeline` 写入时间戳 |
| Bridge | `MCUSerialBridge/c_core/include/msb_bridge.h` | 回调签名增加 `uint32_t timestamp_ms` 参数 |
| Bridge | `MCUSerialBridge/c_core/include/msb_handle.h` | `PortDataFrame` 增加 `timestamp_ms` 字段 |
| Bridge | `MCUSerialBridge/c_core/include/msb_packet.h` | `msb_parse_upload_data` 增加 timestamp_ms 参数 |
| Bridge | `MCUSerialBridge/c_core/src/msb_packet.c` | 传递 timestamp_ms 到回调和 PortQueue |
| Bridge | `MCUSerialBridge/c_core/src/msb_thread.c` | 从 PayloadHeader 取 timestamp_ms 传递给解析函数和回调 |
| CLR | `MCUSerialBridge/wrapper/MCUSerialBridgeCLR.cs` | P/Invoke 委托和公开回调增加 timestamp_ms 参数 |
| SDK | `3rd/CoralinkerSDK/DIVERSession.cs` | `WireTapDataEventArgs` / `WireTapLogEntry` 增加 `McuTimestampMs`；回调处理传递 |
| SDK | `3rd/CoralinkerSDK/MCUNode.cs` | `OnConsoleOutput` 事件增加 `uint` 参数 |
| Backend | `3rd/CoralinkerHost/Services/WireTapAggregatorService.cs` | 聚合数据携带 mcuTimestampMs |
| Backend | `3rd/CoralinkerHost/Web/ApiRoutes.cs` | CSV 导出增加 MCU_Timestamp_Ms 列；日志 API 返回 mcuTimestampMs |
| Frontend | `ClientApp/src/types/index.ts` | 类型增加 mcuTimestampMs / mcuTimestamp |
| Frontend | `ClientApp/src/api/device.ts` | `WireTapLogEntryFromBackend` 增加 mcuTimestampMs |
| Frontend | `ClientApp/src/stores/wiretap.ts` | 增加 `formatMcuTimestamp`；日志条目包含 mcuTimestamp |
| Frontend | `ClientApp/src/components/logs/CANAggregatedView.vue` | 显示 MCU 时间 (+mm:ss.mmm) |
| Frontend | `ClientApp/src/components/logs/WireTapLogView.vue` | Serial 日志显示 MCU 时间 |

### 时间格式

- Host 时间：`HH:mm:ss.fff`（绝对时钟）
- MCU 时间：`+mm:ss.mmm`（从 SysTick 开始的相对时间，紫色显示）
- Console.WriteLine：`[HH:mm:ss.fff | MCU +mm:ss.mmm] message`
- CSV 导出：新增 `MCU_Timestamp_Ms` 列（原始毫秒值）
