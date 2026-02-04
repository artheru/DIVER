# DIVER 产品介绍视频脚本

**预计总时长**: 35-40 分钟  
**目标受众**: AGV/机器人开发者、嵌入式工程师  
**格式说明**: [台词] 为需要念的内容，[动作] 为需要操作的内容

---

## 第一章：Why DIVER?

**时长**: 3-4 分钟

### 1.1 移动机器人开发的痛点

[台词]

大家好，欢迎观看 DIVER 产品介绍。

在移动机器人开发中，底层硬件实时控制一直是效率瓶颈。

一方面，不同机器人、不同硬件的接口高度碎片化。IO 类型不统一、数量不固定；接口少了不够用，接口多了又会导致与算力单元（上位机）的通信协议急剧复杂化。
另一方面，许多硬件需要专用驱动和控制算法，必须在 MCU 上单独定制开发，而 MCU 的开发语言、工具链、调试方式又与上位机完全不同。

在传统方案中，每增加一种硬件：

MCU 侧要新增一套 C 代码，上位机要新增一套通信协议和对接逻辑，硬件规模一旦扩大，系统复杂度会呈指数级增长，可维护性迅速下降。

调试体验同样低效。
当控制逻辑出现问题时，通常只能：

在 MCU 代码中插入 printf，重新编译、烧录，连接串口、打开串口工具查看日志

这个流程缓慢、割裂，也极大限制了算法和系统迭代速度。
这些问题，直接拉低了移动机器人整体的研发效率。

[动作]
无

### 1.2 DIVER 的核心理念

[台词]
DIVER 是我们的移动机器人开发解决方案。DIVER 全称是 Dotnet Integrated Vehicle Embedded Runtime。是一套运行在MCU上的类操作系统，是一个专门面向移动机器人开发的整套方法论。

它的核心理念可以概括为三点：

**第一，变量表编程模式。**

DIVER 把所有的 IO 数据抽象成一张变量表。电机转速是一个变量，传感器读数是一个变量，控制指令也是一个变量。开发者只需要关心"我要读哪个变量、写哪个变量"，不需要关心底层是通过什么协议、什么硬件传输的。

这你在写上位机程序时，使用设备就如同直接访问一个结构体的字段一样简单。底层的通信、同步、刷新，DIVER 全部帮你处理好了。

**第二，周期性操作循环。**

DIVER 借鉴了 PLC 的扫描周期概念。你写的控制逻辑会被 MCU 周期性地执行，比如每 50 毫秒执行一次。每次执行时，输入变量已经是最新的值，你只需要计算输出变量就行了。

这种模式非常适合实时控制场景。你不需要处理中断、不需要管理线程，只需要专注于控制算法本身。

**第三，脊髓式分布式架构。**

这是 DIVER 最核心的设计思想。

你可以把上位机想象成大脑，MCU 节点想象成脊髓。大脑负责高层决策，比如路径规划、任务调度；脊髓负责底层反射，比如电机控制、传感器采集。可以有多块脊椎，每块脊椎控制一部分硬件，然后总体数据处理好后全部上传至大脑决策。

大脑和脊髓之间通过变量表交换数据。大脑下发目标速度，脊髓返回实际速度。双方互不感知对方的内部实现。

更重要的是：
开发者只需编写一套 C# 项目，定义变量表和控制逻辑，DIVER 会自动将其部署到所有 MCU 节点执行。
无论节点数量多少、MCU 型号如何，都运行同一套逻辑。

这显著降低了多节点机器人系统的开发复杂度。

[动作]
无

### 1.3 DIVER 的优势总结

[台词]
总结一下，DIVER 为移动机器人开发带来的核心价值时：

第一，高开发效率。
使用 C# 编写控制逻辑，语法简洁，编译速度快。一套代码，多节点运行。

第二，天然分布式、易扩展。
通过增加 MCU 节点即可扩展 IO 数量和功能，无需重构系统。

第三，调试体验显著提升。
所有 IO 变量可实时可视化，无需 printf、无需串口调试。
运行时错误可直接定位到 C# 源码行号。

第四，系统架构清晰。
上位机与下位机通过变量表解耦。
新增硬件只需扩展变量定义，不需要修改通信协议。

