using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using MCUSerialBridgeCLR;

namespace MCUTestDIVER
{
    /// <summary>
    /// DIVER 模式测试程序
    /// 测试流程: Open → Reset → GetVersion → GetState → Configure → Program → GetState → Start → GetState
    /// 主循环: MemoryUpperIO → GetState → Sleep(1s) → (循环3次后) EnableWireTap
    /// </summary>
    class Program
    {
        static volatile bool gRunning = true;

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear,
                wMonth,
                wDayOfWeek,
                wDay,
                wHour,
                wMinute,
                wSecond,
                wMilliseconds;
        }

        [DllImport("kernel32.dll")]
        private static extern void GetLocalTime(out SYSTEMTIME lpSystemTime);

        private static void Log(string fmt, params object[] args)
        {
            GetLocalTime(out SYSTEMTIME st);
            Console.WriteLine(
                $"[{st.wHour:D2}:{st.wMinute:D2}:{st.wSecond:D2}.{st.wMilliseconds:D3}] DIVER Test | {string.Format(fmt, args)}"
            );
        }

        /// <summary>
        /// MCU 测试端口索引常量
        /// </summary>
        internal static class BKBoardPortIndex
        {
            // RS485
            public const byte RS485_1 = 0;
            public const byte RS485_2 = 1;
            public const byte RS485_3 = 2;

            // RS232
            public const byte RS232_1 = 3;

            // CAN
            public const byte CAN1 = 4;
            public const byte CAN2 = 5;
        }

        static void Main(string[] args)
        {
            Console.CancelKeyPress += (s, e) =>
            {
                gRunning = false;
                e.Cancel = true;
            };

            Log("=== DIVER Mode Test Start ===");

            // Args (strict):
            //   TestDIVER.exe <COMName> <baud> <program.bin>
            //
            // Example:
            //   TestDIVER.exe COM18 1000000 D:\path\to\SimpleTestLogic.bin
            if (args == null || args.Length < 3)
            {
                Log("Usage: TestDIVER.exe <COMName> <baud> <program.bin>");
                Log("Example: TestDIVER.exe COM18 1000000 D:\\path\\to\\SimpleTestLogic.bin");
                return;
            }

            string comPort = args[0];
            if (string.IsNullOrWhiteSpace(comPort))
            {
                Log("Invalid COMName (empty).");
                return;
            }

            if (!uint.TryParse(args[1], out uint baud) || baud == 0)
            {
                Log("Invalid baud: {0}", args[1]);
                return;
            }

            string programPath = args[2];
            if (string.IsNullOrWhiteSpace(programPath))
            {
                Log("Invalid program path (empty).");
                return;
            }

            if (!File.Exists(programPath))
            {
                Log("Program file not found: {0}", programPath);
                return;
            }

            byte[] programBytes;
            try
            {
                programBytes = File.ReadAllBytes(programPath);
            }
            catch (Exception ex)
            {
                Log("Failed to read program file: {0}", ex.Message);
                return;
            }

            if (programBytes == null || programBytes.Length == 0)
            {
                Log("Program file is empty: {0}", programPath);
                return;
            }

            // MCU side buffer limit is currently 16KB (see mcu/appl/source/control.c PROGRAM_BUFFER_MAX_SIZE)
            const int PROGRAM_MAX = 16 * 1024;
            if (programBytes.Length > PROGRAM_MAX)
            {
                Log(
                    "Program is too large: {0} bytes (max {1}). Please generate a smaller program or increase MCU PROGRAM_BUFFER_MAX_SIZE.",
                    programBytes.Length,
                    PROGRAM_MAX
                );
                return;
            }

            var bridge = new MCUSerialBridge();

            // 1. Open
            var err = bridge.Open(comPort, baud);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB Open FAILED: {0}", err.ToDescription());
                return;
            }
            Log("MSB Open OK");

