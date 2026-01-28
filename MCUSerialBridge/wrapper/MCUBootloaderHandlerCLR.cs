using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MCUBootloaderCLR
{
    #region 错误码枚举

    /// <summary>
    /// MCU Bootloader 错误码枚举
    /// </summary>
    public enum MCUBootloaderError : uint
    {
        // 成功
        OK = 0x00000000,

        // Windows/上位机侧错误 (0x8xxxxxxx)
        Win_OpenFailed = 0x80000001,
        Win_ConfigFailed = 0x80000002,
        Win_WriteFailed = 0x80000003,
        Win_ReadFailed = 0x80000004,
        Win_InvalidParam = 0x80000005,
        Win_HandleNotFound = 0x80000006,
        Win_OutOfMemory = 0x80000007,
        Win_Timeout = 0x80000008,
        Win_ProbeFailed = 0x80000009,
        Win_AlreadyOpen = 0x8000000A,
        Win_NotOpen = 0x8000000B,

        // 协议层错误 (0xExxxxxxx)
        Proto_HeaderError = 0xE0000001,
        Proto_TailError = 0xE0000002,
        Proto_CRCError = 0xE0000003,
        Proto_LengthError = 0xE0000004,
        Proto_UnknownResponse = 0xE0000005,
        Proto_ResponseMismatch = 0xE0000006,

        // MCU 返回的错误 (0x0F0000xx)
        MCU_UnknownCommand = 0x0F000001,
        MCU_InvalidPayload = 0x0F000002,
        MCU_FlashEraseFailed = 0x0F000003,
        MCU_FirmwareDecryptionError = 0x0F000004,
        MCU_FirmwareLengthError = 0x0F000005,
        MCU_NotErased = 0x0F000006,
        MCU_WriteOffsetMisaligned = 0x0F000007,
        MCU_WriteLengthTooLong = 0x0F000008,
        MCU_WriteError = 0x0F000009,
        MCU_WriteFirmwareCrcMismatch = 0x0F00000A,
        MCU_WriteAppInvalid = 0x0F00000B,
    }

    #endregion

    #region 数据结构

    /// <summary>
    /// 固件元数据（用于 UI 显示，UPG 和 MCU 通用）
    /// </summary>
    public record FirmwareMetadata(
        string ProductName,
        string Tag,
        string Commit,
        string BuildTime,
        uint AppLength,
        uint AppCRC32,
        bool IsValid
    );

    /// <summary>
    /// 下位机固件信息
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct FirmwareInfo
    {
        /// <summary>固件是否有效：1=有效，0=无效</summary>
        public int IsValid;

        /// <summary>产品型号</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string ProductName;

        /// <summary>标签版本</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
        public string Tag;

        /// <summary>Git Commit</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
        public string Commit;

        /// <summary>编译时间</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 24)]
        public string BuildTime;

        /// <summary>固件长度</summary>
        public uint AppLength;

        /// <summary>固件 CRC32</summary>
        public uint AppCRC32;

        /// <summary>固件信息区 CRC32</summary>
        public uint AppInfoCRC32;

        public override readonly string ToString()
        {
            string validStr = IsValid == 1 ? "有效" : (IsValid == 0 ? "无效" : "未知");
            return $"产品: {ProductName}, 标签: {Tag}, Commit: {Commit}, " +
                   $"编译时间: {BuildTime}, 固件大小: {AppLength}, " +
                   $"CRC: 0x{AppCRC32:X8}, 有效: {validStr}";
        }

        /// <summary>
        /// 转换为通用元数据
        /// </summary>
        public readonly FirmwareMetadata ToMetadata()
        {
            return new FirmwareMetadata(
                ProductName ?? "",
                Tag ?? "",
                Commit ?? "",
                BuildTime ?? "",
                AppLength,
                AppCRC32,
                IsValid == 1
            );
        }
    }

    /// <summary>
    /// Erase 命令参数（内部结构，与 C 层对应）
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct EraseParamsC
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Tag;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Commit;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
        public byte[] BuildTime;

        public uint AppLength;
        public uint AppCRC32;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] First32Bytes;
    }

    #endregion

    #region UPG 文件解析

    /// <summary>
    /// UPG 固件文件解析类
    /// </summary>
    public class UPGFile
    {
        /// <summary>文件路径</summary>
        public string FilePath { get; }

        /// <summary>产品型号</summary>
        public string ProductName { get; private set; } = "";

        /// <summary>BL 结构版本</summary>
        public uint BLStructureVersion { get; private set; }

        /// <summary>固件标签版本</summary>
        public string FirmwareTag { get; private set; } = "";

        /// <summary>固件 Commit</summary>
        public string FirmwareCommit { get; private set; } = "";

        /// <summary>编译时间</summary>
        public string BuildTime { get; private set; } = "";

        /// <summary>段数</summary>
        public uint SectionNumber { get; private set; }

        /// <summary>段在 UPG 文件中的偏移</summary>
        public uint SectionUPGAddress { get; private set; }

        /// <summary>段长度</summary>
        public uint SectionLength { get; private set; }

        /// <summary>段 Flash 地址</summary>
        public uint SectionFlashAddress { get; private set; }

        /// <summary>段 CRC32</summary>
        public uint SectionCRC { get; private set; }

        /// <summary>固件大小（等于 SectionLength）</summary>
        public uint FirmwareSize => SectionLength;

        /// <summary>加密后的固件数据（包含前 32 字节）</summary>
        public byte[] EncryptedData { get; private set; } = Array.Empty<byte>();

        // 原始字节（用于传给 C 层）
        internal byte[] FirmwareTagBytes { get; private set; } = new byte[8];
        internal byte[] FirmwareCommitBytes { get; private set; } = new byte[8];
        internal byte[] BuildTimeBytes { get; private set; } = new byte[24];

        /// <summary>
        /// 从文件路径加载并解析 UPG 文件
        /// </summary>
        /// <param name="path">UPG 文件路径</param>
        /// <exception cref="ArgumentException">文件格式错误</exception>
        public UPGFile(string path)
        {
            FilePath = path;
            Parse(File.ReadAllBytes(path));
        }

        /// <summary>
        /// 从字节数组解析 UPG 文件
        /// </summary>
        /// <param name="data">UPG 文件数据</param>
        /// <exception cref="ArgumentException">文件格式错误</exception>
        public UPGFile(byte[] data)
        {
            FilePath = "";
            Parse(data);
        }

        private void Parse(byte[] data)
        {
            // 校验文件长度
            if (data.Length < 0x120)
            {
                throw new ArgumentException("文件长度太短，不是有效 UPG 文件");
            }

            // 解析整体 CRC32
            uint crc32All = BitConverter.ToUInt32(data, 0x00);

            // 校验整体 CRC32: 从 0x04 到文件末尾
            uint crcCalc = CalcCRC32(data, 0x04, data.Length - 0x04);
            if (crcCalc != crc32All)
            {
                throw new ArgumentException(
                    $"整体 CRC32 校验失败: 计算值=0x{crcCalc:X8}, 文件值=0x{crc32All:X8}");
            }

            // 产品类型
            ProductName = SafeBytesToString(data, 0x04, 16);

            // 固件格式版本
            BLStructureVersion = BitConverter.ToUInt32(data, 0x14);
            if (BLStructureVersion != 1)
            {
                throw new ArgumentException(
                    $"文件格式错误: BLStructureVersion={BLStructureVersion}");
            }

            // 固件 Tag/Commit/BuildTime
            FirmwareTagBytes = new byte[8];
            Array.Copy(data, 0x18, FirmwareTagBytes, 0, 8);
            FirmwareTag = SafeBytesToString(data, 0x18, 8);

            FirmwareCommitBytes = new byte[8];
            Array.Copy(data, 0x20, FirmwareCommitBytes, 0, 8);
            FirmwareCommit = SafeBytesToString(data, 0x20, 8);

            BuildTimeBytes = new byte[24];
            Array.Copy(data, 0x28, BuildTimeBytes, 0, 24);
            BuildTime = SafeBytesToString(data, 0x28, 24);

            // 总段数
            SectionNumber = BitConverter.ToUInt32(data, 0x40);
            if (SectionNumber != 1)
            {
                throw new ArgumentException($"只支持单段固件，发现段数: {SectionNumber}");
            }

            // 段描述
            SectionUPGAddress = BitConverter.ToUInt32(data, 0x44);
            SectionLength = BitConverter.ToUInt32(data, 0x48);
            SectionFlashAddress = BitConverter.ToUInt32(data, 0x4C);
            SectionCRC = BitConverter.ToUInt32(data, 0x50);

            // 提取固件加密段
            int encryptedLen = (int)SectionLength + 32;
            EncryptedData = new byte[encryptedLen];
            Array.Copy(data, 0x100, EncryptedData, 0, encryptedLen);
        }

        /// <summary>
        /// 获取固件元数据（用于 UI 显示）
        /// </summary>
        public FirmwareMetadata GetMetadata()
        {
            return new FirmwareMetadata(
                ProductName,
                FirmwareTag,
                FirmwareCommit,
                BuildTime,
                FirmwareSize,
                SectionCRC,
                true // UPG 文件解析成功即为有效
            );
        }

        private static string SafeBytesToString(byte[] data, int offset, int length)
        {
            byte[] slice = new byte[length];
            Array.Copy(data, offset, slice, 0, length);

            // 全 0xFF
            if (Array.TrueForAll(slice, b => b == 0xFF))
                return $"FF × {length}";

            // 全 0x00
            if (Array.TrueForAll(slice, b => b == 0x00))
                return $"00 × {length}";

            try
            {
                // 去除尾部 0x00
                int end = Array.IndexOf(slice, (byte)0x00);
                if (end < 0) end = length;
                return Encoding.UTF8.GetString(slice, 0, end).Trim();
            }
            catch
            {
                // 无法解码，返回 Hex
                if (length > 8)
                    return BitConverter.ToString(slice, 0, 8).Replace("-", "") + $"... ({length} bytes)";
                return BitConverter.ToString(slice).Replace("-", "");
            }
        }

        /// <summary>
        /// 计算 CRC32（与 Python zlib.crc32 兼容）
        /// </summary>
        private static uint CalcCRC32(byte[] data, int offset, int length)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < length; i++)
            {
                byte b = data[offset + i];
                crc = crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFF;
        }

        private static readonly uint[] crc32Table = GenerateCRC32Table();

        private static uint[] GenerateCRC32Table()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320;
                    else
                        crc >>= 1;
                }
                table[i] = crc;
            }
            return table;
        }
    }

    #endregion

    #region P/Invoke 声明

    internal static class MCUBootloaderCoreAPI
    {
        private const string DLL = "mcu_serial_bridge.dll";

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUBootloaderError mbl_open(
            out IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string port,
            uint baud);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUBootloaderError mbl_close(IntPtr handle);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint mbl_get_baudrate(IntPtr handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void mbl_progress_callback_t(
            int progress,
            MCUBootloaderError error,
            IntPtr user_ctx);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUBootloaderError mbl_register_progress_callback(
            IntPtr handle,
            mbl_progress_callback_t callback,
            IntPtr user_ctx);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUBootloaderError mbl_command_read(
            IntPtr handle,
            out FirmwareInfo info,
            uint timeout_ms);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUBootloaderError mbl_command_erase(
            IntPtr handle,
            ref EraseParamsC eraseParams,
            uint timeout_ms);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUBootloaderError mbl_command_write(
            IntPtr handle,
            uint offset,
            uint total_length,
            [In] byte[] chunk_data,
            uint chunk_length,
            uint timeout_ms);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUBootloaderError mbl_command_exit(
            IntPtr handle,
            uint timeout_ms);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MCUBootloaderError mbl_write_firmware(
            IntPtr handle,
            [In] byte[] firmware,
            uint firmware_len,
            uint timeout_ms);
    }

    #endregion

    #region 主类

    /// <summary>
    /// MCU Bootloader 通讯封装类
    /// </summary>
    public class MCUBootloaderHandler : IDisposable
    {
        private IntPtr nativeHandle = IntPtr.Zero;

        /// <summary>是否已打开</summary>
        public bool IsOpen => nativeHandle != IntPtr.Zero;

        /// <summary>当前波特率</summary>
        public uint Baudrate => IsOpen ? MCUBootloaderCoreAPI.mbl_get_baudrate(nativeHandle) : 0;

        // 保持对回调委托的引用，防止 GC 回收
        private MCUBootloaderCoreAPI.mbl_progress_callback_t? _progressCallback;

        /// <summary>
        /// 构造函数
        /// </summary>
        public MCUBootloaderHandler()
        {
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~MCUBootloaderHandler()
        {
            Dispose(false);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (nativeHandle != IntPtr.Zero)
            {
                MCUBootloaderCoreAPI.mbl_close(nativeHandle);
                nativeHandle = IntPtr.Zero;
            }
        }

        /// <summary>
        /// 打开串口并连接 MCU Bootloader
        /// </summary>
        /// <param name="port">串口名称，如 "COM3"</param>
        /// <param name="baud">波特率，0 表示自动探测</param>
        /// <returns>错误码</returns>
        public MCUBootloaderError Open(string port, uint baud = 0)
        {
            if (IsOpen)
                return MCUBootloaderError.Win_AlreadyOpen;

            return MCUBootloaderCoreAPI.mbl_open(out nativeHandle, port, baud);
        }

        /// <summary>
        /// 关闭串口
        /// </summary>
        /// <returns>错误码</returns>
        public MCUBootloaderError Close()
        {
            if (!IsOpen)
                return MCUBootloaderError.Win_NotOpen;

            var err = MCUBootloaderCoreAPI.mbl_close(nativeHandle);
            nativeHandle = IntPtr.Zero;
            return err;
        }

        /// <summary>
        /// 注册进度回调
        /// </summary>
        /// <param name="callback">回调函数 (progress: 0-100, error)</param>
        /// <returns>错误码</returns>
        public MCUBootloaderError RegisterProgressCallback(Action<int, MCUBootloaderError> callback)
        {
            if (!IsOpen)
                return MCUBootloaderError.Win_NotOpen;

            void del(int progress, MCUBootloaderError error, IntPtr ctx)
            {
                callback(progress, error);
            }

            _progressCallback = del;

            return MCUBootloaderCoreAPI.mbl_register_progress_callback(
                nativeHandle, _progressCallback, IntPtr.Zero);
        }

        /// <summary>
        /// 读取下位机固件信息
        /// </summary>
        /// <param name="info">返回固件信息</param>
        /// <param name="timeout">超时时间（毫秒）</param>
        /// <returns>错误码</returns>
        public MCUBootloaderError CommandRead(out FirmwareInfo info, uint timeout = 1000)
        {
            info = default;
            if (!IsOpen)
                return MCUBootloaderError.Win_NotOpen;

            return MCUBootloaderCoreAPI.mbl_command_read(nativeHandle, out info, timeout);
        }

        /// <summary>
        /// 擦除固件
        /// </summary>
        /// <param name="upgFile">已解析的 UPG 文件</param>
        /// <param name="timeout">超时时间（毫秒），建议 10000</param>
        /// <returns>错误码</returns>
        public MCUBootloaderError CommandErase(UPGFile upgFile, uint timeout = 10000)
        {
            if (!IsOpen)
                return MCUBootloaderError.Win_NotOpen;

            if (upgFile == null)
                return MCUBootloaderError.Win_InvalidParam;

            // 构建 EraseParams
            var eraseParams = new EraseParamsC
            {
                Tag = new byte[8],
                Commit = new byte[8],
                BuildTime = new byte[24],
                AppLength = upgFile.FirmwareSize,
                AppCRC32 = upgFile.SectionCRC,
                First32Bytes = new byte[32],
            };

            Array.Copy(upgFile.FirmwareTagBytes, eraseParams.Tag, 8);
            Array.Copy(upgFile.FirmwareCommitBytes, eraseParams.Commit, 8);
            Array.Copy(upgFile.BuildTimeBytes, eraseParams.BuildTime, 24);
            Array.Copy(upgFile.EncryptedData, 0, eraseParams.First32Bytes, 0, 32);

            return MCUBootloaderCoreAPI.mbl_command_erase(nativeHandle, ref eraseParams, timeout);
        }

        /// <summary>
        /// 写入固件数据块
        /// </summary>
        /// <param name="offset">偏移</param>
        /// <param name="totalLength">总长度</param>
        /// <param name="chunkData">块数据</param>
        /// <param name="timeout">超时时间（毫秒）</param>
        /// <returns>错误码</returns>
        public MCUBootloaderError CommandWrite(
            uint offset, uint totalLength, byte[] chunkData, uint timeout = 1000)
        {
            if (!IsOpen)
                return MCUBootloaderError.Win_NotOpen;

            if (chunkData == null || chunkData.Length == 0 || chunkData.Length > 64)
                return MCUBootloaderError.Win_InvalidParam;

            return MCUBootloaderCoreAPI.mbl_command_write(
                nativeHandle, offset, totalLength, chunkData, (uint)chunkData.Length, timeout);
        }

        /// <summary>
        /// 退出 Bootloader，重启进入应用程序
        /// </summary>
        /// <param name="timeout">超时时间（毫秒）</param>
        /// <returns>错误码</returns>
        public MCUBootloaderError CommandExit(uint timeout = 1000)
        {
            if (!IsOpen)
                return MCUBootloaderError.Win_NotOpen;

            return MCUBootloaderCoreAPI.mbl_command_exit(nativeHandle, timeout);
        }

        /// <summary>
        /// 写入完整固件（使用 UPG 文件）
        /// </summary>
        /// <param name="upgFile">已解析的 UPG 文件</param>
        /// <param name="timeout">单次写入超时（毫秒）</param>
        /// <returns>错误码</returns>
        public MCUBootloaderError WriteFirmware(UPGFile upgFile, uint timeout = 1000)
        {
            if (!IsOpen)
                return MCUBootloaderError.Win_NotOpen;

            if (upgFile == null)
                return MCUBootloaderError.Win_InvalidParam;

            // 跳过前 32 字节（加密头），写入实际固件数据
            byte[] firmwareData = new byte[upgFile.FirmwareSize];
            Array.Copy(upgFile.EncryptedData, 32, firmwareData, 0, upgFile.FirmwareSize);

            return MCUBootloaderCoreAPI.mbl_write_firmware(
                nativeHandle, firmwareData, (uint)firmwareData.Length, timeout);
        }

        /// <summary>
        /// 完整的固件升级流程
        /// </summary>
        /// <param name="upgFile">已解析的 UPG 文件</param>
        /// <param name="eraseTimeout">擦除超时（毫秒）</param>
        /// <param name="writeTimeout">单次写入超时（毫秒）</param>
        /// <returns>错误码</returns>
        public MCUBootloaderError UpgradeFirmware(
            UPGFile upgFile,
            uint eraseTimeout = 10000,
            uint writeTimeout = 1000)
        {
            // 1. 擦除
            var err = CommandErase(upgFile, eraseTimeout);
            if (err != MCUBootloaderError.OK)
                return err;

            // 2. 写入
            err = WriteFirmware(upgFile, writeTimeout);
            if (err != MCUBootloaderError.OK)
                return err;

            // 3. 退出（可选，也可以让调用者自己决定是否退出）
            // err = CommandExit(1000);

            return MCUBootloaderError.OK;
        }
    }

    #endregion
}