接下来，我会带大家完整体验一遍 DIVER 的功能。

[动作]
无

---

## 第二章：界面概览

**时长**: 3-4 分钟

### 2.1 整体布局

[台词]
这是 DIVER 的 Web 控制台。

界面分为几个主要区域。

左上角是节点图区域，每个方块代表一个 MCU 节点，显示节点的连接状态、运行状态、端口配置等信息。

右侧是变量面板，显示所有 IO 变量的实时值。绿色背景的是可写变量，橙色背景的是只读变量。

下方是终端面板，包含控制台日志输出和端口数据抓包结果。

左下角是文件浏览器，管理逻辑代码文件。

[动作]
打开 DIVER Web 控制台，鼠标依次指向各个区域

### 2.2 顶部工具栏

[台词]
顶部工具栏从左到右依次是：

Status 显示当前会话状态，Idle 表示空闲，Running 表示运行中。

Build 按钮用于编译 C# 逻辑代码。

Start 和 Stop 按钮用于启动或停止整个系统。

New、Save、Load 用于项目管理。

Add Node 用于添加新的 MCU 节点。

[动作]
鼠标悬停在各个按钮上

### 2.3 节点视图详解

[台词]
点击展开一个节点，可以看到详细信息。

最上面是节点名称和连接地址。

BASE 区域显示硬件信息，包括 URI 连接串。

CONFIG 区域可以配置逻辑文件。

PORTS 区域显示所有端口的配置和统计信息。每个端口显示类型、名称、波特率，以及 TX 和 RX 计数器。

注意端口旁边有两个小圆点，这是端口数据抓包开关，点击可以开启端口监听，稍后我会详细演示。

[动作]
点击节点展开详情，鼠标指向各个区域

---

## 第三章：快速开始演示

**时长**: 3-4 分钟

### 3.1 目标说明

[台词]
在深入讲解之前，我们先用两分钟跑一个最简单的例子，让大家对整个流程有个直观感受。

我们的目标是：写一个逻辑，让 MCU 每秒输出一行 Hello DIVER。

[动作]
点击 New 新建项目

### 3.2 添加节点

[台词]
首先，点击 Add Node 添加 MCU 节点。

选择连接方式，这里用串口连接。选择 COM 端口，波特率用默认的 1000000。

点击 Probe，系统会与设备通信，获取硬件信息。

探测成功后，显示了设备的版本号和硬件布局。点击 Add 添加节点。

[动作]
点击 Add Node，选择串口和参数，点击 Probe，等待探测成功，点击 Add

### 3.3 编写逻辑

[台词]
节点添加完成，现在来写逻辑代码。

在文件浏览器中，进入 inputs 文件夹，新建一个 HelloWorld.cs 文件。

代码很简单，只有十几行。

首先定义一个类，继承自 LadderLogic。加上 LogicRunOnMCU 标注，设置扫描周期为 1000 毫秒，也就是每秒执行一次。

然后实现 Operation 方法，这是 DIVER 的核心入口。每个扫描周期，MCU 都会调用这个方法。

在 Operation 里面，我们调用 Console.WriteLine 输出一行日志，包含当前的迭代次数。

[动作]
在文件浏览器中新建文件 inputs/HelloWorld.cs，输入代码：

```csharp
using CartActivator;

[LogicRunOnMCU(scanInterval = 1000)]
public class HelloWorld : LadderLogic<CartDefinition>
{
    public override void Operation(int iteration)
    {
        Console.WriteLine($"Hello DIVER! iteration={iteration}");
    }
}
```

### 3.4 编译和运行

[台词]
代码写好了，按 Ctrl+S 保存，然后点击 Build 编译。

可以看到 Build 面板显示编译进度，几秒钟就完成了。

编译成功后，在节点配置里选择 Logic 为 HelloWorld。

现在点击 Start，启动系统。

看 Terminal 面板，每秒都会输出一行 Hello DIVER，iteration 从 0 开始不断递增。

这就是 DIVER 的基本工作流程：写代码、Build、选择 Logic、Start。整个过程不到两分钟。

点击 Stop 停止运行。

[动作]
Ctrl+S 保存，点击 Build，等待编译成功，在节点上选择 Logic，点击 Start，观察 Terminal 输出，点击 Stop

