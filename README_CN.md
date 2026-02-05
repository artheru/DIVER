# DIVER (Dotnet Integrated Vehicle Embedded Runtime)

DIVER 是一个专门的运行时和编译器系统，使 C# 代码能够在微控制器 (MCU) 上运行，特别适用于汽车和机器人应用。

典型部署场景：主机系统（PC/服务器或 Nvidia Orin 等强大的嵌入式系统）运行 .NET 应用程序，通过串口或以太网与一个或多个运行 DIVER 运行时的 MCU 通信。DIVER 提供了一种健壮的"末梢神经"架构，MCU 处理实时 IO，主机处理复杂逻辑。

## 特性

- **C# 到 MCU 字节码编译** - 用熟悉的 C# 语法编写 MCU 逻辑
- **类 RTOS 运行时** - 可配置扫描间隔的确定性执行
- **双向数据交换** - UpperIO（主机→MCU）和 LowerIO（MCU→主机）
- **硬件 IO 抽象** - 支持 CAN、串口、GPIO、Modbus
- **调试源码映射** - 将运行时错误映射回 C# 源代码位置
- **Web 控制面板** - CoralinkerHost 提供可视化节点管理

## 架构

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              主机系统 (.NET)                                 │
│  ┌───────────────────────┐    ┌───────────────────────┐                     │
│  │   DIVERVehicle        │    │   LadderLogic<T>      │                     │
│  │   - IO 变量           │    │   - Operation()       │ ─── DiverCompiler ──┼──> .bin
│  │   - SetMCUProgram()   │    │   - cart 引用         │                     │
│  │   - SendUpperData()   │    └───────────────────────┘                     │
│  │   - NotifyLowerData() │                                                  │
│  └───────────┬───────────┘                                                  │
│              │ 串口/以太网                                                   │
└──────────────┼──────────────────────────────────────────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              MCU (DIVER 运行时)                              │
│  ┌───────────────────────┐    ┌───────────────────────┐                     │
│  │   vm_run(iteration)   │    │   硬件 IO             │                     │
│  │   - 执行字节码        │    │   - CAN、串口、GPIO   │                     │
│  │   - 处理 UpperIO      │    │   - Event/Stream/     │                     │
│  │   - 生成 LowerIO      │    │     Snapshot 缓冲区   │                     │
│  └───────────────────────┘    └───────────────────────┘                     │
└─────────────────────────────────────────────────────────────────────────────┘
```

## 核心原则

### 1. 关注点分离
- **MCU**: 实时 IO 处理、基本控制循环、确定性时序
- **主机**: 复杂逻辑、数据记录、用户界面、机器学习
- 清晰的边界防止时序问题影响实时操作

### 2. 数据流（UpperIO / LowerIO）

| 方向 | 属性 | 描述 | 示例 |
|------|------|------|------|
| 主机 → MCU | `[AsUpperIO]` | 控制命令、设定值 | `motorSpeed`、`targetPosition` |
| MCU → 主机 | `[AsLowerIO]` | 传感器数据、状态 | `actualSpeed`、`temperature` |
| 双向 | (无) | 互相交换 | `sharedCounter` |

### 3. 执行周期

每个扫描间隔（如 50ms）：
```
1. MCU 采集硬件 IO（GPIO、CAN、串口）
2. MCU 接收来自主机的 UpperIO
3. DIVER 运行时执行 Operation(iteration)
4. MCU 发送 LowerIO 回主机（包含 iteration 计数器）
5. 重复
```

## 项目结构

```
DIVER/
├── DiverCompiler/          # C# 到 MCU 字节码编译器（Fody weaver）
│   ├── Processor.cs        # 主编译逻辑
│   └── Processor.Builtin.cs # 内置方法处理
├── MCURuntime/             # MCU 执行的 C 运行时
│   ├── mcu_runtime.c       # VM 解释器、IO 处理
│   └── mcu_runtime.h       # 公共 API
├── DiverTest/              # 测试工具和示例
│   ├── DIVER/
│   │   └── DIVERInterface.cs  # DIVERVehicle、LocalDebugDIVERVehicle
│   ├── TestLogic.cs        # MCU 逻辑示例
│   └── RunOnMCU.cs         # MCU API 存根（ReadEvent、WriteStream 等）
├── MCUSerialBridge/        # 串口通信桥接（C + C# 封装）
│   ├── c_core/             # 原生 C 实现
│   ├── mcu/                # 使用桥接的 MCU 固件
│   └── wrapper/            # C# P/Invoke 封装
└── 3rd/
    ├── CoralinkerHost/     # Web 控制面板（ASP.NET + Vue 3）
    └── CoralinkerSDK/      # 多节点管理的 DIVERSession
