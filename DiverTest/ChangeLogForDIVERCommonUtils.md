# ChangeLog — DIVERCommonUtils 重构及相关修复

> 基于 artheru commit `455646b` (2026-03-04) 的后续整理工作  
> 编写日期: 2026-03-04

---

## 一、artheru 在 commit 455646b 中做的事情

### 1. MCURuntime 栈操作 bug 修复 (mcu_runtime.c)

修复了 `PUSH_STACK_*` 宏中 `((int*)eptr)[1] = 0` 会覆盖 value 最高字节的 bug：

```c
// 修复前: 清零 eptr[4~7]，但 eptr[4] 是值的一部分
((int*)eptr)[1] = 0;

// 修复后: 只清零 eptr[5~7]
eptr[5]=eptr[6]=eptr[7]=0;
```

涉及两组宏定义（`eptr` 版本和 `*reptr` 版本），共 14 个宏全部修复。

### 2. Fatal Error 前 flush console (mcu_runtime.c)

在 PC 版 `report_error` 中增加了 `flush_console()` 调用（`fflush(stdout)`），修复了致命错误发生时 Console.WriteLine 输出丢失的问题。

### 3. DIVERCommonUtils.cs 初稿（未完成的重构）

artheru 的意图：
- 将 `PField` 和 `LogicInfo` 从 `DIVERInterface.cs` 的内部私有类提升为公开类
- 将 `NotifyLowerData` 的序列化/反序列化逻辑从 `DIVERVehicle` 实例方法抽取到静态工具类 `DIVERCommonUtils`，使其可在不同上下文复用
- 新签名 `NotifyLowerData(byte[] lowerIOData, object target)` 中的 `target` 参数打算替代 `this`

**但这个重构只做了一半**：
- namespace 写成了 `DiverTest.DIVER` 而不是 `CartActivator`
- 方法体是从 `DIVERInterface.cs` 直接复制的，`this`/`mcu_logics`/`mcuUri`/`CoerceValue`/`SendUpperData` 等引用未改造
- 导致编译失败（8 个 CS 错误）

### 4. DIVERInterface.cs 中的改动

- 在 `NotifyLowerData` 循环开头增加了 `if (tup.fields[cid].isUpper) continue;`
- 删除了 `PField` 和 `LogicInfo` 的内部类定义（已移到 DIVERCommonUtils.cs）

---

## 二、本次 Session 完成的工作

### A. BUGS.md 更新

将 `MCURuntime/BUGS.md` 中三个 bug 全部标记为已修复：

| Bug | 状态 | 修复说明 |
|-----|------|---------|
| #1 BitConverter.GetBytes(UInt16/UInt32) | ✅ 已修复 | 当前代码已采用正确实现（确认修复，非本次 commit） |
| #2 PUSH_STACK_INT 截断最高字节 | ✅ 已修复 | commit 455646b |
| #3 Fatal Error 时 Console 输出丢失 | ✅ 已修复 | commit 455646b（PC 版），MCU 版本次补全 |

### B. MCU 端 Fatal Error 前 flush console 实现

PC 版的 `flush_console()` 不适用于 MCU（MCU 的 console 输出走 USB 串口上传协议，不是 stdout）。MCU 有两条崩溃路径都需要在发送错误包之前先发送缓冲的 Console.WriteLine 数据：

| 路径 | 触发场景 | 入口 |
|------|---------|------|
| 路径 1 | 主动 ASSERT（数组越界等） | `vm_glue.c` → `report_error()` |
| 路径 2 | 硬件异常（HardFault） | `vm_core_dump.c` → `core_dump_handler()` |

改动清单：

1. **`fatal_error.h`** — 新增 `fatal_error_send_console_writeline(const void* data, uint32_t length)` 声明
2. **`fatal_error.c`** — 实现该函数：复用静态缓冲区，构建 `CommandUploadConsoleWriteLine` 包，`hal_usart_send_sync` 阻塞发送一次，不禁中断不复位（由后续 send_string/send_coredump 负责）
3. **`upload.c`** — 补全 `upload_console_writeline_fatal()`：调用 `fatal_error_send_console_writeline` 发送缓冲区后清零；新增 `#include "appl/fatal_error.h"`
4. **`vm_core_dump.c`** — 路径 2：在 `fatal_error_send_coredump()` 前插入 `upload_console_writeline_fatal()` 调用
5. **`vm_glue.c`** — 路径 1：原来直接调用 `fatal_error_send_console_writeline(buffer, length)` 改为 `upload_console_writeline_fatal()`，不再直接引用内部缓冲区变量
6. **`mcu_runtime.h`** — `flush_console` 不暴露到头文件（含义不明确，MCU 版走 upload 通道）

### C. 完成 artheru 的 DIVERCommonUtils 重构

顺着 artheru 的思路，完成了他未做完的静态化重构：

**`DIVERCommonUtils.cs`**：
- namespace 改为 `CartActivator`（与 `DIVERInterface.cs` 一致）
- `PField` / `LogicInfo` 保持为 public 类
- `NotifyLowerData` 改为完整可用的静态方法，签名：
  ```csharp
  public static void NotifyLowerData(
      byte[] lowerIOData,
      object target,          // 替代原来的 this
      LogicInfo tup,          // 由调用方查字典后传入
      Action<byte[]> sendUpperData)  // 闭包捕获 mcuUri
  ```
- `CoerceValue` 也搬入，改为 `public static`

**`DIVERInterface.cs`**：
- `NotifyLowerData` 瘦身为 ~10 行 wrapper：查 `mcu_logics` 字典，委托给 `DIVERCommonUtils.NotifyLowerData`
- 删除 `CoerceValue` 方法（已移走）

**`CoralinkerHost.csproj`**：
- 新增 `DIVERCommonUtils.cs` 的文件链接（该项目通过 Link 引用 DiverTest 的源文件，缺少新文件会导致编译失败）

### D. 编译验证

最终编译结果：DiverTest、CoralinkerHost 等项目全部通过。Coralinker_arch 有预存的不相关编译错误（缺少 System.Management 等），与本次改动无关。

---

## 三、建议

1. **commit message 请写清楚改动内容**。`update.` 作为 message 会导致后续排查困难，特别是这种包含 bug fix + 重构的混合提交。

2. **重构建议拆分提交**。bug fix（PUSH_STACK 宏修复）和重构（DIVERCommonUtils 提取）混在同一个 commit 里，增加了 review 难度。

3. **DIVERCommonUtils 后续可以继续扩展**。当前只搬了 `NotifyLowerData` 和 `CoerceValue`，如果有其他需要在不同上下文复用的序列化逻辑，也可以放进来。

4. **MCU 端的 `upload_console_writeline_fatal` 目前只发送一次**，不像 error 包那样重发 10 次。如果需要更可靠的 console 传输，可以考虑加重发逻辑（但通常 console 日志丢失可以接受，error 包不能丢）。

5. **`flush_console` 在 `mcu_runtime.h` 中没有声明**，PC 版作为文件内部函数保留。如果将来需要统一接口，可以考虑用 `#ifdef IS_MCU` 分别实现，但目前不暴露是合理的。