---

## 第四章：节点管理

**时长**: 4-5 分钟

### 4.1 添加节点详解

[台词]
刚才我们快速添加了一个节点，现在详细讲解一下节点管理功能。

点击 Add Node，可以看到支持多种连接方式。最常用的是 Serial 串口连接。如果设备支持以太网，也可以用 TCP 连接。

串口连接需要配置端口号和波特率。波特率默认是 1000000，也就是 1M，最高可以支持到 2M。高波特率可以提升数据交换的实时性。

[动作]
点击 Add Node，展示连接方式选项，修改参数演示

### 4.2 探测和升级

[台词]
点击 Probe 后，系统会与设备通信，获取三类信息：

第一是版本信息，包括固件版本号和硬件型号。

第二是硬件布局，告诉你这个设备有多少个 Serial 端口、多少个 CAN 端口、多少个数字 IO。

第三是当前状态，是否已经在运行等。

如果检测到固件版本过旧，可以点击 Upgrade 升级固件。升级过程会显示进度条，完成后自动重新连接。

[动作]
点击 Probe，展示探测到的信息，指向 Upgrade 按钮

### 4.3 端口配置

[台词]
节点添加后，需要根据实际硬件配置端口参数。

点击节点展开详情，在 PORTS 区域可以看到所有端口。

对于 Serial 端口，主要配置三个参数：名称，比如 RS485-1；波特率，比如 115200；帧超时时间，用于判断一帧数据是否接收完成，设为 0 表示使用默认值。

对于 CAN 端口，主要配置：名称，波特率，常用 500K 或 1M；重试时间，发送失败后多久重试。

修改完成后，配置会自动保存。

[动作]
点击节点进入配置，修改 Serial 端口参数，修改 CAN 端口参数

### 4.4 多节点场景

[台词]
DIVER 的一个重要特性是支持多节点。

一个典型的 AGV 可能有这样的架构：

节点 1 负责运动控制，连接左右轮的伺服驱动器，处理速度环和位置环。

节点 2 负责传感器采集，连接激光雷达、IMU、超声波等。

节点 3 负责 IO 控制，连接急停按钮、指示灯、继电器等。

在 DIVER 里，这三个节点可以运行同一个 Logic 项目。每个节点访问各自的端口，数据汇总到同一张变量表。上位机看到的就是一个统一的接口，不需要关心数据来自哪个节点。

这就是我们说的"一套代码，多节点执行"。

[动作]
如果有多个设备，可以添加第二个节点演示

---

## 第五章：Logic 开发详解

**时长**: 5-6 分钟

### 5.1 LadderLogic 概念

[台词]
现在来详细讲解 Logic 开发。

DIVER 的核心编程模型叫 LadderLogic，借鉴了 PLC 的梯形图思想。

它的核心概念是：程序按固定周期循环执行，每个周期读取输入、执行逻辑、写入输出。

在 DIVER 里，每个 Logic 类需要满足三个条件：

第一，继承自 LadderLogic 泛型类，泛型参数是你的 Vehicle 类，也就是变量表定义。

第二，实现 Operation 方法，这是控制逻辑的入口，MCU 每个扫描周期都会调用它。

第三，加上 LogicRunOnMCU 标注，指定扫描周期，单位是毫秒。比如设为 50，就是每 50 毫秒执行一次。

[动作]
展示代码结构

```csharp
[LogicRunOnMCU(scanInterval = 50)]
public class MyLogic : LadderLogic<MyVehicle>
{
    public override void Operation(int iteration)
    {
        // 每 50ms 执行一次
    }
}
```

### 5.2 变量表：UpperIO 和 LowerIO

[台词]
DIVER 的数据交换通过变量表实现，变量表定义在 Vehicle 类中。

变量分为两种方向：

UpperIO，标注 AsUpperIO 属性，表示从上位机到 MCU 的方向，用于下发控制指令。比如目标速度、目标角度、使能开关。

LowerIO，标注 AsLowerIO 属性，表示从 MCU 到上位机的方向，用于上报状态。比如实际速度、实际角度、错误码。

如果一个变量不加标注，默认是双向的，上位机和 MCU 都可以读写。