```

## 类继承关系

```
CartDefinition（抽象基类）
    └── DIVERVehicle（抽象类）- 通信接口
            │   SetMCUProgram(mcuUri, program)   // 下载程序到 MCU
            │   SendUpperData(mcuUri, data)      // 发送 UpperIO 到 MCU
            │   NotifyLowerData(mcuUri, data)    // 处理来自 MCU 的 LowerIO
            │
            └── LocalDebugDIVERVehicle（抽象类）- PC 端测试
                    │   使用 MCUTestRunner（原生 DLL）模拟 MCU
                    │   不需要真实硬件
                    │
                    └── YourVehicle : LocalDebugDIVERVehicle
                            [AsLowerIO] sensorValue
                            [AsUpperIO] motorSpeed

LadderLogic<T> where T : CartDefinition
    └── YourLogic : LadderLogic<YourVehicle>
            cart     // 车辆实例引用
            Operation(int iteration)  // 每个扫描周期调用
```

## 快速开始

### 前置条件

- Visual Studio 2022
- .NET 8.0 SDK
- ARM GCC 工具链（用于真实 MCU 构建）

### 构建

1. 克隆仓库
2. 在 Visual Studio 中打开 `DIVER.sln`
3. 首先构建 DiverCompiler
4. 构建 DiverTest（这会将 MCURuntime 编译为 DLL 用于测试）
5. 运行 DiverTest 验证一切正常

### 快速开始：本地调试模式

无需真实硬件进行测试，使用 `LocalDebugDIVERVehicle`：

**1. 定义你的车辆（IO 变量）：**
```csharp
public class MyVehicle : LocalDebugDIVERVehicle
{
    [AsLowerIO] public int sensorValue;    // MCU → 主机
    [AsUpperIO] public int motorSpeed;     // 主机 → MCU
}
```

**2. 创建 MCU 逻辑：**
```csharp
[LogicRunOnMCU(scanInterval = 50)]  // 50ms 扫描间隔
public class MyLogic : LadderLogic<MyVehicle>
{
    public override void Operation(int iteration)
    {
        // 读取传感器，应用控制逻辑
        if (cart.sensorValue > 100)
            cart.motorSpeed = 0;
        else
            cart.motorSpeed = 50;
        
        Console.WriteLine($"迭代 {iteration}: 传感器={cart.sensorValue}");
    }
}
```

**3. 运行主机：**
```csharp
static void Main()
{
    var vehicle = new MyVehicle();
    vehicle.Start(Assembly.GetExecutingAssembly());
}
```

### 真实硬件部署

对于真实 MCU 部署，直接实现 `DIVERVehicle`：

**主机端：**
```csharp
public class RealVehicle : DIVERVehicle
{
    private SerialPort _port;
    
    public override void SetMCUProgram(string mcuUri, byte[] program)
    {
        // 通过串口发送程序字节到 MCU
        _port.Write(program, 0, program.Length);
    }
    
    public override void SendUpperData(string mcuUri, byte[] data)
    {
        // 发送 UpperIO 数据到 MCU
        _port.Write(data, 0, data.Length);
    }
    
