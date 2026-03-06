// CartActivator stub — 客户逻辑代码的全部可用类型定义。
// 此文件由 DIVER 工程导出，仅用于 AI 辅助编码和 IDE 类型提示，不参与实际编译。
// 实际编译由 CoralinkerHost 内置的编译链完成，无需客户引用此文件。

using System;
using System.Collections.Generic;

namespace CartActivator
{
    /// <summary>CAN 帧结构</summary>
    public class CANMessage
    {
        /// <summary>标准帧 ID（0~0x7FF，11 位）</summary>
        public ushort ID;

        /// <summary>远程帧标志：false = 数据帧，true = 远程帧</summary>
        public bool RTR;

        /// <summary>数据负载（0~8 字节），长度即为 DLC</summary>
        public byte[] Payload;

        /// <summary>数据长度码（只读，由 Payload.Length 推导）</summary>
        public int DLC => Payload != null ? Payload.Length : 0;
    }

    /// <summary>
    /// 标注逻辑类运行在 MCU 上。
    /// scanInterval: 扫描周期（毫秒）。
    /// mcuUri: 设备标识（一般不填，由 Host 分配）。
    /// </summary>
    public class LogicRunOnMCUAttribute : Attribute
    {
        public string mcuUri = "default";
        public int scanInterval = 1000;
    }

    /// <summary>标注字段方向：Host -> MCU（可控量）</summary>
    public class AsUpperIO : Attribute { }

    /// <summary>标注字段方向：MCU -> Host（上报量）</summary>
    public class AsLowerIO : Attribute { }

    /// <summary>
    /// 逻辑基类。泛型参数 T 必须继承 CartDefinition。
    /// cart: 变量表实例，通过它读写 UpperIO / LowerIO 字段。
    /// Operation: 每个扫描周期调用一次。iteration 在 Host-MCU 通信正常时每周期 +1，丢包时不增加。
    /// </summary>
    public abstract class LadderLogic<T> where T : CartDefinition
    {
        public T cart;
        public abstract void Operation(int iteration);
    }

    /// <summary>
    /// 变量表基类。字段必须为 public，标注 [AsUpperIO] 或 [AsLowerIO]。
    /// 支持的字段类型：bool, byte, sbyte, char, short, ushort, int, uint, float, string, 及这些基础类型的一维数组。
    /// 不支持：long, double, decimal, 自定义 class/struct, List, Dictionary, 多维数组。
    /// 所有 CartDefinition 中同名字段在运行时共享同一个变量。
    /// </summary>
    public abstract class CartDefinition { }

    /// <summary>MCU 通信 API。所有方法在 Operation() 内调用。</summary>
    public class RunOnMCU
    {
        /// <summary>读事件（底层接口）。返回 null 表示无数据。</summary>
        public static byte[] ReadEvent(int port, int event_id) => default;

        /// <summary>写事件（底层接口）。</summary>
        public static void WriteEvent(byte[] payload, int port, int event_id) { }

        /// <summary>
        /// 读取 CAN 消息。返回 null 表示该 canId 没有新数据。
        /// port: 端口索引（由节点 Layout 决定）。
        /// canId: CAN Standard ID（0~0x7FF）。
        /// </summary>
        public static CANMessage ReadCANMessage(int port, int canId) => default;

        /// <summary>
        /// 发送 CAN 消息。Payload 不超过 8 字节。
        /// port: 端口索引（由节点 Layout 决定）。
        /// </summary>
        public static void WriteCANMessage(int port, CANMessage message) { }

        /// <summary>读取串口数据。返回 null 表示无数据。</summary>
        public static byte[] ReadStream(int port) => default;

        /// <summary>发送串口数据。</summary>
        public static void WriteStream(byte[] payload, int port) { }

        /// <summary>读数字输入。固定返回 4 字节（32 位），bit0~bit31 对应 DI0~DI31，实际路数由硬件决定。</summary>
        public static byte[] ReadSnapshot() => default;

        /// <summary>写数字输出。固定传入 4 字节（32 位），bit0~bit31 对应 DO0~DO31，实际路数由硬件决定。</summary>
        public static void WriteSnapshot(byte[] payload) { }

        /// <summary>MCU 上电后经过的微秒数。</summary>
        public static int GetMicrosFromStart() => default;

        /// <summary>MCU 上电后经过的毫秒数。</summary>
        public static int GetMillisFromStart() => default;

        /// <summary>MCU 上电后经过的秒数。</summary>
        public static int GetSecondsFromStart() => default;
    }
}