这种设计的好处是：变量的含义和方向非常清晰，看一眼定义就知道哪些是输入、哪些是输出。而且 DIVER 会自动优化数据传输，只传输有变化的数据，减少通信开销。

[动作]
展示代码示例

```csharp
public class MyVehicle : LocalDebugDIVERVehicle
{
    [AsUpperIO] public int targetSpeed;    // 上位机下发
    [AsUpperIO] public int targetAngle;    
    
    [AsLowerIO] public int actualSpeed;    // MCU 上报
    [AsLowerIO] public int actualAngle;    
    [AsLowerIO] public int errorCode;      
}
```

### 5.3 可用 API

[台词]
在 Logic 的 Operation 方法里，你可以调用这些 API：

调试输出，Console.WriteLine，输出会显示在 Terminal 面板，不需要接串口。

串口通信，ReadStream 读取数据，WriteStream 写入数据。参数是端口索引。

事件通信，用于 CAN 或 Modbus。ReadEvent 读取指定 ID 的消息，WriteEvent 发送消息。

GPIO 操作，ReadSnapshot 读取数字输入状态，WriteSnapshot 写入数字输出状态。

这些 API 的底层实现都在 MCU 上执行，具有实时性保证。

[动作]
无

### 5.4 Host 和 MCU 的协作模式

[台词]
最后讲一下 DIVER 的运行架构。

在 DIVER 系统中，Host 和 MCU 有明确的分工。

MCU 负责实时任务：执行 Operation 循环，读写硬件 IO，处理通信协议。这些任务对时间敏感，需要在毫秒级别完成。

Host 负责非实时任务：复杂业务逻辑，用户界面，数据存储，路径规划。这些任务可以容忍一定延迟。

两者之间通过变量表高速交换数据。Host 通过 UpperIO 下发指令，MCU 通过 LowerIO 上报状态。交换周期和扫描周期一致，比如 50 毫秒。

这种架构的好处是解耦。你可以单独修改上位机的业务逻辑，不影响 MCU 的实时控制。也可以更换 MCU 硬件，只要变量表兼容，上位机代码完全不用改。

[动作]
无

---

## 第六章：示例演示

**时长**: 22-26 分钟

### 6.1 示例一：Hello World

**时长**: 2 分钟

[台词]
第一个例子是最简单的 Hello World。不需要定义任何变量，只需要继承 CartDefinition 就行。

[动作]
创建文件 inputs/HelloWorld.cs

```csharp
using CartActivator;

[LogicRunOnMCU(scanInterval = 1000)]
public class HelloWorld : LadderLogic<CartDefinition>
{
    public override void Operation(int iteration)
    {
        Console.WriteLine($"Hello DIVER! iteration={iteration}");
    }
}
```

[台词]
代码很简单。LogicRunOnMCU 标注设置扫描周期为 1000 毫秒。继承 LadderLogic，泛型参数直接用 CartDefinition，表示不需要自定义变量。

Operation 方法是核心入口，每个扫描周期 MCU 都会调用它。这里我们用 Console.WriteLine 输出日志。

Build 并运行，看 Terminal 面板，每秒输出一行 Hello DIVER。

这是 DIVER 最基础的用法：不需要定义 IO，直接继承 CartDefinition。

[动作]
Build 并运行，观察 Terminal 输出

### 6.2 示例二：数值计算

**时长**: 3 分钟

[台词]
第二个例子演示变量表的使用。当你需要自定义输入输出变量时，要继承 CartDefinition 创建自己的变量表类。

我们定义三个变量：X1 和 X2 是输入，标注 AsUpperIO；Y 是输出，标注 AsLowerIO。Logic 的功能是 Y 等于 X1 加 X2。

[动作]
创建文件 inputs/NumericDemo.cs，输入代码

```csharp
using CartActivator;

public class NumericCart : CartDefinition
{
    [AsUpperIO] public int X1;
    [AsUpperIO] public int X2;
    [AsLowerIO] public int Y;
}

[LogicRunOnMCU(scanInterval = 100)]
public class NumericDemo : LadderLogic<NumericCart>
{
    public override void Operation(int iteration)
    {
        cart.Y = cart.X1 + cart.X2;
    }
}
```

[台词]
代码非常简单。在 Operation 里，我们通过 cart 对象访问变量表，cart.Y 等于 cart.X1 加 cart.X2。

