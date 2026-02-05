using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using MCUSerialBridgeCLR;

namespace MCUTest
{
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
                $"[{st.wHour:D2}:{st.wMinute:D2}:{st.wSecond:D2}.{st.wMilliseconds:D3}] C# Test    | {string.Format(fmt, args)}"
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

            var bridge = new MCUSerialBridge();
            var err = bridge.Open("COM18", 1000000u);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB Open FAILED: {0}", err.ToDescription());
                return;
            }
            else
            {
                Log("MSB Open OK");
            }

            err = bridge.Reset(200);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB Reset FAILED: {0}", err.ToDescription());
                return;
            }
            else
            {
                Thread.Sleep(500);
                Log("MSB Reset OK");
            }

            err = bridge.GetVersion(out var version, 100);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB GetVersion FAILED: {0}", err.ToDescription());
                return;
            }
            else
            {
                Log("MSB GetVersion OK: {0}", version.ToString());
            }

            err = bridge.GetLayout(out var layout, 100);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB GetLayout FAILED: {0}", err.ToDescription());
                return;
            }
            else
            {
                Log("MSB GetLayout OK: {0}", layout.ToString());
                var validPorts = layout.GetValidPorts();
                for (int i = 0; i < validPorts.Length; i++)
                {
                    Log("  Port[{0}]: {1}", i, validPorts[i].ToString());
                }
            }

            err = bridge.GetState(out var state, 100);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB GetState FAILED: {0}", err.ToDescription());
                return;
            }
            else
            {
                Log("MSB GetState OK: {0}", state.ToString());
            }

            // 配置端口
            var ports = new List<PortConfig>();
            for (int i = 0; i < 4; i++)
                ports.Add(new SerialPortConfig(115200, 0));
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
            Log("=========================");

            // 调用 MSB 配置
            var ret = bridge.Configure(ports, 200);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB Configure FAILED: {0}", err.ToDescription());
                return;
            }
            else
            {
                Log("MSB Configure OK");
            }
            Thread.Sleep(100);
            err = bridge.GetState(out state, 100);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB GetState FAILED: {0}", err.ToDescription());
                return;
            }
            else
            {
                Log("MSB GetState OK: {0}", state.ToString());
            }

            // 启动 MCU
            err = bridge.Start();
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB Start FAILED: {0}", err.ToDescription());
                return;
            }
            else
            {
                Log("MSB Start OK");
            }

            err = bridge.GetState(out state, 100);
            if (err != MCUSerialBridgeError.OK)
            {
                Log("MSB GetState FAILED: {0}", err.ToDescription());
                return;
            }
            else
            {
                Log("MSB GetState OK: {0}", state.ToString());
            }
            Thread.Sleep(100);

            // 注册回调函数，当 Port3 (RS232-1) 收到数据的时候，会自动调用下面的回调
            // 回调参数：(portIndex, direction, data) - direction: 0=RX, 1=TX
            err = bridge.RegisterSerialPortCallback(
                BKBoardPortIndex.RS232_1,
                (portIndex, direction, data) =>
                {
                    string dir = direction == 0 ? "RX" : "TX";
                    Log("Callback Serial Port{0} {1}: {2}", portIndex, dir, BitConverter.ToString(data));
                }
            );
            // 注册回调函数，当 Port5 (CAN2) 收到数据的时候，会自动调用下面的回调
            // 回调参数：(portIndex, direction, canMessage) - direction: 0=RX, 1=TX
            err = bridge.RegisterCANPortCallback(
                BKBoardPortIndex.CAN2,
                (portIndex, direction, canMessage) =>
                {
                    string dir = direction == 0 ? "RX" : "TX";
                    Log("Callback CAN Port{0} {1}: {2}", portIndex, dir, canMessage.ToString());
                }
            );

            Log("=== Main Loop Start ===");

            const int STEP_DELAY_MS = 100;
            const int TIMEOUT_MS = 50;

            uint ioStep = 0;
            byte canIdBase = 10;

            while (gRunning)
            {
                try
                {
                    // 1. IO 流水灯写
                    uint ioValue = 1u << (int)ioStep;
                    byte[] ioBuf = BitConverter.GetBytes(ioValue);
                    Log("IO Write bit -> Started");
                    var ioErr = bridge.WriteOutput(ioBuf, TIMEOUT_MS);
                    Log(
                        "IO Write bit {0} (0x{1:X4}) -> {2}",
                        ioStep,
                        ioValue,
                        ioErr == MCUSerialBridgeError.OK ? "OK" : "FAILED"
                    );

                    // 立即读取 IO 输入（与 C 版本保持完全一致的打印顺序）
                    byte[] ioReadBuf = new byte[4];
                    var readErr = bridge.ReadInput(out ioReadBuf, TIMEOUT_MS);
                    if (readErr == MCUSerialBridgeError.OK)
                    {
                        uint ioReadValue = BitConverter.ToUInt32(ioReadBuf, 0);

                        Log("IO Read  raw value: 0x{0:X8}", ioReadValue);

                        // 构建低 16 bit 字符串：从低位到高位，每 8 bit 一组，用空格分隔
                        char[] bitStr = new char[32];
                        int pos = 0;
                        for (int bit = 0; bit < 16; bit++)
                        {
                            bitStr[pos++] = ((ioReadValue & (1u << bit)) != 0) ? '1' : '0';
                            if ((bit % 8 == 7) && (bit != 15))
                            {
                                bitStr[pos++] = ' ';
                            }
                        }

                        Log("IO Read  bits(0-15): {0}", new string(bitStr, 0, pos));
                    }
                    else
                    {
                        Log("IO Read FAILED");
                    }

                    Thread.Sleep(STEP_DELAY_MS);

                    // 2. RS232-1 自发自收
                    byte[] testData = new byte[32];
                    for (int i = 0; i < 32; i++)
                        testData[i] = (byte)(0x30 + (i % 10));

                    Log("Write Serial Port 3(RS232-1) (32 bytes) Started");
                    var w3Err = bridge.WriteSerial(BKBoardPortIndex.RS232_1, testData, TIMEOUT_MS);
                    Log(
                        "Write Serial Port 3(RS232-1) (32 bytes) -> {0}",
                        w3Err == MCUSerialBridgeError.OK ? "OK" : "FAILED"
                    );

                    // Serial 3(RS232-1) 的接收已经放在了回调方式中
                    Thread.Sleep(STEP_DELAY_MS);

                    // 3. Serial 0 (RS485-1) → Serial 1 (RS485-2)
                    Log("Write Serial Port 0(RS485-1) (32 bytes) Started");
                    var w0Err = bridge.WriteSerial(BKBoardPortIndex.RS485_1, testData, TIMEOUT_MS);
                    Log(
                        "Write Serial Port 0(RS485-1) (32 bytes) -> {0}",
                        w0Err == MCUSerialBridgeError.OK ? "OK" : "FAILED"
                    );

                    // 阻塞方式接收示例
                    byte[] recv1 = null;
                    var r1Err = bridge.ReadSerial(BKBoardPortIndex.RS485_3, out recv1, TIMEOUT_MS);
                    if (r1Err == MCUSerialBridgeError.OK && recv1 != null)
                    {
                        string text = System.Text.Encoding.ASCII.GetString(recv1);
                        Log("Read Serial Port 1(RS485-2) SUCCESS");
                        Log("Received hex  : {0}", BitConverter.ToString(recv1));
                    }
                    else
                    {
                        Log("Read Serial Port 1(RS485-2) FAILED or No Data");
                    }

                    Thread.Sleep(STEP_DELAY_MS);

                    // 4. CAN Port4 (CAN1)
                    CANMessage canSend = new CANMessage
                    {
                        ID = canIdBase,
                        RTR = false,
                        Payload = new byte[8],
                    };
                    for (int i = 0; i < 8; i++)
                        canSend.Payload[i] = (byte)(canIdBase + i + 1);

                    var canWErr = bridge.WriteCAN(4, canSend, TIMEOUT_MS);
                    Log(
                        "Write CAN Port 4 (CAN1) with {0}, result {1}",
                        canSend,
                        canWErr.ToDescription()
                    );

                    // CAN Port5 (CAN2) 的接收已经放在了回调方式中
                    canIdBase++;
                }
                catch (Exception ex)
                {
                    Log("Exception: {0}", ex.Message);
                }

                if (++ioStep >= 14)
                    ioStep = 0;

                Thread.Sleep(STEP_DELAY_MS);
            }

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

            Log("Program exited");
        }
    }
}
