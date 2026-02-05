# MCU Runtime Bugs

## 1. BitConverter.GetBytes(UInt16) 和 GetBytes(UInt32) 实现错误

**文件**: `mcu_runtime.c` 第 5087-5099 行

**问题**: `builtin_BitConverter_GetBytes_UInt16` 和 `builtin_BitConverter_GetBytes_UInt32` 的实现复制了 `ToUInt16/ToUInt32` 的逻辑，导致调用时 HardFault。

**错误代码**:
```c
void builtin_BitConverter_GetBytes_UInt16(uchar** reptr) {
    int startIndex = pop_int(reptr);           // ← 错！GetBytes 不需要 startIndex
    int array_id = pop_reference(reptr);       // ← 错！GetBytes 不需要 array_id
    struct array_val* arr = heap_obj[array_id].pointer;
    PUSH_STACK_UINT16(*(unsigned short*)(&arr->payload + startIndex));
}

void builtin_BitConverter_GetBytes_UInt32(uchar** reptr) {
    int startIndex = pop_int(reptr);           // ← 错！
    int array_id = pop_reference(reptr);       // ← 错！
    struct array_val* arr = heap_obj[array_id].pointer;
    push_int(reptr, *(unsigned int*)(&arr->payload + startIndex));
}
```

**正确实现应参考 GetBytes_Int16/Int32**:
```c
void builtin_BitConverter_GetBytes_UInt16(uchar** reptr) {
    unsigned short value = pop_short(reptr);
    int array_id = newarr(2, Byte);
    struct array_val* arr = heap_obj[array_id].pointer;
    *(unsigned short*)&arr->payload = value;
    PUSH_STACK_REFERENCEID(array_id);
}

void builtin_BitConverter_GetBytes_UInt32(uchar** reptr) {
    unsigned int value = pop_int(reptr);
    int array_id = newarr(4, Byte);
    struct array_val* arr = heap_obj[array_id].pointer;
    *(unsigned int*)&arr->payload = value;
    PUSH_STACK_REFERENCEID(array_id);
}
```

**临时规避**: 使用 `int` 类型代替 `uint`，调用 `BitConverter.GetBytes(int)` 可正常工作。

**发现日期**: 2026-02-05

---

## 2. PUSH_STACK_INT 会截断 int 的最高字节

**文件**: `mcu_runtime.c` 第 1103 行和第 3379 行

**问题**: `PUSH_STACK_INT` 宏会把 int 值的最高字节清零，导致负数显示为大正数。

### 相关定义

```c
#define STACK_STRIDE 8                           // 栈槽大小 8 字节
#define Int32 6                                  // Int32 的类型 ID
#define As(What, TType) (*(TType*)(What))        // 类型转换宏
```

### 问题代码

```c
#define PUSH_STACK_INT(val) *eptr = Int32; As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;
```

### 逐步分析

假设 `eptr` 当前指向地址 `0x1000`，要存储 `val = -500`

**步骤 1**: `*eptr = Int32`
- 在 eptr[0] 写入类型 ID = 6
- 内存布局: `[06][??][??][??][??][??][??][??]`
- 地址:      `0  1   2   3   4   5   6   7`

**步骤 2**: `As(eptr + 1, int) = val`
- 等价于 `*(int*)(eptr + 1) = val`
- 把 4 字节的 val 写到 eptr+1 的位置
- `-500` = `0xFFFFFE0C`，小端序存储为 `0C FE FF FF`
- 内存布局: `[06][0C][FE][FF][FF][??][??][??]`
- 地址:      `0   1   2   3   4   5   6   7`
- **注意**: val 的 4 个字节占据了 eptr[1] ~ eptr[4]

**步骤 3**: `((int*)eptr)[1] = 0`
- 把 eptr 转成 `int*`，然后访问第 2 个元素 (index=1)
- `((int*)eptr)[0]` = eptr[0~3]
- `((int*)eptr)[1]` = eptr[4~7]  ← **这是要清零的区域**
- 执行后: `[06][0C][FE][FF][00][00][00][00]`
- 地址:    `0   1   2   3   4   5   6   7`

**问题**: eptr[4] 被清零了，但它存储的是 val 的最高字节 0xFF！

### 读取时的值