Build 并 Start 运行。

看右侧 Variables 面板，X1 和 X2 显示绿色背景，表示可写，这是 UpperIO。Y 显示橙色背景，表示只读，这是 LowerIO。

现在我修改 X1 为 10。直接在面板上点击，输入数值，回车确认。

再修改 X2 为 20。

可以看到 Y 立刻变成了 30。

修改 X1 为 100，Y 变成 120。

这就是变量表的工作方式：上位机修改 UpperIO，MCU 计算结果写入 LowerIO，上位机立刻看到更新后的值。整个数据流是自动完成的。

[动作]
Build 并运行，在 Variables 面板修改 X1、X2，观察 Y 变化

### 6.3 示例三：调试输出

**时长**: 2 分钟

[台词]
第三个例子演示调试输出功能。

在开发过程中，我们经常需要打印日志来观察程序状态。传统做法是串口 printf，需要接线、打开串口助手。在 DIVER 里，直接用 Console.WriteLine 就行了。

[动作]
创建文件 inputs/DebugDemo.cs

```csharp
using CartActivator;

public class DebugCart : CartDefinition
{
    [AsUpperIO] public int X1;
    [AsUpperIO] public int X2;
    [AsLowerIO] public int Y;
}

[LogicRunOnMCU(scanInterval = 500)]
public class DebugDemo : LadderLogic<DebugCart>
{
    public override void Operation(int iteration)
    {
        Console.WriteLine($"[iter={iteration}] X1={cart.X1}, X2={cart.X2}, Y={cart.Y}");
        cart.Y = cart.X1 * cart.X2;
    }
}
```

[台词]
这个 Logic 每 500 毫秒执行一次，每次打印当前的变量值。

Build 并运行。

看 Terminal 面板，每行日志显示时间戳和内容。iteration 从 0 开始递增，变量值也实时显示。

现在我修改 X1 为 5，X2 为 7，看日志里的值也跟着变了，Y 变成 35。

这比传统的串口调试方便多了。不用接线，不用切换窗口，直接在同一个界面里完成开发和调试。

[动作]
Build 并运行，观察 Terminal 输出，修改变量看日志变化

### 6.4 示例四：数字输出控制

**时长**: 2 分钟

[台词]
第四个例子演示数字 IO 控制。AGV 上常见的指示灯、蜂鸣器、继电器，都是通过数字输出来控制的。

DIVER 提供了 WriteSnapshot 和 ReadSnapshot API 来操作数字 IO。

[动作]
创建文件 inputs/DigitalDemo.cs

```csharp
using CartActivator;

public class DigitalCart : CartDefinition
{
    [AsUpperIO] public bool ledEnable;      // 控制 LED 开关
    [AsUpperIO] public int blinkInterval;   // 闪烁间隔 (ms)
}

[LogicRunOnMCU(scanInterval = 50)]
public class DigitalDemo : LadderLogic<DigitalCart>
{
    private int timer = 0;
    private bool ledState = false;
    
    public override void Operation(int iteration)
    {
        if (cart.ledEnable && cart.blinkInterval > 0)
        {
            timer += 50;  // 扫描周期 50ms
            if (timer >= cart.blinkInterval)
            {
                timer = 0;
                ledState = !ledState;
                
                // 写入数字输出，端口 0，位 0
                WriteSnapshot(ledState ? 1 : 0, 0);
                Console.WriteLine($"LED = {ledState}");
            }
        }
        else
        {
            WriteSnapshot(0, 0);  // 关闭 LED
            timer = 0;
        }
    }
}
```

[台词]
这个 Logic 实现了一个可配置的 LED 闪烁功能。ledEnable 控制是否启用，blinkInterval 控制闪烁间隔。

WriteSnapshot 的第一个参数是要写入的值，第二个参数是端口索引。这里我们把 LED 连接在数字输出端口 0。

Build 并运行。

在 Variables 面板，把 ledEnable 设为 true，blinkInterval 设为 500。

可以看到 Console 输出 LED 状态每 500 毫秒切换一次。如果有实际硬件连接，LED 就会闪烁。

把 blinkInterval 改成 100，闪烁变快了。改成 1000，闪烁变慢。

