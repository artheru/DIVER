using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

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
    /// 抽象端口配置基类
    /// </summary>
    public abstract class PortConfig
    {
        /// <summary>端口类型（由子类实现）</summary>
        public abstract byte PortType { get; }

        /// <summary>序列化端口配置为字节数组（供 P/Invoke 使用）</summary>
        /// <returns>返回固定长度字节数组（16 bytes）</returns>
        public abstract byte[] ToBytes();
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
    }

    /// <summary>
    /// CAN 帧结构（标准帧 11-bit ID + 1-bit RTR + 4-bit DLC + Payload）
    /// </summary>
    public class CANMessage
    {
        /// <summary>标准帧 ID（0~0x7FF，11 位）</summary>
        public ushort ID { get; set; }

        /// <summary>远程帧标志：false = 数据帧，true = 远程帧</summary>
        public bool RTR { get; set; }

        /// <summary>数据长度码：0~8</summary>
        public byte DLC { get; set; }

        /// <summary>数据负载，长度必须严格等于 DLC（DLC=0 时可为 null）</summary>
        public byte[] Payload { get; set; }

        /// <summary>
        /// 序列化为 MCU 协议字节流
        /// </summary>
        /// <returns>返回字节数组：2 bytes header + Payload</returns>
        /// <exception cref="ArgumentOutOfRangeException">如果 DLC > 8</exception>
        /// <exception cref="ArgumentException">如果 Payload 长度 != DLC</exception>
        public byte[] ToBytes()
        {
            if (DLC > 8)
                throw new ArgumentOutOfRangeException(nameof(DLC), "DLC must be 0-8");
            if (DLC > 0 && (Payload == null || Payload.Length != DLC))
                throw new ArgumentException("Payload length must equal DLC");

            // 构造 2 字节 header
            ushort header = 0;
            header |= (ushort)(ID & 0x7FF); // bits 0-10
            if (RTR)
                header |= (1 << 11); // bit 11
            header |= (ushort)((DLC & 0xF) << 12); // bits 12-15

            byte[] result = new byte[2 + DLC];
            byte[] headerBytes = BitConverter.GetBytes(header); // 小端序
            result[0] = headerBytes[0];
            result[1] = headerBytes[1];

            if (DLC > 0)
                Buffer.BlockCopy(Payload, 0, result, 2, DLC);
            return result;
        }

        /// <summary>
        /// 反序列化 MCU 协议字节流为 CANMessage
        /// </summary>
        /// <param name="data">原始字节数组</param>
        /// <param name="length">实际有效长度</param>
        /// <returns>CANMessage 实例</returns>
        /// <exception cref="ArgumentException">数据长度错误</exception>
        public static CANMessage FromBytes(byte[] data, uint length)
        {
            if (data == null || length > data.Length || length < 2)
                throw new ArgumentException("Data must be at least 2 bytes");

            ushort header = BitConverter.ToUInt16(data, 0);

            var msg = new CANMessage
            {
                ID = (ushort)(header & 0x7FF),
                RTR = (header & (1 << 11)) != 0,
                DLC = (byte)((header >> 12) & 0xF),
            };

            if (msg.DLC > 0)
            {
                if (length < 2 + msg.DLC)
                    throw new ArgumentException("Data length less than DLC");
                msg.Payload = new byte[msg.DLC];
                Buffer.BlockCopy(data, 2, msg.Payload, 0, msg.DLC);
            }
            else
            {
                msg.Payload = Array.Empty<byte>();
            }

            return msg;
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
        public static extern MCUSerialBridgeError msb_version(
            IntPtr handle,
            out VersionInfo version,
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
    }
}
