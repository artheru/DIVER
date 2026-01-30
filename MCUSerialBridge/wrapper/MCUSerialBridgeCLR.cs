using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCUSerialBridgeCLR
{
    /// <summary>
    /// 辅助方法与内部 C 结构体封装，用于 P/Invoke 交互
    /// </summary>
    internal static class PortStructHelper
    {
        /// <summary>
        /// 将任意结构体序列化为字节数组
        /// </summary>
        /// <typeparam name="T">结构体类型</typeparam>
        /// <param name="str">要序列化的结构体</param>
        /// <returns>返回结构体对应的字节数组</returns>
        /// <remarks>使用 Marshal 分配内存并复制内容</remarks>
        public static byte[] StructToBytes<T>(T str)
            where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(str, ptr, false);
                Marshal.Copy(ptr, arr, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return arr;
        }

        /// <summary>
        /// 串口端口配置的原生结构体（与 MCU C 层对应）
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct SerialPortConfigC
        {
            /// <summary>端口类型（0x01 = Serial）</summary>
            public byte port_type;

            /// <summary>波特率</summary>
            public uint baud;

            /// <summary>接收帧时间间隔</summary>
            public uint receive_frame_ms;

            /// <summary>保留字节，填 0</summary>
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
            public byte[] reserved;
        }

        /// <summary>
        /// CAN 端口配置的原生结构体（与 MCU C 层对应）
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct CANPortConfigC
        {
            /// <summary>端口类型（0x02 = CAN）</summary>
            public byte port_type;

            /// <summary>波特率</summary>
            public uint baud;

            /// <summary>最大重发时间</summary>
            public uint retry_time_ms;

            /// <summary>保留字节，填 0</summary>
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
            public byte[] reserved;
        }
    }

    /// <summary>
    /// MCU 固件版本信息结构体
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct VersionInfo
    {
        /// <summary>产品型号</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string ProductionName;

        /// <summary>Git 标签</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
        public string GitTag;

        /// <summary>Git commit 哈希值</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
        public string GitCommit;

        /// <summary>编译时间（字符串）</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 24)]
        public string BuildTime;

        /// <summary>
        /// 转换为可读字符串
        /// </summary>
        /// <returns>返回包含产品、Tag、Commit、BuildTime 的字符串</returns>
        public override string ToString()
        {
            return $"Product: {ProductionName}, Tag: {GitTag}, Commit: {GitCommit}, Built: {BuildTime}";
        }
    }

    /// <summary>
    /// MCU 运行模式枚举
    /// </summary>
    public enum MCUMode : byte
    {
        /// <summary>Bridge（透传/桥接）模式</summary>
        Bridge = 0x00,

        /// <summary>DIVER（应用/驱动）模式</summary>
        DIVER = 0x80,
    }

    /// <summary>
    /// MCU 运行状态枚举
    /// </summary>
    public enum MCURunState : byte
    {
        /// <summary>空闲状态</summary>
        Idle = 0x00,

        /// <summary>运行中</summary>
        Running = 0x0F,

        /// <summary>错误状态</summary>
        Error = 0xFF,
    }

    /// <summary>
    /// MCU 当前运行状态
    /// 内存布局（小端序）：[0] running_state | [1] is_configured | [2] is_programmed | [3] mode
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct MCUState
    {
        /// <summary>原始 32 位状态值</summary>
        [FieldOffset(0)]
        public uint RawValue;

        /// <summary>运行状态 (MCURunState)</summary>
        [FieldOffset(0)]
        public MCURunState RunningState;

        /// <summary>是否已配置端口 (0 或 1)</summary>
        [FieldOffset(1)]
        public byte IsConfigured;

        /// <summary>是否已加载程序 (0 或 1, 仅 DIVER 模式)</summary>
        [FieldOffset(2)]
        public byte IsProgrammed;

        /// <summary>模式 (MCUMode: 0x00=Bridge, 0x80=DIVER)</summary>
        [FieldOffset(3)]
        public MCUMode Mode;

        /// <summary>是否处于 Bridge 模式</summary>
        public bool IsBridge => Mode == MCUMode.Bridge;

        /// <summary>是否处于 DIVER 模式</summary>
        public bool IsDIVER => Mode == MCUMode.DIVER;

        /// <summary>是否正在运行</summary>
        public bool IsRunning => RunningState == MCURunState.Running;

        /// <summary>是否处于错误状态</summary>
        public bool IsError => RunningState == MCURunState.Error;

        /// <summary>
        /// 返回可读的状态字符串
        /// </summary>
        /// <returns>例如 "Bridge: Running, Configured" 或 "DIVER: Idle, Programmed"</returns>
        public override string ToString()
        {
            string modeStr = IsBridge ? "Bridge" : "DIVER";
            string runStr = RunningState switch
            {
                MCURunState.Idle => "Idle",
                MCURunState.Running => "Running",
                MCURunState.Error => "Error",
                _ => $"Unknown(0x{(byte)RunningState:X2})",
            };

            var flags = new System.Collections.Generic.List<string>();
            if (IsConfigured != 0)
                flags.Add("Configured");
            if (IsProgrammed != 0)
                flags.Add("Programmed");
            string flagsStr = flags.Count > 0 ? string.Join(", ", flags) : "NotConfigured";

            return $"{modeStr}: {runStr}, {flagsStr}";
        }
    }

    /// <summary>
    /// 端口类型枚举
    /// </summary>
    public enum PortType : byte
    {
        /// <summary>串口</summary>
        Serial = 0x01,
        /// <summary>CAN 总线</summary>
        CAN = 0x02,
        /// <summary>LED / IO 类端口</summary>
        LED = 0x03,
    }

    /// <summary>
    /// 端口描述符结构体 (16 bytes)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Size = 16)]
    public struct PortDescriptor
    {
        /// <summary>端口类型</summary>
        public PortType Type;

        /// <summary>端口名称（最多15字符，null-terminated）</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
        public string Name;

        /// <summary>
        /// 转换为可读字符串
        /// </summary>
        public override string ToString() => $"{Type}: {Name}";
    }

    /// <summary>
    /// 单个端口的统计数据结构体
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PortStats
    {
        /// <summary>发送帧数</summary>
        public uint TxFrames;

        /// <summary>接收帧数</summary>
        public uint RxFrames;

        /// <summary>发送字节数</summary>
        public uint TxBytes;

        /// <summary>接收字节数</summary>
        public uint RxBytes;

        /// <summary>
        /// 转换为可读字符串
        /// </summary>
        public override readonly string ToString() =>
            $"TX: {TxFrames} frames/{TxBytes} bytes, RX: {RxFrames} frames/{RxBytes} bytes";
    }

    /// <summary>
    /// MCU 运行时统计数据结构体
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RuntimeStats
    {
        /// <summary>MCU 运行时间（毫秒）</summary>
        public uint UptimeMs;

        /// <summary>数字输入状态（位图）</summary>
        public uint DigitalInputs;

        /// <summary>数字输出状态（位图）</summary>
        public uint DigitalOutputs;

        /// <summary>有效端口数量</summary>
        public byte PortCount;

        /// <summary>保留字节（对齐）</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] Reserved;

        /// <summary>各端口统计数据（最多16个）</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public PortStats[] Ports;

        /// <summary>
        /// 获取有效的端口统计列表
        /// </summary>
        public readonly PortStats[] GetValidPorts()
        {
            if (Ports == null || PortCount <= 0) return Array.Empty<PortStats>();
            return Ports.Take(Math.Min(PortCount, Ports.Length)).ToArray();
        }

        /// <summary>
        /// 获取运行时间（TimeSpan 格式）
        /// </summary>
        public readonly TimeSpan Uptime => TimeSpan.FromMilliseconds(UptimeMs);

        /// <summary>
        /// 转换为可读字符串
        /// </summary>
        public override readonly string ToString()
        {
            var uptime = TimeSpan.FromMilliseconds(UptimeMs);
            return $"Uptime={uptime}, DI=0x{DigitalInputs:X8}, DO=0x{DigitalOutputs:X8}, Ports={PortCount}";
        }
    }

    /// <summary>
    /// Core Dump 数据布局类型
    /// </summary>
    public enum CoreDumpLayout : uint
    {
        /// <summary>后续是字符串，按字符串解析</summary>
        String = 0,
        /// <summary>STM32F4 Core Dump 变量</summary>
        STM32F4 = 4,
    }

    /// <summary>
    /// DIVER 运行时调试信息 (64 bytes)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DIVERDebugInfo
    {
        /// <summary>当前 IL 指令偏移</summary>
        public int ILOffset;

        /// <summary>C 代码行号 (来自 __LINE__)</summary>
        public int LineNo;

        /// <summary>保留字段</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 14)]
        public uint[] Reserved;
    }

    /// <summary>
    /// STM32F4 Core Dump 变量 (68 bytes, 17 个寄存器)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CoreDumpVariablesF4
    {
        public uint R0;
        public uint R1;
        public uint R2;
        public uint R3;
        public uint R12;
        /// <summary>链接寄存器</summary>
        public uint LR;
        /// <summary>程序计数器</summary>
        public uint PC;
        /// <summary>程序状态寄存器</summary>
        public uint PSR;
        /// <summary>主状态寄存器</summary>
        public uint MSR;
        /// <summary>可配置故障状态寄存器</summary>
        public uint CFSR;
        /// <summary>硬故障状态寄存器</summary>
        public uint HFSR;
        /// <summary>调试故障状态寄存器</summary>
        public uint DFSR;
        /// <summary>辅助故障状态寄存器</summary>
        public uint AFSR;
        /// <summary>总线故障地址寄存器</summary>
        public uint BFAR;
        /// <summary>内存管理故障地址寄存器</summary>
        public uint MMAR;
        /// <summary>主堆栈指针</summary>
        public uint MSP;
        /// <summary>堆栈结束地址</summary>
        public uint StackEnd;
    }

    /// <summary>
    /// Core Dump 数据联合体 (128 bytes)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    public struct CoreDumpData
    {
        /// <summary>原始字节数据</summary>
        [FieldOffset(0)]
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] Raw;
    }

    /// <summary>
    /// 错误上报 Payload 结构 (256 bytes)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ErrorPayload
    {
        /// <summary>Payload 版本号 (当前 = 1)</summary>
        public uint PayloadVersion;

        /// <summary>固件版本信息 (56 bytes)</summary>
        public VersionInfo Version;

        /// <summary>DIVER 调试信息 (64 bytes)</summary>
        public DIVERDebugInfo DebugInfo;

        /// <summary>Core Dump 布局类型</summary>
        public uint CoreDumpLayoutValue;

        /// <summary>Core Dump 数据 (128 bytes)</summary>
        public CoreDumpData CoreDump;

        /// <summary>获取 Core Dump 布局类型枚举</summary>
        public readonly CoreDumpLayout Layout => (CoreDumpLayout)CoreDumpLayoutValue;

        /// <summary>
        /// 获取错误字符串（当 Layout == String 时）
        /// </summary>
        public readonly string GetErrorString()
        {
            if (Layout != CoreDumpLayout.String || CoreDump.Raw == null)
                return string.Empty;

            int len = Array.IndexOf(CoreDump.Raw, (byte)0);
            if (len < 0) len = CoreDump.Raw.Length;
            return System.Text.Encoding.UTF8.GetString(CoreDump.Raw, 0, len);
        }

        /// <summary>
        /// 获取 STM32F4 Core Dump 数据（当 Layout == STM32F4 时）
        /// </summary>
        public readonly CoreDumpVariablesF4? GetF4CoreDump()
        {
            if (Layout != CoreDumpLayout.STM32F4 || CoreDump.Raw == null)
                return null;

            IntPtr ptr = Marshal.AllocHGlobal(68);
            try
            {
                Marshal.Copy(CoreDump.Raw, 0, ptr, 68);
                return Marshal.PtrToStructure<CoreDumpVariablesF4>(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>
        /// 转换为 JSON 对象
        /// </summary>
        public readonly JObject ToJson()
        {
            var json = new JObject
            {
                ["payloadVersion"] = PayloadVersion,
                ["version"] = new JObject
                {
                    ["productionName"] = Version.ProductionName?.Trim('\0') ?? "",
                    ["gitTag"] = Version.GitTag?.Trim('\0') ?? "",
                    ["gitCommit"] = Version.GitCommit?.Trim('\0') ?? "",
                    ["buildTime"] = Version.BuildTime?.Trim('\0') ?? ""
                },
                ["debugInfo"] = new JObject
                {
                    ["ilOffset"] = DebugInfo.ILOffset,
                    ["lineNo"] = DebugInfo.LineNo
                },
                ["coreDumpLayout"] = Layout.ToString()
            };

            if (Layout == CoreDumpLayout.String)
            {
                json["errorString"] = GetErrorString();
            }
            else if (Layout == CoreDumpLayout.STM32F4)
            {
                var f4 = GetF4CoreDump();
                if (f4.HasValue)
                {
                    var v = f4.Value;
                    json["coreDump"] = new JObject
                    {
                        ["R0"] = $"0x{v.R0:X8}",
                        ["R1"] = $"0x{v.R1:X8}",
                        ["R2"] = $"0x{v.R2:X8}",
                        ["R3"] = $"0x{v.R3:X8}",
                        ["R12"] = $"0x{v.R12:X8}",
                        ["LR"] = $"0x{v.LR:X8}",
                        ["PC"] = $"0x{v.PC:X8}",
                        ["PSR"] = $"0x{v.PSR:X8}",
                        ["MSR"] = $"0x{v.MSR:X8}",
                        ["CFSR"] = $"0x{v.CFSR:X8}",
                        ["HFSR"] = $"0x{v.HFSR:X8}",
                        ["DFSR"] = $"0x{v.DFSR:X8}",
                        ["AFSR"] = $"0x{v.AFSR:X8}",
                        ["BFAR"] = $"0x{v.BFAR:X8}",
                        ["MMAR"] = $"0x{v.MMAR:X8}",
                        ["MSP"] = $"0x{v.MSP:X8}",
                        ["StackEnd"] = $"0x{v.StackEnd:X8}"
                    };
                }
            }

            return json;
        }

        /// <summary>
        /// 转换为可读字符串
        /// </summary>
        public override readonly string ToString()
        {
            return $"ErrorPayload(v{PayloadVersion}): IL={DebugInfo.ILOffset}, Line={DebugInfo.LineNo}, Layout={Layout}";
        }
    }

    /// <summary>
    /// MCU 硬件布局信息结构体
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LayoutInfo
    {
        /// <summary>数字输入数量</summary>
        public sbyte DigitalInputCount;

        /// <summary>数字输出数量</summary>
        public sbyte DigitalOutputCount;

        /// <summary>端口数量</summary>
        public sbyte PortCount;

        /// <summary>保留字节（对齐）</summary>
        public byte Reserved;

        /// <summary>端口描述数组（最多16个）</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public PortDescriptor[] Ports;

        /// <summary>
        /// 获取有效的端口描述列表
        /// </summary>
        public PortDescriptor[] GetValidPorts()
        {
            if (Ports == null || PortCount <= 0) return Array.Empty<PortDescriptor>();
            return Ports.Take(Math.Min(PortCount, Ports.Length)).ToArray();
        }

        /// <summary>
        /// 转换为可读字符串
        /// </summary>
        public override string ToString()
        {
            var ports = GetValidPorts();
            var portStr = string.Join(", ", ports.Select(p => p.ToString()));
            return $"DI={DigitalInputCount}, DO={DigitalOutputCount}, Ports=[{portStr}]";
        }
    }

    /// <summary>
    /// 抽象端口配置基类
    /// </summary>
    public abstract class PortConfig
    {
        /// <summary>端口类型（由子类实现）</summary>
        public abstract byte PortType { get; }

        /// <summary>序列化端口配置为字节数组（供 P/Invoke 使用）</summary>
        /// <returns>返回固定长度字节数组（16 bytes）</returns>
        public abstract byte[] ToBytes();

        /// <summary>
        /// 从 JSON 对象反序列化 PortConfig
        /// JSON 格式: { "type": "serial", "baud": 9600, "receiveFrameMs": 0 }
        ///        或: { "type": "can", "baud": 500000, "retryTimeMs": 10 }
        /// </summary>
        public static PortConfig FromJson(Newtonsoft.Json.Linq.JObject json)
        {
            var type = json["type"]?.ToString()?.ToLower() ?? "serial";
            var baud = (uint)(json["baud"]?.ToObject<uint>() ?? 9600);
            
            return type switch
            {
                "serial" => new SerialPortConfig(baud, (uint)(json["receiveFrameMs"]?.ToObject<uint>() ?? 0)),
                "can" => new CANPortConfig(baud, (uint)(json["retryTimeMs"]?.ToObject<uint>() ?? 10)),
                _ => throw new ArgumentException($"Unknown port type: {type}")
            };
        }

        /// <summary>
        /// 从 JSON 数组反序列化 PortConfig 数组
        /// </summary>
        public static PortConfig[] FromJsonArray(Newtonsoft.Json.Linq.JArray jsonArray)
        {
            return jsonArray.Select(j => FromJson((Newtonsoft.Json.Linq.JObject)j)).ToArray();
        }

        /// <summary>
        /// 序列化为 JSON 对象
        /// </summary>
        public abstract Newtonsoft.Json.Linq.JObject ToJson();
    }

    /// <summary>
    /// 串口配置
    /// </summary>
    /// <remarks>
    /// 构造函数
    /// </remarks>
    /// <param name="baud">波特率</param>
    /// <param name="receiveFrameMs">接收帧间隔</param>
    public class SerialPortConfig(uint baud, uint receiveFrameMs) : PortConfig
    {
        /// <summary>Serial 类型</summary>
        public override byte PortType => 0x01;

        /// <summary>波特率</summary>
        public uint Baud { get; set; } = baud;

        /// <summary>接收帧间隔</summary>
        public uint ReceiveFrameMs { get; set; } = receiveFrameMs;

        /// <summary>
        /// 转换为字节数组
        /// </summary>
        /// <returns>16 字节数组</returns>
        public override byte[] ToBytes()
        {
            var c = new PortStructHelper.SerialPortConfigC
            {
                port_type = PortType,
                baud = Baud,
                receive_frame_ms = ReceiveFrameMs,
                reserved = new byte[7],
            };
            return PortStructHelper.StructToBytes(c);
        }

        /// <summary>序列化为 JSON</summary>
        public override Newtonsoft.Json.Linq.JObject ToJson() => new()
        {
            ["type"] = "serial",
            ["baud"] = Baud,
            ["receiveFrameMs"] = ReceiveFrameMs
        };
    }

    /// <summary>
    /// CAN 端口配置
    /// </summary>
    /// <remarks>
    /// 构造函数
    /// </remarks>
    /// <param name="baud">波特率</param>
    /// <param name="retryTimeMs">重发间隔</param>
    public class CANPortConfig(uint baud, uint retryTimeMs) : PortConfig
    {
        /// <summary>CAN 类型</summary>
        public override byte PortType => 0x02;

        /// <summary>波特率</summary>
        public uint Baud { get; set; } = baud;

        /// <summary>重发间隔</summary>
        public uint RetryTimeMs { get; set; } = retryTimeMs;

        /// <summary>
        /// 转换为字节数组
        /// </summary>
        /// <returns>16 字节数组</returns>
        public override byte[] ToBytes()
        {
            var c = new PortStructHelper.CANPortConfigC
            {
                port_type = PortType,
                baud = Baud,
                retry_time_ms = RetryTimeMs,
                reserved = new byte[7],
            };
            return PortStructHelper.StructToBytes(c);
        }

        /// <summary>序列化为 JSON</summary>
        public override Newtonsoft.Json.Linq.JObject ToJson() => new()
        {
            ["type"] = "can",
            ["baud"] = Baud,
            ["retryTimeMs"] = RetryTimeMs
        };
    }

    /// <summary>
    /// CAN 帧结构（标准帧 11-bit ID + 1-bit RTR + Payload）
    /// DLC 由 Payload.Length 自动推导
    /// </summary>
    public class CANMessage
    {
        /// <summary>标准帧 ID（0~0x7FF，11 位）</summary>
        public ushort ID { get; set; }

        /// <summary>远程帧标志：false = 数据帧，true = 远程帧</summary>
        public bool RTR { get; set; }

        /// <summary>数据负载（0~8 字节），长度即为 DLC</summary>
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        /// <summary>数据长度码（只读，由 Payload.Length 推导）</summary>
        public int DLC => Payload?.Length ?? 0;

        /// <summary>
        /// 序列化为 MCU 协议字节流
        /// </summary>
        /// <returns>返回字节数组：2 bytes header + Payload，失败返回 null</returns>
        public byte[] ToBytes()
        {
            int dlc = Payload?.Length ?? 0;
            if (dlc > 8)
                return null; // Payload 超长，返回 null

            // 构造 2 字节 header
            ushort header = 0;
            header |= (ushort)(ID & 0x7FF); // bits 0-10
            if (RTR)
                header |= (1 << 11); // bit 11
            header |= (ushort)((dlc & 0xF) << 12); // bits 12-15

            byte[] result = new byte[2 + dlc];
            byte[] headerBytes = BitConverter.GetBytes(header); // 小端序
            result[0] = headerBytes[0];
            result[1] = headerBytes[1];

            if (dlc > 0)
                Buffer.BlockCopy(Payload!, 0, result, 2, dlc);
            return result;
        }

        /// <summary>
        /// 反序列化 MCU 协议字节流为 CANMessage
        /// </summary>
        /// <param name="data">原始字节数组</param>
        /// <param name="length">实际有效长度</param>
        /// <returns>CANMessage 实例，失败返回 null</returns>
        public static CANMessage FromBytes(byte[] data, uint length)
        {
            if (data == null || length > data.Length || length < 2)
                return null;

            ushort header = BitConverter.ToUInt16(data, 0);

            ushort id = (ushort)(header & 0x7FF);
            bool rtr = (header & (1 << 11)) != 0;
            int dlc = (header >> 12) & 0xF;

            byte[] payload;
            if (dlc > 0)
            {
                if (length < 2 + dlc)
                    return null; // 数据长度不足
                payload = new byte[dlc];
                Buffer.BlockCopy(data, 2, payload, 0, dlc);
            }
            else
            {
                payload = Array.Empty<byte>();
            }

            return new CANMessage { ID = id, RTR = rtr, Payload = payload };
        }

        public override string ToString()
        {
            string payloadStr =
                (Payload == null || Payload.Length == 0)
                    ? "[]"
                    : "0x[" + string.Join(" ", Payload.Select(b => $"{b:X2}")) + "]";

            return $"CANMessage(ID=0x{ID:X3}, RTR={RTR}, DLC={DLC}, Payload={payloadStr})";
        }
    }

    /// <summary>
    /// 内部 P/Invoke 层，直接映射 C DLL 函数
    /// </summary>
    internal static class MCUSerialBridgeCoreAPI
    {
        private const string DLL = @"mcu_serial_bridge.dll";

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_open(
            out IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string port,
            uint baud
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_close(IntPtr handle);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_reset(IntPtr handle, uint timeout_ms);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_upgrade(IntPtr handle, uint timeout_ms);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern MCUSerialBridgeError msb_version(
            IntPtr handle,
            out VersionInfo version,
            uint timeout_ms
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern MCUSerialBridgeError mcu_get_layout(
            IntPtr handle,
            out LayoutInfo layout,
            uint timeout_ms
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern MCUSerialBridgeError mcu_state(
            IntPtr handle,
            out MCUState state,
            uint timeout
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_configure(
            IntPtr handle,
            uint num_ports,
            IntPtr ports,
            uint timeout
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_read_input(
            IntPtr handle,
            [Out] byte[] inputs,
            uint timeout_ms
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_write_output(
            IntPtr handle,
            [In] byte[] outputs,
            uint timeout_ms
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_read_port(
            IntPtr handle,
            byte port_index,
            [Out] byte[] dst_data,
            uint dst_capacity,
            out uint out_length,
            uint timeout_ms
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void msb_on_port_data_callback_function_t(
            IntPtr dst_data,
            uint dst_data_size,
            IntPtr user_ctx
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_register_port_data_callback(
            IntPtr handle,
            byte port_index,
            msb_on_port_data_callback_function_t callback,
            IntPtr user_ctx
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_write_port(
            IntPtr handle,
            byte port_index,
            [In] byte[] src_data,
            uint src_data_len,
            uint timeout_ms
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_start(IntPtr handle, uint timeout_ms);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_program(
            IntPtr handle,
            [In] byte[] program_bytes,
            uint program_len,
            uint timeout_ms
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_enable_wire_tap(
            IntPtr handle,
            uint timeout_ms
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_memory_upper_io(
            IntPtr handle,
            [In] byte[] data,
            uint data_len,
            uint timeout_ms
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void msb_on_memory_lower_io_callback_function_t(
            IntPtr data,
            uint data_size,
            IntPtr user_ctx
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_register_memory_lower_io_callback(
            IntPtr handle,
            msb_on_memory_lower_io_callback_function_t callback,
            IntPtr user_ctx
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void msb_on_console_writeline_callback_function_t(
            IntPtr message,
            uint message_len,
            IntPtr user_ctx
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_register_console_writeline_callback(
            IntPtr handle,
            msb_on_console_writeline_callback_function_t callback,
            IntPtr user_ctx
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void msb_on_fatal_error_callback_function_t(
            IntPtr payload,
            IntPtr user_ctx
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_register_fatal_error_callback(
            IntPtr handle,
            msb_on_fatal_error_callback_function_t callback,
            IntPtr user_ctx
        );

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUSerialBridgeError msb_get_stats(
            IntPtr handle,
            out RuntimeStats stats,
            uint timeout_ms
        );
    }

    /// <summary>
    /// MCU 串口/端口操作托管封装类
    /// 实现 IDisposable 管理底层句柄生命周期
    /// </summary>
    public class MCUSerialBridge : IDisposable
    {
        public static uint MaxPortNumber = 16;

        private IntPtr nativeHandle = IntPtr.Zero;

        /// <summary>判断是否已打开</summary>
        public bool IsOpen => nativeHandle != IntPtr.Zero;

        /// <summary>构造函数，初始化对象</summary>
        public MCUSerialBridge()
        {
            nativeHandle = IntPtr.Zero;
        }

        /// <summary>析构函数</summary>
        ~MCUSerialBridge()
        {
            Dispose(false);
        }

        /// <summary>显式释放资源</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>内部释放资源方法</summary>
        /// <param name="disposing">true 表示手动释放，false 表示析构释放</param>
        private void Dispose(bool disposing)
        {
            if (nativeHandle != IntPtr.Zero)
            {
                MCUSerialBridgeCoreAPI.msb_close(nativeHandle);
                nativeHandle = IntPtr.Zero;
            }
        }

        /// <summary>打开串口</summary>
        /// <param name="portName">串口名，如 "COM3"</param>
        /// <param name="baud">波特率</param>
        /// <returns>错误码</returns>
        public MCUSerialBridgeError Open(string portName, uint baud)
        {
            return MCUSerialBridgeCoreAPI.msb_open(out nativeHandle, portName, baud);
        }

        /// <summary>关闭串口</summary>
        /// <returns>错误码</returns>
        public MCUSerialBridgeError Close()
        {
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            MCUSerialBridgeError error = MCUSerialBridgeCoreAPI.msb_close(nativeHandle);
            nativeHandle = IntPtr.Zero;
            return error;
        }

        /// <summary>MCU 复位</summary>
        /// <returns>错误码</returns>
        public MCUSerialBridgeError Reset(uint timeout = 200)
        {
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            return MCUSerialBridgeCoreAPI.msb_reset(nativeHandle, timeout);
        }

        /// <summary>
        /// MCU 进入升级模式（Bootloader）
        /// 发送升级命令后，MCU 会在约 100~200ms 后重启进入 Bootloader 模式。
        /// 调用此方法后应关闭当前连接，等待 MCU 重启后使用 Bootloader 接口连接。
        /// </summary>
        /// <param name="timeout">超时时间（ms）</param>
        /// <returns>错误码</returns>
        public MCUSerialBridgeError Upgrade(uint timeout = 200)
        {
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            return MCUSerialBridgeCoreAPI.msb_upgrade(nativeHandle, timeout);
        }

        /// <summary>获取固件版本</summary>
        /// <param name="version">输出版本信息</param>
        /// <param name="timeout">超时时间（ms）</param>
        /// <returns>错误码</returns>
        public MCUSerialBridgeError GetVersion(out VersionInfo version, uint timeout = 200)
        {
            version = new VersionInfo();
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            return MCUSerialBridgeCoreAPI.msb_version(nativeHandle, out version, timeout);
        }

        /// <summary>获取 MCU 硬件布局信息</summary>
        /// <param name="layout">输出布局信息</param>
        /// <param name="timeout">超时时间（ms）</param>
        /// <returns>错误码</returns>
        public MCUSerialBridgeError GetLayout(out LayoutInfo layout, uint timeout = 200)
        {
            layout = new LayoutInfo();
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            return MCUSerialBridgeCoreAPI.mcu_get_layout(nativeHandle, out layout, timeout);
        }

        /// <summary>获取 MCU 当前状态</summary>
        /// <param name="state">输出状态</param>
        /// <param name="timeout">超时时间（ms）</param>
        /// <returns>错误码</returns>
        public MCUSerialBridgeError GetState(out MCUState state, uint timeout = 200)
        {
            state = new MCUState();
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            return MCUSerialBridgeCoreAPI.mcu_state(nativeHandle, out state, timeout);
        }

        /// <summary>配置端口</summary>
        /// <param name="ports">端口集合</param>
        /// <param name="timeout">超时时间（ms）</param>
        /// <returns>错误码</returns>
        public MCUSerialBridgeError Configure(IEnumerable<PortConfig> ports, uint timeout = 200)
        {
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            if (ports == null)
                return MCUSerialBridgeError.Win_InvalidParam;

            // 转换成数组
            PortConfig[] portArray = ports as PortConfig[] ?? ports.ToArray();
            int count = portArray.Length;

            // 分配连续原生内存
            int structSize = 16; // 每个 PortConfig 固定 16 字节
            IntPtr nativePorts = Marshal.AllocHGlobal(structSize * count);

            try
            {
                for (int i = 0; i < count; i++)
                {
                    byte[] bytes = portArray[i].ToBytes();
                    if (bytes.Length != structSize)
                        return MCUSerialBridgeError.Win_InvalidParam;

                    Marshal.Copy(bytes, 0, nativePorts + i * structSize, structSize);
                }

                // 调用底层 API
                return MCUSerialBridgeCoreAPI.msb_configure(
                    nativeHandle,
                    (uint)count,
                    nativePorts,
                    timeout
                );
            }
            catch
            {
                return MCUSerialBridgeError.Win_InvalidParam;
            }
            finally
            {
                Marshal.FreeHGlobal(nativePorts);
            }
        }

        /// <summary>读取输入（4 字节）</summary>
        /// <param name="inputs">输出数组</param>
        /// <param name="timeout">超时（ms）</param>
        /// <returns>错误码</returns>
        public MCUSerialBridgeError ReadInput(out byte[] inputs, uint timeout = 100)
        {
            inputs = new byte[4];
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            return MCUSerialBridgeCoreAPI.msb_read_input(nativeHandle, inputs, timeout);
        }

        /// <summary>写输出（4 字节）</summary>
        /// <param name="outputs">数据数组</param>
        /// <param name="timeout">超时（ms）</param>
        /// <returns>错误码</returns>
        public MCUSerialBridgeError WriteOutput(byte[] outputs, uint timeout = 100)
        {
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            return MCUSerialBridgeCoreAPI.msb_write_output(nativeHandle, outputs, timeout);
        }

        /// <summary>
        /// 读取 Serial 端口的一帧数据。
        /// Serial 上报的数据按帧入队；本接口每次调用最多读取一帧。
        /// 注意，如果不及时调用该接口，数据可能会丢失。
        /// </summary>
        /// <param name="portIndex">Serial 端口索引</param>
        /// <param name="buffer">接收到的数据</param>
        /// <param name="timeout">
        /// 超时时间（毫秒）
        ///  - 0：不等待，有数据立即返回，没有数据立即返回 MSB_Error_NoData
        ///  - >0：若当前无数据，最多等待 timeout，期间有新帧到达则立即返回
        /// </param>
        /// <remarks>
        /// 如果已经注册回调，本函数将始终返回 MSB_Error_NoData
        /// </remarks>
        /// <returns>
        /// 错误码 MCUSerialBridgeError
        ///  - OK               成功读取一帧
        ///  - NoData           当前无可读数据（仅在 timeout == 0 或等待超时）
        ///  - Win_InvalidParam 参数错误
        /// </returns>
        public MCUSerialBridgeError ReadSerial(byte portIndex, out byte[] buffer, uint timeout)
        {
            buffer = Array.Empty<byte>();

            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            const int MAX_PORT_FRAME = 2048;
            byte[] tmp = new byte[MAX_PORT_FRAME];

            var err = MCUSerialBridgeCoreAPI.msb_read_port(
                nativeHandle,
                portIndex,
                tmp,
                (uint)tmp.Length,
                out uint outLen,
                timeout
            );

            if (err != MCUSerialBridgeError.OK)
                return err;

            buffer = new byte[outLen];
            Buffer.BlockCopy(tmp, 0, buffer, 0, (int)outLen);

            return MCUSerialBridgeError.OK;
        }

        /// <summary>
        /// 写 Serial 端口数据。
        /// 注意：对于单个Serial端口，不支持多线程并行发送，不要在上一条数据没有发送完成之前调用该函数，否则有可能导致数据错误。
        /// 注意：超时时间一定要大于波特率和数据长度综合得出的帧时间
        /// </summary>
        /// <param name="portIndex">Serial 端口索引</param>
        /// <param name="data">待发送数据</param>
        /// <param name="timeout">超时时间（毫秒）</param>
        /// <returns>
        /// 错误码 MCUSerialBridgeError
        ///  - OK                   成功发送
        ///  - 其他错误请查看 MCUSerialBridgeError
        /// </returns>
        public MCUSerialBridgeError WriteSerial(byte portIndex, byte[] data, uint timeout)
        {
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            if (data == null || data.Length == 0)
                return MCUSerialBridgeError.Win_InvalidParam;

            return MCUSerialBridgeCoreAPI.msb_write_port(
                nativeHandle,
                portIndex,
                data,
                (uint)data.Length,
                timeout
            );
        }

        /// <summary>
        /// 读取 CAN 端口的一帧数据。
        /// CAN 上报的数据按帧入队；本接口每次调用最多读取一帧。
        /// 注意，如果不及时调用该接口，数据可能会丢失。
        /// </summary>
        /// <param name="portIndex">CAN 端口索引</param>
        /// <param name="message">输出 CAN 消息对象</param>
        /// <param name="timeout">超时时间（毫秒）</param>
        /// <remarks>
        /// 如果已经注册回调，本函数将始终返回 MSB_Error_NoData
        /// </remarks>
        /// <returns>
        /// 错误码 MCUSerialBridgeError
        ///  - OK                   成功读取一帧
        ///  - NoData               当前无可读数据（仅在 timeout == 0 或等待超时）
        ///  - Win_InvalidParam     参数错误
        ///  - CAN_DataError        CAN数据错误
        ///  - Win_HandleNotFound   句柄无效
        /// </returns>
        public MCUSerialBridgeError ReadCAN(byte portIndex, out CANMessage message, uint timeout)
        {
            message = null;

            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            const int MAX_FRAME = 16;
            byte[] tmp = new byte[MAX_FRAME];

            var err = MCUSerialBridgeCoreAPI.msb_read_port(
                nativeHandle,
                portIndex,
                tmp,
                (uint)tmp.Length,
                out uint outLen,
                timeout
            );
            if (err != MCUSerialBridgeError.OK)
                return err;

            try
            {
                message = CANMessage.FromBytes(tmp, outLen);
            }
            catch
            {
                return MCUSerialBridgeError.CAN_DataError;
            }
            return MCUSerialBridgeError.OK;
        }

        /// <summary>
        /// 写 CAN 端口数据。
        /// 注意：CAN 支持多线程发送，最多可同时发送 16 个消息。
        /// 但是，多个CAN消息会进入排队队列等待发送，如果消息太多，可能引起超时。
        /// </summary>
        /// <param name="portIndex">CAN 端口索引</param>
        /// <param name="message">待发送 CAN 消息对象</param>
        /// <param name="timeout">超时时间（毫秒），默认 500ms</param>
        /// <returns>
        /// 错误码 MCUSerialBridgeError
        ///  - OK                   成功发送
        ///  - Win_InvalidParam     参数错误
        ///  - CAN_DataError        CAN 数据错误
        ///  - Win_HandleNotFound   句柄无效
        /// </returns>
        public MCUSerialBridgeError WriteCAN(byte portIndex, CANMessage message, uint timeout)
        {
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;
            if (message == null)
                return MCUSerialBridgeError.Win_InvalidParam;
            try
            {
                byte[] buffer = message.ToBytes();
                return MCUSerialBridgeCoreAPI.msb_write_port(
                    nativeHandle,
                    portIndex,
                    buffer,
                    (uint)buffer.Length,
                    timeout
                );
            }
            catch
            {
                return MCUSerialBridgeError.CAN_DataError;
            }
        }

        private readonly Dictionary<
            byte,
            MCUSerialBridgeCoreAPI.msb_on_port_data_callback_function_t
        > _portCallbacks = [];

        private MCUSerialBridgeCoreAPI.msb_on_memory_lower_io_callback_function_t _memoryLowerIOCallback;
        private MCUSerialBridgeCoreAPI.msb_on_console_writeline_callback_function_t _consoleWriteLineCallback;
        private MCUSerialBridgeCoreAPI.msb_on_fatal_error_callback_function_t _fatalErrorCallback;

        /// <summary>
        /// 注册指定端口(Serial)的回调函数
        /// </summary>
        /// <param name="portIndex">端口索引</param>
        /// <param name="callback">接收数据回调，byte[] 为接收到的原始数据</param>
        /// <returns>错误码</returns>
        /// <remarks>
        /// 注意事项：
        /// 1. 回调会在底层 C 层线程中直接调用，请**不要在回调内阻塞**，例如等待 I/O 或 Sleep。
        /// 2. 回调内**不能调用 WriteSerial/WriteCAN 等发送函数**，否则可能导致死锁或丢帧。
        /// 3. 回调内只能做轻量级操作，例如简单解析、统计或打标记。
        /// 4. 若需要复杂处理（例如长时间解析、解码、存储数据库等），请**将数据入队到另一个线程**，再在后台处理。
        /// 5. 数据可能随时到来，请保证回调尽快返回，避免影响后续帧接收。
        /// 6. 不要把其他类型的端口注册到这个接口，接口不对 portIndex 做类型检查。
        /// </remarks>
        public MCUSerialBridgeError RegisterSerialPortCallback(
            byte portIndex,
            Action<byte[]> callback
        )
        {
            if (callback == null)
                return MCUSerialBridgeError.Win_InvalidParam;

            if (portIndex > MaxPortNumber)
                return MCUSerialBridgeError.Config_PortNumOver;

            // 包装 C# 回调为 P/Invoke 委托
            void del(IntPtr dst_data, uint dst_data_size, IntPtr user_ctx)
            {
                byte[] data = new byte[dst_data_size];
                Marshal.Copy(dst_data, data, 0, (int)dst_data_size);
                callback(data);
            }

            // 保存引用，防止 GC 回收
            _portCallbacks[portIndex] = del;

            // 调用 C 层注册
            return MCUSerialBridgeCoreAPI.msb_register_port_data_callback(
                nativeHandle,
                portIndex,
                _portCallbacks[portIndex],
                IntPtr.Zero
            );
        }

        /// <summary>
        /// 注册指定端口(CAN)的回调函数
        /// </summary>
        /// <param name="portIndex">端口索引</param>
        /// <param name="callback">接收数据回调，CANMessage 为接收到的原始数据</param>
        /// <returns>错误码</returns>
        /// <remarks>
        /// 注意事项：
        /// 1. 回调会在底层 C 层线程中直接调用，请**不要在回调内阻塞**，例如等待 I/O 或 Sleep。
        /// 2. 回调内**不能调用 WriteSerial/WriteCAN 等发送函数**，否则可能导致死锁或丢帧。
        /// 3. 回调内只能做轻量级操作，例如简单解析、统计或打标记。
        /// 4. 若需要复杂处理（例如长时间解析、解码、存储数据库等），请**将数据入队到另一个线程**，再在后台处理。
        /// 5. 数据可能随时到来，请保证回调尽快返回，避免影响后续帧接收。
        /// 6. 不要把其他类型的端口注册到这个接口，接口不对 portIndex 做类型检查。
        /// </remarks>
        public MCUSerialBridgeError RegisterCANPortCallback(
            byte portIndex,
            Action<CANMessage> callback
        )
        {
            if (callback == null)
                return MCUSerialBridgeError.Win_InvalidParam;

            if (portIndex > MaxPortNumber)
                return MCUSerialBridgeError.Config_PortNumOver;

            // 包装 C# 回调为 P/Invoke 委托
            void del(IntPtr dst_data, uint dst_data_size, IntPtr user_ctx)
            {
                try
                {
                    byte[] data = new byte[dst_data_size];
                    Marshal.Copy(dst_data, data, 0, (int)dst_data_size);
                    CANMessage msg = CANMessage.FromBytes(data, dst_data_size);
                    callback(msg);
                }
                catch
                {
                    // 解析失败直接忽略，保证回调不会抛异常阻塞 C 层线程
                }
            }

            // 保存引用，防止 GC 回收
            _portCallbacks[portIndex] = del;

            // 调用 C 层注册
            return MCUSerialBridgeCoreAPI.msb_register_port_data_callback(
                nativeHandle,
                portIndex,
                _portCallbacks[portIndex],
                IntPtr.Zero
            );
        }

        /// <summary>
        /// 启动 MCU 运行（DIVER 模式或透传模式）
        /// </summary>
        /// <param name="timeout">超时时间（毫秒）</param>
        /// <returns>错误码</returns>
        public MCUSerialBridgeError Start(uint timeout = 200)
        {
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            return MCUSerialBridgeCoreAPI.msb_start(nativeHandle, timeout);
        }

        /// <summary>
        /// 设置程序到 MCU
        /// </summary>
        /// <param name="programBytes">
        /// 程序字节数组
        /// - 如果为 null 或空数组：MCU 切换到透传模式（through mode / direct packet communication）
        /// - 如果非 null：MCU 切换到 DIVER 模式并加载程序
        /// </param>
        /// <param name="timeout">超时时间（毫秒）</param>
        /// <returns>错误码</returns>
        public MCUSerialBridgeError Program(byte[] programBytes, uint timeout = 5000)
        {
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            uint len = (programBytes == null) ? 0u : (uint)programBytes.Length;
            return MCUSerialBridgeCoreAPI.msb_program(nativeHandle, programBytes, len, timeout);
        }

        /// <summary>
        /// 启用 Wire Tap 模式
        /// 启用后，即使在 DIVER 模式下，端口数据也会上传
        /// </summary>
        /// <param name="timeout">超时时间（毫秒），默认 200ms</param>
        /// <returns>错误码</returns>
        public MCUSerialBridgeError EnableWireTap(uint timeout = 200)
        {
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            return MCUSerialBridgeCoreAPI.msb_enable_wire_tap(nativeHandle, timeout);
        }

        /// <summary>
        /// PC → MCU memory exchange (UpperIO data for DIVER mode).
        /// Sends input variable values to MCU for the next VM iteration.
        /// </summary>
        /// <param name="data">
        /// UpperIO payload bytes. Format: For each [AsUpperIO] field in cart definition order:
        ///   [typeid: 1 byte][value: N bytes depending on type]
        ///
        /// TypeIDs (primitive types):
        ///   0=Boolean(1B), 1=Byte(1B), 2=SByte(1B), 3=Char(2B), 4=Int16(2B),
        ///   5=UInt16(2B), 6=Int32(4B), 7=UInt32(4B), 8=Single(4B)
        ///
        /// For arrays: [11=ArrayHeader][elemTid:1B][length:4B][raw element bytes...]
        /// For strings: [12=StringHeader][length:2B][UTF8 bytes...]
        /// For references: [16=ReferenceID][rid:4B] (rid=0 means null)
        ///
        /// References for format details:
        ///   - MCURuntime/mcu_runtime.c: vm_put_upper_memory() and comments above it
        ///   - DiverTest/DIVER/DIVERInterface.cs: NotifyLowerData() method (serialization logic)
        ///   - Search keywords: "vm_put_upper_memory", "upper_memory", "AsUpperIO"
        /// </param>
        /// <param name="timeout">Timeout in milliseconds</param>
        /// <returns>Error code</returns>
        public MCUSerialBridgeError MemoryUpperIO(byte[] data, uint timeout = 200)
        {
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            if (data == null || data.Length == 0)
                return MCUSerialBridgeError.Win_InvalidParam;

            return MCUSerialBridgeCoreAPI.msb_memory_upper_io(
                nativeHandle,
                data,
                (uint)data.Length,
                timeout
            );
        }

        /// <summary>
        /// 注册 MCU → PC 内存交换回调（LowerIO 数据，用于 DIVER 模式）
        /// </summary>
        /// <param name="callback">接收数据回调，byte[] 为接收到的原始数据</param>
        /// <returns>错误码</returns>
        /// <remarks>
        /// 注意事项：
        /// 1. 回调会在底层 C 层线程中直接调用，请**不要在回调内阻塞**，例如等待 I/O 或 Sleep。
        /// 2. 回调内**不能调用 MemoryUpperIO 等发送函数**，否则可能导致死锁。
        /// 3. 回调内只能做轻量级操作，例如简单解析、统计或打标记。
        /// 4. 若需要复杂处理（例如长时间解析、解码、存储数据库等），请**将数据入队到另一个线程**，再在后台处理。
        /// 5. 数据可能随时到来，请保证回调尽快返回，避免影响后续帧接收。
        /// </remarks>
        public MCUSerialBridgeError RegisterMemoryLowerIOCallback(Action<byte[]> callback)
        {
            if (callback == null)
                return MCUSerialBridgeError.Win_InvalidParam;

            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            // 包装 C# 回调为 P/Invoke 委托
            void del(IntPtr data, uint data_size, IntPtr user_ctx)
            {
                try
                {
                    byte[] buffer = new byte[data_size];
                    Marshal.Copy(data, buffer, 0, (int)data_size);
                    callback(buffer);
                }
                catch
                {
                    // 解析失败直接忽略，保证回调不会抛异常阻塞 C 层线程
                }
            }

            // 保存引用，防止 GC 回收
            _memoryLowerIOCallback = del;

            // 调用 C 层注册
            return MCUSerialBridgeCoreAPI.msb_register_memory_lower_io_callback(
                nativeHandle,
                _memoryLowerIOCallback,
                IntPtr.Zero
            );
        }

        /// <summary>
        /// 获取 MCU 运行时统计数据
        /// </summary>
        /// <param name="stats">输出统计数据</param>
        /// <param name="timeout">超时时间（ms）</param>
        /// <returns>错误码</returns>
        /// <remarks>
        /// 统计数据包括：
        /// - MCU 运行时间
        /// - 数字 IO 当前状态
        /// - 各端口的收发帧数和字节数
        /// </remarks>
        public MCUSerialBridgeError GetStats(out RuntimeStats stats, uint timeout = 200)
        {
            stats = new RuntimeStats();
            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            return MCUSerialBridgeCoreAPI.msb_get_stats(nativeHandle, out stats, timeout);
        }

        /// <summary>
        /// 注册 MCU Console.WriteLine 日志回调（DIVER 模式日志输出）
        /// </summary>
        /// <param name="callback">接收日志回调，string 为接收到的日志消息</param>
        /// <returns>错误码</returns>
        /// <remarks>
        /// 注意事项：
        /// 1. 回调会在底层 C 层线程中直接调用，请**不要在回调内阻塞**，例如等待 I/O 或 Sleep。
        /// 2. 回调内**不能调用其他发送函数**，否则可能导致死锁。
        /// 3. 回调内只能做轻量级操作，例如打印日志、入队等。
        /// 4. 若需要复杂处理，请**将数据入队到另一个线程**，再在后台处理。
        /// 5. 数据可能随时到来，请保证回调尽快返回，避免影响后续帧接收。
        /// </remarks>
        public MCUSerialBridgeError RegisterConsoleWriteLineCallback(Action<string> callback)
        {
            if (callback == null)
                return MCUSerialBridgeError.Win_InvalidParam;

            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            // 包装 C# 回调为 P/Invoke 委托
            void del(IntPtr message, uint message_len, IntPtr user_ctx)
            {
                try
                {
                    string msg = Marshal.PtrToStringAnsi(message, (int)message_len);
                    callback(msg);
                }
                catch
                {
                    // 解析失败直接忽略，保证回调不会抛异常阻塞 C 层线程
                }
            }

            // 保存引用，防止 GC 回收
            _consoleWriteLineCallback = del;

            // 调用 C 层注册
            return MCUSerialBridgeCoreAPI.msb_register_console_writeline_callback(
                nativeHandle,
                _consoleWriteLineCallback,
                IntPtr.Zero
            );
        }

        /// <summary>
        /// 注册 MCU 致命错误回调（MCU HardFault 或 ASSERT 失败时触发）
        /// </summary>
        /// <param name="callback">接收错误回调，ErrorPayload 为错误详情</param>
        /// <returns>错误码</returns>
        /// <remarks>
        /// 注意事项：
        /// 1. MCU 检测到致命错误时会连续发送多次（防止丢包），C 层已做去重，回调只会触发一次。
        /// 2. 回调触发后 MCU 会自动复位，连接会断开。
        /// 3. 错误信息包括：IL 偏移、行号、Core Dump 寄存器或错误字符串。
        /// 4. 可通过 ErrorPayload.ToJson() 获取 JSON 格式的错误详情，便于上报前端显示。
        /// </remarks>
        public MCUSerialBridgeError RegisterFatalErrorCallback(Action<ErrorPayload> callback)
        {
            if (callback == null)
                return MCUSerialBridgeError.Win_InvalidParam;

            if (nativeHandle == IntPtr.Zero)
                return MCUSerialBridgeError.Win_HandleNotFound;

            // 包装 C# 回调为 P/Invoke 委托
            void del(IntPtr payload, IntPtr user_ctx)
            {
                try
                {
                    var errorPayload = Marshal.PtrToStructure<ErrorPayload>(payload);
                    callback(errorPayload);
                }
                catch
                {
                    // 解析失败直接忽略，保证回调不会抛异常阻塞 C 层线程
                }
            }

            // 保存引用，防止 GC 回收
            _fatalErrorCallback = del;

            // 调用 C 层注册
            return MCUSerialBridgeCoreAPI.msb_register_fatal_error_callback(
                nativeHandle,
                _fatalErrorCallback,
                IntPtr.Zero
            );
        }
    }
}