把 ledEnable 设为 false，LED 停止闪烁。

[动作]
Build 并运行，设置 ledEnable 和 blinkInterval，观察 Console 输出，调整参数演示

### 6.5 示例五：端口通信与数据抓包

**时长**: 7-8 分钟

[台词]
第五个例子演示端口通信和数据抓包功能。这是调试 AGV 通信问题的核心工具。

传统做法是接逻辑分析仪或串口助手来看通信数据。DIVER 把这个功能内置了，不需要额外的工具，直接在界面上看 MCU 收发的每一帧数据。

我们写一个 Logic，周期性发送几种典型的工业协议报文：Modbus RTU 和 CANOpen。端口 0 到 3 是 RS485 串口，端口 4 是 CAN 总线。

[动作]
打开文件 inputs/PortDemo.cs，展示代码结构

[台词]
这个 PortDemo 每 2 秒发送一轮示例报文。

Modbus 部分通过端口 0 发送两条报文：一条是读保持寄存器的请求，8 个字节；另一条是模拟的响应报文，包含 10 个寄存器的数据，一共 25 个字节。这个长报文可以演示折叠功能。

CANOpen 部分通过端口 4 发送四条消息：两条 SDO 请求，分别读取 0x6041 Statusword 和 0x1017 心跳时间；一条 NMT 启动节点命令；一条 Heartbeat 心跳。

这些都是工业控制中最常见的报文类型。

Build 并运行。

现在来演示端口数据抓包功能。

在节点视图里，找到端口统计区域。每个端口旁边有 TX 和 RX 两个小圆点。TX 控制发送方向的监听，RX 控制接收方向。

点击 TX 圆点，开启发送监听，圆点变成橙色并发光。点击 RX 圆点，开启接收监听，圆点变成绿色。

可以只开 TX，或只开 RX，或两个都开。关闭的时候再点一次。

开启监听会略微增加 MCU 的负担，因为要把数据上报给 Host。正式运行时建议关闭，调试时再打开。

我们打开端口 0 的 TX 监听 Modbus 发送，再打开端口 4 的 TX 监听 CANOpen 发送。

[动作]
点击端口 0 的 TX 圆点、端口 4 的 TX 圆点开启抓包

[台词]
现在看下方的端口抓包日志面板。

面板分成多列。最左边是 Console 列，显示 Console.WriteLine 的输出。右边是各个端口的数据列。

每条数据显示：时间戳，精确到毫秒；方向，TX 或 RX；数据内容，十六进制格式。

如果是 Serial 数据，还会尝试 UTF-8 解码，如果是可读文本会显示出来。

如果是 CAN 数据，格式不一样，显示 COB-ID 和 DLC。

数据太长会自动折叠，比如那条 25 字节的 Modbus 响应，只显示前 8 字节、省略号、后 8 字节。点击可以展开看完整内容。

这些日志存储在后端内存里，刷新网页不会丢失。下次点击 Start 时会自动清空上一次的日志。

[动作]
展示日志面板各部分，演示折叠和展开

[台词]
接下来演示两个非常实用的功能：字节 Inspect 和协议解析。

首先是字节 Inspect。对于二进制协议，我们经常需要知道某几个字节表示什么数值。

用鼠标选中一段十六进制数据，会自动弹出 Inspect 窗口。

窗口显示这几个字节按不同格式解析的结果。

如果选中 1 个字节，显示 u8、i8、十六进制、字符。

如果选中 2 个字节，显示 u16 和 i16，分别有大端和小端两种解析。

如果选中 4 个字节，除了整数还有浮点数解析。

这个弹窗可以拖动位置，方便对照数据查看。

[动作]
选中 1 字节、2 字节、4 字节数据，展示不同的 Inspect 结果，拖动窗口

[台词]
Inspect 适合分析单个数值。如果要分析完整的协议帧，可以用协议解析功能。

点击数据条目旁边的放大镜按钮，系统会自动识别协议类型并解析。

目前支持两种协议：Modbus RTU 和 CANOpen。

先看 Modbus。点击端口 0 的数据，系统自动识别这是 Modbus RTU 格式，显示结构化的解析结果：从站地址、功能码、寄存器地址、数据内容、CRC 校验结果。