    // 从 MCU 接收数据时调用 NotifyLowerData()
}
```

**MCU 端（C）：**
```c
void main()
{
    vm_set_program(program_from_host);
    
    int iteration = 0;
    while (1)
    {
        // 采集硬件 IO
        vm_put_snapshot_buffer(gpio_states, gpio_size);
        vm_put_event_buffer(can_messages, can_size);
        
        // 从主机接收 UpperIO
        vm_put_upper_memory(upper_buffer, upper_size);
        
        // 执行一次迭代
        vm_run(iteration++);
        
        // 发送 LowerIO 到主机
        send_to_host(vm_get_lower_memory(), vm_get_lower_memory_size());
    }
}
```

## 硬件 IO 类型

| 类型 | API | 使用场景 |
|------|-----|----------|
| **Event** | `ReadEvent(port, id)` / `WriteEvent(data, port, id)` | CAN 消息、Modbus 帧 |
| **Stream** | `ReadStream(port)` / `WriteStream(data, port)` | 串口 UART 数据 |
| **Snapshot** | `ReadSnapshot()` / `WriteSnapshot(data)` | GPIO 状态、模拟量值 |

## 自定义内置函数

添加可从 C# 调用的原生 C 函数：

**1. 在 C# 中声明存根：**
```csharp
// ExtraMethods.cs
public static class MyBuiltins
{
    public static int FastCalculation(int a, int b)
    {
        throw new NotImplementedException(); // 在 C 中实现
    }
}
```

**2. 生成头文件：**
```bash
DiverCompiler.exe -g
```

**3. 在 C 中实现：**
```c
// additional_builtins.h
void builtin_FastCalculation(uchar** reptr)
{
    int b = pop_int(reptr);  // 参数顺序相反！
    int a = pop_int(reptr);
    int result = a * b + (a >> 2);
    push_int(reptr, result);
}
```

**4. 在逻辑中使用：**
```csharp
public override void Operation(int iteration)
{
    int result = MyBuiltins.FastCalculation(10, 20);
    cart.outputValue = result;
}
```

## 调试源码映射（`*.diver.map.json`）

编译器生成用于调试 MCU 错误的源码映射：

| 字段 | 描述 |
|------|------|
| `ilOffset` | 编译程序中的字节偏移 |
| `methodIndex` | 输出中的方法索引 |
| `diverLine` | `.diver` 反汇编文件中的行号 |
| `methodName` | 完全限定方法名 |
| `sourceFile` | 原始 C# 文件名 |
| `sourceLine` | C# 源代码中的行号 |

**示例：**
```json
[
  {"ilOffset":239,"methodIndex":0,"diverLine":1,"methodName":"MyLogic.Operation(Int32)","sourceFile":"MyLogic.cs","sourceLine":15}
]
```

当 MCU 发生致命错误时，查找满足 `ilOffset <= error.ilOffset` 的最大条目即可定位 C# 源代码。

## CoralinkerHost（Web 控制面板）

`3rd/CoralinkerHost` 项目提供了一个 Web 界面，用于：

- **节点管理**: 添加/删除/配置 MCU 节点
- **可视化编程**: 基于 Vue-flow 的节点图
- **变量监视器**: 实时 UpperIO/LowerIO 监控
- **构建系统**: 编译 C# 逻辑并部署到节点
- **错误处理**: 致命错误对话框，支持跳转到源代码

详见 `3rd/CoralinkerHost/README.md`。

## 内置变量

### `__iteration`

每个节点都有一个内置的 `__iteration` 变量：

- **来源**: MCU 每次执行 `vm_run(i++)` 时递增
- **传输**: LowerIO 数据包的前 4 字节
- **属性**: LowerIO（只读），每个节点独立
- **显示**: 在变量面板中显示为 `{节点名}.__iteration`

## 故障排除

| 问题 | 解决方案 |
|------|----------|
| 缺少 `extra_methods.txt` | 运行 `DiverCompiler.exe -g` |
| "Unknown cart descriptor" | 检查车辆类中的字段类型 |
| 调用内置函数时主机崩溃 | 验证 C 中的 pop/push 顺序（相反！） |
| MCU HardFault | 检查源码映射，在运行时启用 `_VERBOSE` |

## 许可证

[您的许可证]