            // 2. Reset
            err = bridge.Reset(200);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB Reset FAILED: {0}", err.ToDescription());
                return;
            }
            Thread.Sleep(500);
            Log("MSB Reset OK");

            // 3. GetVersion
            err = bridge.GetVersion(out var version, 100);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB GetVersion FAILED: {0}", err.ToDescription());
                return;
            }
            Log("MSB GetVersion OK: {0}", version.ToString());

            // 4. GetState
            err = bridge.GetState(out var state, 100);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB GetState FAILED: {0}", err.ToDescription());
                return;
            }
            Log("MSB GetState OK: {0}", state.ToString());

            // 5. Configure - 配置端口
            var ports = new List<PortConfig>();
            for (int i = 0; i < 4; i++)
                ports.Add(new SerialPortConfig(9600, 0));
            for (int i = 0; i < 2; i++)
                ports.Add(new CANPortConfig(250000, 10));

            Log("=== Port Configuration ===");
            for (int i = 0; i < ports.Count; i++)
            {
                if (ports[i] is SerialPortConfig s)
                    Log(
                        "Port {0}: Serial, Baud={1}, ReceiveFrameMs={2}",
                        i,
                        s.Baud,
                        s.ReceiveFrameMs
                    );
                else if (ports[i] is CANPortConfig c)
                    Log("Port {0}: CAN, Baud={1}, RetryTimeMs={2}", i, c.Baud, c.RetryTimeMs);
            }
            Log("==========================");

            err = bridge.Configure(ports, 200);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB Configure FAILED: {0}", err.ToDescription());
                return;
            }
            Log("MSB Configure OK");

            // 6. Program - Download DIVER program (DIVER mode)
            Log("=== Programming DIVER ===");
            Log("Program file: {0}", programPath);
            Log("Program size: {0} bytes", programBytes.Length);

            err = bridge.Program(programBytes, 5000);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB Program FAILED: {0}", err.ToDescription());
                return;
            }
            Log("MSB Program OK");

            // 7. GetState after Program
            err = bridge.GetState(out state, 100);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB GetState FAILED: {0}", err.ToDescription());
                return;
            }
            Log("MSB GetState OK: {0}", state.ToString());

            // 注册 LowerIO 回调 (MCU → PC 内存交换)
            bridge.RegisterMemoryLowerIOCallback(data =>
            {
                byte[] displayData = data;
                if (data.Length > 16)
                {
                    displayData = new byte[16];
                    Array.Copy(data, displayData, 16);
                }
                Log(
                    "LowerIO Callback Received: {0} bytes, data: {1}",
                    data.Length,
                    BitConverter.ToString(displayData) + (data.Length > 16 ? "..." : "")
                );
            });
            bridge.RegisterConsoleWriteLineCallback(str =>
            {
                Log("ConsoleWriteLine in MCU Callback Received: >>>{0}<<<", str);
            });

            Log("=== Main Loop Start (DIVER Mode) ===");

            // 8. Start
            err = bridge.Start();
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB Start FAILED: {0}", err.ToDescription());
                return;
            }
            Log("MSB Start OK");

            // 9. GetState after Start
            err = bridge.GetState(out state, 100);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB GetState FAILED: {0}", err.ToDescription());
                return;
            }
            Log("MSB GetState OK: {0}", state.ToString());

            const int LOOP_DELAY_MS = 1000;
            const int TIMEOUT_MS = 200;
            int loopCount = 0;
            bool wireTapEnabled = false;

            while (gRunning)
            {
                try
                {
                    loopCount++;
                    Log("--- Loop {0} ---", loopCount);

                    // MemoryUpperIO - PC → MCU 内存交换 (发送输入变量)
                    byte[] upperIOData = new byte[16];
                    for (int i = 0; i < upperIOData.Length; i++)
                    {
                        upperIOData[i] = (byte)((loopCount + i) & 0xFF);
                    }

                    Log("MemoryUpperIO: Sending {0} bytes", upperIOData.Length);
                    err = bridge.MemoryUpperIO(upperIOData, TIMEOUT_MS);
                    if (err != MCUSerialBridgeError.OK)
                    {
                        Log("MemoryUpperIO FAILED: {0}", err.ToDescription());
                    }
                    else
                    {
                        Log("MemoryUpperIO OK");
                    }

                    // GetState
                    err = bridge.GetState(out state, TIMEOUT_MS);
                    if (err != MCUSerialBridgeError.OK)
                    {
                        Log("GetState FAILED: {0}", err.ToDescription());
                    }
                    else
                    {
                        Log("GetState OK: {0}", state.ToString());
                    }

                    // 循环 3 次后启用 WireTap
                    if (loopCount == 3 && !wireTapEnabled)
                    {
                        Log("=== Enabling WireTap ===");
                        err = bridge.EnableWireTap(TIMEOUT_MS);
                        if (err != MCUSerialBridgeError.OK)
                        {
                            Log("EnableWireTap FAILED: {0}", err.ToDescription());
                        }
                        else
                        {
                            Log(
                                "EnableWireTap OK - Port data will now be uploaded even in DIVER mode"
                            );
                            wireTapEnabled = true;
                        }
                    }

                    // Wait 1 second
                    Thread.Sleep(LOOP_DELAY_MS);
                }
                catch (Exception ex)
                {
                    Log("Exception: {0}", ex.Message);
                }
            }

            // Cleanup
            if (bridge != null)
            {
                try
                {
                    bridge.Dispose();
                    Log("MSB Closed");
                }
                catch (Exception ex)
                {
                    Log("Close Error: {0}", ex.Message);
                }
            }

            Log("=== DIVER Mode Test Exited ===");
        }
    }
}