如果 CRC 校验失败，会用红色标出警告。这对排查通信问题非常有用。

[动作]
点击 Modbus 数据的放大镜按钮，展示解析结果

[台词]
再看 CANOpen。CANOpen 的解析更丰富。系统根据 COB-ID 识别消息类型：NMT、SDO、PDO、Heartbeat、Emergency。

看这条 SDO 消息，系统解析出命令类型是 Upload Request，Index 是 0x6041，SubIndex 是 0。

这里有一个亮点：系统不仅解析了原始字节，还查询了对象字典。

看这条读 0x6041 的请求，系统识别出这是 CiA 402 标准定义的 Statusword，直接显示了字段含义。

再看这条读 0x1017 的请求，系统识别出这是 CiA 301 通信配置区的 Producer Heartbeat Time，心跳时间参数。

这两个标准是 CANOpen 最常用的，系统都内置了对象字典。

[动作]
点击 CANOpen SDO 数据的放大镜按钮，展示解析结果，强调 CiA 402 识别

[台词]
再看 NMT 消息。COB-ID 是 0，系统识别为 NMT 命令，显示命令码 0x01 表示 Start Remote Node，目标节点是 1。

还有 Heartbeat 消息，系统解析出节点状态是 Operational。

这个弹窗也可以拖动，而且打开时会高亮显示正在解析的那条数据，方便对照。

[动作]
点击 NMT 和 Heartbeat 数据的放大镜按钮展示，演示拖动和高亮

[台词]
端口数据抓包和协议解析，是 DIVER 调试通信问题的利器。不需要接逻辑分析仪，不需要对照协议文档手动算，直接在界面上看。

### 6.6 示例六：遥控器面板

**时长**: 4 分钟

[台词]
第六个例子演示遥控器面板，这是调试移动机器人时非常实用的功能。

我们模拟一个简单的小车：用摇杆的 X 轴控制转向，Y 轴控制油门，输出左右轮的转速。

[动作]
创建文件 inputs/CarDemo.cs

```csharp
using CartActivator;

public class CarCart : CartDefinition
{
    [AsUpperIO] public int joystickX;   // 转向
    [AsUpperIO] public int joystickY;   // 油门
    
    [AsLowerIO] public int leftRPM;
    [AsLowerIO] public int rightRPM;
    [AsLowerIO] public int speed;
}

[LogicRunOnMCU(scanInterval = 50)]
public class CarDemo : LadderLogic<CarCart>
{
    public override void Operation(int iteration)
    {
        int throttle = cart.joystickY;
        int steering = cart.joystickX;
        
        int baseSpeed = throttle * 10;
        int diff = steering * 5;
        
        cart.leftRPM = baseSpeed - diff;
        cart.rightRPM = baseSpeed + diff;
        cart.speed = throttle;
    }
}
```

[台词]
这是一个简单的差速转向模型。油门控制基础速度，转向控制左右轮的速度差。向左转时左轮减速右轮加速，向右转则相反。

Build 并 Start。

现在点击右上角的控制器图标，打开 Control Panel。

这是一个可视化的遥控器配置界面。点击加号添加控件。

先添加一个 Joystick 摇杆控件。Variable X 绑定 joystickX，Variable Y 绑定 joystickY。范围设置为负 100 到正 100。Auto Return 打开，表示松开后自动回中。

再添加两个 Gauge 仪表盘控件。一个绑定 leftRPM，一个绑定 rightRPM。范围设置为负 1000 到正 1000。

现在拖动摇杆试试。向前推，两个仪表盘显示正转速，数值相同，表示直行。向左拨，左轮减速右轮加速，产生转向。

摇杆还可以绑定键盘。点击摇杆的设置按钮，在 Key Bindings 里设置 WASD 四个方向键。

现在按键盘 W 键，相当于向前推摇杆。按 A 键，向左转。按 S 键，后退。支持组合按键，比如 W 加 A，前进同时左转。

这样在没有手柄的情况下，也能方便地调试。

还有一个功能，在浏览器中打开 /control 这个地址，可以单独显示遥控器面板。这在多屏幕工作时很方便，一个屏幕显示主界面，另一个屏幕显示遥控器。

