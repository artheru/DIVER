using System;
using System.IO;
using System.Runtime.InteropServices;
using MCUBootloaderCLR;

namespace MCUBootloaderTest
{
    class Program
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear, wMonth, wDayOfWeek, wDay,
                          wHour, wMinute, wSecond, wMilliseconds;
        }

        [DllImport("kernel32.dll")]
        private static extern void GetLocalTime(out SYSTEMTIME lpSystemTime);

        private static void Log(string fmt, params object[] args)
        {
            GetLocalTime(out SYSTEMTIME st);
            Console.WriteLine(
                $"[{st.wHour:D2}:{st.wMinute:D2}:{st.wSecond:D2}.{st.wMilliseconds:D3}] TestBL | {string.Format(fmt, args)}"
            );
        }

        private static void LogError(string fmt, params object[] args)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Log(fmt, args);
            Console.ResetColor();
        }

        private static void LogSuccess(string fmt, params object[] args)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Log(fmt, args);
            Console.ResetColor();
        }

        private static void LogWarning(string fmt, params object[] args)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Log(fmt, args);
            Console.ResetColor();
        }

        static int Main(string[] args)
        {
            Console.WriteLine("==============================================");
            Console.WriteLine("    MCU Bootloader 固件刷写测试工具");
            Console.WriteLine("==============================================");
            Console.WriteLine();

            // 解析命令行参数
            if (args.Length < 2)
            {
                Console.WriteLine("用法: TestBL.exe <端口> <UPG文件> [波特率]");
                Console.WriteLine();
                Console.WriteLine("参数:");
                Console.WriteLine("  端口      串口名称，如 COM3");
                Console.WriteLine("  UPG文件   UPG 固件文件路径");
                Console.WriteLine("  波特率    可选，默认自动探测 (0)");
                Console.WriteLine();
                Console.WriteLine("示例:");
                Console.WriteLine("  TestBL.exe COM3 firmware.upg");
                Console.WriteLine("  TestBL.exe COM3 firmware.upg 460800");
                return 1;
            }

            string port = args[0];
            string upgPath = args[1];
            uint baud = 0;  // 默认自动探测

            if (args.Length >= 3)
            {
                if (!uint.TryParse(args[2], out baud))
                {
                    LogError("无效的波特率: {0}", args[2]);
                    return 1;
                }
            }

            // 检查 UPG 文件是否存在
            if (!File.Exists(upgPath))
            {
                LogError("UPG 文件不存在: {0}", upgPath);
                return 1;
            }

            Console.WriteLine($"端口: {port}");
            Console.WriteLine($"固件: {upgPath}");
            Console.WriteLine($"波特率: {(baud == 0 ? "自动探测" : baud.ToString())}");
            Console.WriteLine();

            // 解析 UPG 文件
            UPGFile upgFile;
            try
            {
                Log("正在解析 UPG 文件...");
                upgFile = new UPGFile(upgPath);
                LogSuccess("UPG 文件解析成功");
            }
            catch (Exception ex)
            {
                LogError("UPG 文件解析失败: {0}", ex.Message);
                return 1;
            }

            // 打开 Bootloader 连接
            using var bl = new MCUBootloaderHandler();
            
            Log("正在连接 Bootloader (端口: {0})...", port);
            var err = bl.Open(port, baud);
            if (err != MCUBootloaderError.OK)
            {
                LogError("连接失败: {0}", err);
                return 1;
            }
            LogSuccess("连接成功，波特率: {0}", bl.Baudrate);
            Console.WriteLine();

            // 读取下位机信息
            Log("正在读取下位机信息...");
            err = bl.CommandRead(out FirmwareInfo mcuInfo, 2000);
            if (err != MCUBootloaderError.OK)
            {
                LogError("读取下位机信息失败: {0}", err);
                return 1;
            }
            LogSuccess("读取下位机信息成功");
            Console.WriteLine();

            // 打印对比信息
            Console.WriteLine("============== 版本对比 ==============");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("【下位机固件信息】");
            Console.ResetColor();
            Console.WriteLine($"  产品型号:  {mcuInfo.ProductName}");
            Console.WriteLine($"  标签版本:  {mcuInfo.Tag}");
            Console.WriteLine($"  提交版本:  {mcuInfo.Commit}");
            Console.WriteLine($"  编译时间:  {mcuInfo.BuildTime}");
            Console.WriteLine($"  固件大小:  {mcuInfo.AppLength} bytes");
            Console.WriteLine($"  固件CRC:   0x{mcuInfo.AppCRC32:X8}");
            Console.WriteLine($"  固件有效:  {(mcuInfo.IsValid == 1 ? "是" : "否")}");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("【UPG 文件信息】");
            Console.ResetColor();
            Console.WriteLine($"  产品型号:  {upgFile.ProductName}");
            Console.WriteLine($"  标签版本:  {upgFile.FirmwareTag}");
            Console.WriteLine($"  提交版本:  {upgFile.FirmwareCommit}");
            Console.WriteLine($"  编译时间:  {upgFile.BuildTime}");
            Console.WriteLine($"  固件大小:  {upgFile.FirmwareSize} bytes");
            Console.WriteLine($"  固件CRC:   0x{upgFile.SectionCRC:X8}");
            Console.WriteLine();

            // 检查版本是否一致
            bool versionMatch = 
                mcuInfo.Tag?.Trim() == upgFile.FirmwareTag?.Trim() &&
                mcuInfo.Commit?.Trim() == upgFile.FirmwareCommit?.Trim() &&
                mcuInfo.BuildTime?.Trim() == upgFile.BuildTime?.Trim() &&
                mcuInfo.AppLength == upgFile.FirmwareSize &&
                mcuInfo.AppCRC32 == upgFile.SectionCRC;

            // 检查产品型号是否一致
            bool productMatch = mcuInfo.ProductName?.Trim() == upgFile.ProductName?.Trim();

            Console.WriteLine("=======================================");
            Console.WriteLine();

            if (!productMatch)
            {
                LogError("产品型号不匹配！下位机: {0}, 固件: {1}", 
                    mcuInfo.ProductName, upgFile.ProductName);
                LogError("拒绝刷写，请检查固件文件是否正确。");
                return 1;
            }

            if (versionMatch)
            {
                LogSuccess("版本完全一致，无需刷写。");
                Console.WriteLine();
                Console.Write("是否强制刷写？(Y/N): ");
                var key = Console.ReadLine()?.Trim().ToUpper();
                if (key != "Y")
                {
                    Log("已取消刷写。");
                    return 0;
                }
                LogWarning("用户选择强制刷写。");
            }
            else
            {
                LogWarning("版本不一致，需要刷写。");
                Console.WriteLine();
                Console.Write("是否开始刷写？(Y/N): ");
                var key = Console.ReadLine()?.Trim().ToUpper();
                if (key != "Y")
                {
                    Log("已取消刷写。");
                    return 0;
                }
            }

            Console.WriteLine();
            Log("开始固件刷写流程...");
            Console.WriteLine();

            // 注册进度回调
            int lastProgress = -1;
            bl.RegisterProgressCallback((progress, error) =>
            {
                if (error != MCUBootloaderError.OK)
                {
                    LogError("写入错误 @ {0}%: {1}", progress, error);
                    return;
                }

                // 每 5% 或最后 100% 打印一次
                if (progress != lastProgress && (progress % 5 == 0 || progress == 100))
                {
                    lastProgress = progress;
                    
                    // 绘制进度条
                    int barWidth = 40;
                    int filled = progress * barWidth / 100;
                    string bar = new string('█', filled) + new string('░', barWidth - filled);
                    
                    Console.Write($"\r  进度: [{bar}] {progress,3}%");
                    if (progress == 100)
                        Console.WriteLine();
                }
            });

            // 步骤 1: 擦除
            Log("步骤 1/3: 擦除固件...");
            err = bl.CommandErase(upgFile, 15000);
            if (err != MCUBootloaderError.OK)
            {
                LogError("擦除失败: {0}", err);
                return 1;
            }
            LogSuccess("擦除完成");
            Console.WriteLine();

            // 步骤 2: 写入
            Log("步骤 2/3: 写入固件...");
            err = bl.WriteFirmware(upgFile, 1000);
            if (err != MCUBootloaderError.OK)
            {
                LogError("写入失败: {0}", err);
                return 1;
            }
            LogSuccess("写入完成");
            Console.WriteLine();

            // 步骤 3: 验证
            Log("步骤 3/3: 验证固件...");
            err = bl.CommandRead(out FirmwareInfo newInfo, 2000);
            if (err != MCUBootloaderError.OK)
            {
                LogError("验证读取失败: {0}", err);
                return 1;
            }

            if (newInfo.AppCRC32 == upgFile.SectionCRC && 
                newInfo.AppLength == upgFile.FirmwareSize &&
                newInfo.IsValid == 1)
            {
                LogSuccess("固件验证通过！");
            }
            else
            {
                LogError("固件验证失败！");
                LogError("  期望 CRC: 0x{0:X8}, 实际 CRC: 0x{1:X8}", 
                    upgFile.SectionCRC, newInfo.AppCRC32);
                return 1;
            }
            Console.WriteLine();

            // 询问是否重启
            Console.Write("是否重启进入应用程序？(Y/N): ");
            var exitKey = Console.ReadLine()?.Trim().ToUpper();
            if (exitKey == "Y")
            {
                Log("正在重启 MCU...");
                err = bl.CommandExit(1000);
                if (err != MCUBootloaderError.OK)
                {
                    LogWarning("重启命令发送后未收到响应（可能已重启）");
                }
                else
                {
                    LogSuccess("MCU 已重启");
                }
            }

            Console.WriteLine();
            LogSuccess("固件刷写流程完成！");
            return 0;
        }
    }
}