读取 `*(int*)(eptr + 1)` 得到:
- 小端序: `0C FE FF 00` = `0x00FFFE0C` = **16776716**
- 本应是: `0C FE FF FF` = `0xFFFFFE0C` = **-500**

### 验证

用户的测试结果:
- `cart.leftRPM = -500`，Console 输出 `L=16776716`
- `cart.rightRPM = -1500` = `0xFFFFFA24`，Console 输出 `R=16775716` = `0x00FFFA24` ✓

### 正确实现

应该只清零 eptr[5~7]，不能动 eptr[4]:

```c
// 方案 1: 从 eptr+5 开始写 0
#define PUSH_STACK_INT(val) *eptr = Int32; As(eptr + 1, int) = val; eptr[5]=eptr[6]=eptr[7]=0; eptr+=STACK_STRIDE;

// 方案 2: 用 memset 清零后 3 字节
#define PUSH_STACK_INT(val) *eptr = Int32; As(eptr + 1, int) = val; memset(eptr+5, 0, 3); eptr+=STACK_STRIDE;
```

### 影响范围

**以下宏全部存在相同问题**（都使用了 `((int*)eptr)[1] = 0` 或 `((int*)*reptr)[1] = 0`）：

第一组定义（第 1099-1115 行）：
```c
#define PUSH_STACK_INT8(val)    *eptr = SByte;  As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;
#define PUSH_STACK_UINT8(val)   *eptr = Byte;   As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;
#define PUSH_STACK_INT16(val)   *eptr = Int16;  As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;
#define PUSH_STACK_UINT16(val)  *eptr = UInt16; As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;
#define PUSH_STACK_INT(val)     *eptr = Int32;  As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;
#define PUSH_STACK_UINT(val)    *eptr = UInt32; As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;
#define PUSH_STACK_REFERENCEID(val) *eptr = ReferenceID; As(eptr + 1, int) = val; ((int*)eptr)[1] = 0; eptr+=STACK_STRIDE;
```

第二组定义（第 3375-3386 行）：
```c
#define PUSH_STACK_INT8(val)    **reptr = SByte;  As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8;
#define PUSH_STACK_UINT8(val)   **reptr = Byte;   As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8;
#define PUSH_STACK_INT16(val)   **reptr = Int16;  As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8;
#define PUSH_STACK_UINT16(val)  **reptr = UInt16; As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8;
#define PUSH_STACK_INT(val)     **reptr = Int32;  As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8;
#define PUSH_STACK_UINT(val)    **reptr = UInt32; As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8;
#define PUSH_STACK_REFERENCEID(val) **reptr = ReferenceID; As(*reptr + 1, int) = val; ((int*)*reptr)[1] = 0; *reptr+=8;
```

### 影响症状

- 所有负数的 int 值会变成大正数
- 只有高字节为 0x00 的值（0 ~ 16777215）不受影响
- 影响所有算术运算结果、字段读取、Console 打印等

**发现日期**: 2026-02-05

---

## 3. Fatal Error 发生时 Console.WriteLine 输出丢失

**问题**: 当运行时发生致命错误（如数组越界）时，Console.WriteLine 的输出没有被发送到 Host，而是直接跳到了 Fatal Error 报告和 MCU 重启。

**复现步骤**:
```csharp
[LogicRunOnMCU(scanInterval = 1000)]
public class ErrorDemo : LadderLogic<CartDefinition>
{
    private byte[] buffer = new byte[8];
    private int index = 0;

    public override void Operation(int iteration)
    {
        Console.WriteLine($"index = {index}");
        buffer[index] = (byte)iteration;  // 当 index >= 8 时越界
        index++;
    }
}
```

**实际输出**:
```
index = 6
index = 7
FATAL ERROR: Array index out of range: 8/8
Disconnecting node due to fatal error...
```

**期望输出**:
```
index = 6
index = 7
index = 8   <-- 这一行应该出现在 FATAL ERROR 之前
FATAL ERROR: Array index out of range: 8/8
Disconnecting node due to fatal error...
```

**原因分析**: MCU 在 Operation 执行过程中，Console.WriteLine 可能是缓冲的。当致命错误发生时，MCU 优先发送 Fatal Error 回调并重启，没有先 flush Console 缓冲区。

**修复建议**: 在报告 Fatal Error 之前，先确保所有 Console 输出已经通过回调发送给 Host。

**发现日期**: 2026-02-05