[动作]
Build 并运行，打开 Control Panel，添加 Joystick，添加 Gauge，拖动摇杆演示，设置键盘绑定，按键演示，新标签页打开 /control 演示独立遥控器

### 6.7 示例七：错误处理

**时长**: 3 分钟

[台词]
第七个例子演示错误处理。

在实际开发中，难免会遇到运行时错误，比如数组越界、除零、空指针等。传统 MCU 开发遇到这种错误，往往只能看到一个 HardFault，要对着寄存器地址和反汇编代码慢慢分析，非常痛苦。

DIVER 提供了完善的错误诊断机制。我们来演示一下。

[动作]
创建文件 inputs/ErrorDemo.cs

```csharp
using CartActivator;

[LogicRunOnMCU(scanInterval = 1000)]
public class ErrorDemo : LadderLogic<CartDefinition>
{
    private byte[] buffer = new byte[8];
    private int index = 0;
    
    public override void Operation(int iteration)
    {
        Console.WriteLine($"index = {index}");
        buffer[index] = (byte)iteration;
        index++;
        Console.WriteLine($"index = {index}");
    }
}
```

[台词]
这个 Logic 故意制造一个数组越界错误。buffer 数组长度是 8，但 index 每次加 1，迟早会超过 7。

Build 并 Start。

看 Terminal 输出，index 从 1 开始递增。当 index 到 8 的时候...

弹出了 Fatal Error 对话框。

看对话框内容。错误类型是 Array Index Out of Bounds，数组越界。

最重要的是这里，Source Location，显示了出错的 C# 源代码文件和行号：ErrorDemo.cs 第 26 行。

点击这个链接，编辑器自动打开文件，光标跳转到出错的那一行。就是 buffer[index] 这行。

这个功能的价值在于：你不需要看汇编，不需要查寄存器，直接就知道是哪行 C# 代码出了问题。

对于复杂的项目，这能节省大量的调试时间。

[动作]
Build 并运行，等待错误发生，展示错误弹窗，点击跳转到源码

---

## 第七章：总结与展望

**时长**: 1-2 分钟

[台词]
以上就是 DIVER 的核心功能介绍。

回顾一下 DIVER 的核心价值：

第一，变量表编程模式。把分布式的 IO 数据统一成一张变量表，开发者只需要关心变量读写，不需要关心底层通信。

第二，周期性操作循环。借鉴 PLC 的扫描周期概念，专注于控制逻辑本身，不用处理中断和线程。

第三，脊髓式分布式架构。上位机是大脑，MCU 是脊髓。一套代码，自动分发到所有节点执行。

第四，出色的调试体验。Variables 面板实时查看变量，Console 输出不用接串口，运行时错误直接定位源码，端口数据抓包监听通信数据，协议自动解析。

如果你正在开发 AGV、移动机器人、工业自动化设备，需要控制多个 MCU 节点，需要和各种传感器驱动器通信，欢迎试用 DIVER。

感谢观看。如有问题，请在评论区留言或联系我们。

[动作]
无

---

## 附录：演示前检查清单

- MCU 设备已连接并通电
- 串口或 CAN 线缆连接正常
- DIVER 后台服务已启动
- 浏览器已打开正确地址
- 示例代码文件已准备好
- 如需演示 Modbus 或 CANOpen，确保有从站设备或模拟器

## 附录：示例代码文件清单

| 文件名 | 用途 | 章节 |
|--------|------|------|
| HelloWorld.cs | Hello World (无 IO) | 6.1 |
| NumericDemo.cs | 数值计算 (有 IO) | 6.2 |
| DebugDemo.cs | 调试输出 (有 IO) | 6.3 |
| DigitalDemo.cs | 数字输出控制 | 6.4 |
| PortDemo.cs | 端口通信与数据抓包 | 6.5 |
| CarDemo.cs | 遥控器面板 | 6.6 |
| ErrorDemo.cs | 错误处理 | 6.7 |

## 附录：录制建议

- 分辨率建议 1920x1080
- 如果字体太小，可以用浏览器缩放 Ctrl 加加号
- 建议使用鼠标高亮工具，方便观众跟踪操作
- 语速保持中等，关键操作时可以稍慢
- 建议每个章节单独录制，方便后期剪辑
- 每次演示前确认设备状态正常
