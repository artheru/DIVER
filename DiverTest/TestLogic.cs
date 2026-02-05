using CartActivator;

namespace DiverTest
{
    // Minimal Cart definition for simple testing
    public class TestVehicle : LocalDebugDIVERVehicle
    {
        // LowerIO: MCU -> PC (these values are sent back from MCU)
        [AsLowerIO]
        public int counter;

        [AsLowerIO]
        public int result;

        [AsLowerIO]
        public float computed;

        [AsLowerIO]
        public int serial0RxLen;

        [AsLowerIO]
        public int serial3RxLen;

        [AsLowerIO]
        public int can4RxLen;

        // UpperIO: PC -> MCU (these values are sent to MCU)
        [AsUpperIO]
        public int inputA;

        [AsUpperIO]
        public int inputB;

        [AsUpperIO]
        public int digital_output;
    }

    /// <summary>
    /// Minimal DIVER test logic - performs simple arithmetic operations
    /// Good for testing basic MCU DIVER runtime functionality:
    /// - Field access
    /// - Basic arithmetic
    /// - Conditional logic
    /// - LowerIO/UpperIO data exchange
    /// - Serial/CAN port read/write (CANOpen + Modbus RTU protocols)
    /// </summary>
    [LogicRunOnMCU(scanInterval = 100)]
    public class TestLogic : LadderLogic<TestVehicle>
    {
        private int _accumulator = 0;
        private float _floatAcc = 0.0f;
        private int _msgCounter = 0; // Counter for cycling through different message types

        // Modbus CRC16 calculation
        private ushort CalcModbusCRC(byte[] data, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc = (ushort)(crc >> 1);
                }
            }
            return crc;
        }

        public override void Operation(int iteration)
        {
            // Increment counter each iteration
            cart.counter = iteration;

            // Simple arithmetic with UpperIO inputs
            cart.result = cart.inputA + cart.inputB + iteration;

            // Accumulate values
            _accumulator += iteration;
            if (_accumulator > 100)
                _accumulator = 0;

            // Float computation
            _floatAcc = (float)iteration * 0.5f + (float)cart.inputA * 0.25f;
            cart.computed = _floatAcc;

            // Simple conditional
            if (iteration % 5 == 0)
            {
                cart.result *= 2;
            }

            // ========================================
            // Protocol Tests (every 10 iterations)
            // CANOpen on CAN port 4, Modbus RTU on Serial port 0
            // ========================================

            // Send messages every 10 iterations
            if (iteration % 10 == 0)
            {
                _msgCounter++;
                int msgType = _msgCounter % 6; // Cycle through 6 different message types

                // ========================================
                // CANOpen Messages on CAN port 4
                // ========================================
                byte nodeId = 0x01; // Target node ID

                switch (msgType)
                {
                    case 0:
                        // NMT: Start Remote Node (COB-ID = 0x000)
                        RunOnMCU.WriteCANMessage(4, new CANMessage
                        {
                            ID = 0x000,
                            RTR = false,
                            Payload = new byte[] { 0x01, nodeId } // CS=0x01 (Start), Node=1
                        });
                        break;

                    case 1:
                        // NMT: Enter Pre-Operational (COB-ID = 0x000)
                        RunOnMCU.WriteCANMessage(4, new CANMessage
                        {
                            ID = 0x000,
                            RTR = false,
                            Payload = new byte[] { 0x80, nodeId } // CS=0x80 (Pre-Op), Node=1
                        });
                        break;

                    case 2:
                        // Heartbeat (COB-ID = 0x700 + node_id)
                        // States: 0x00=Boot-up, 0x04=Stopped, 0x05=Operational, 0x7F=Pre-op
                        RunOnMCU.WriteCANMessage(4, new CANMessage
                        {
                            ID = (ushort)(0x700 + nodeId),
                            RTR = false,
                            Payload = new byte[] { 0x05 } // Operational state
                        });
                        break;

                    case 3:
                        // Emergency (COB-ID = 0x080 + node_id)
                        // Format: [ErrorCode_Lo, ErrorCode_Hi, ErrorRegister, ManufacturerSpecific...]
                        RunOnMCU.WriteCANMessage(4, new CANMessage
                        {
                            ID = (ushort)(0x080 + nodeId),
                            RTR = false,
                            Payload = new byte[] { 0x10, 0x81, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 }
                            // Error 0x8110 = CAN overrun, ErrorReg=0x01
                        });
                        break;

                    case 4:
                        // SDO Upload Request (Read) (COB-ID = 0x600 + node_id)
                        // Read Statusword (Index 0x6041, SubIndex 0x00)
                        RunOnMCU.WriteCANMessage(4, new CANMessage
                        {
                            ID = (ushort)(0x600 + nodeId),
                            RTR = false,
                            Payload = new byte[] { 0x40, 0x41, 0x60, 0x00, 0x00, 0x00, 0x00, 0x00 }
                            // CCS=0x40 (Initiate Upload), Index=0x6041, SubIndex=0x00
                        });
                        break;

                    case 5:
                        // SDO Download Request (Write) (COB-ID = 0x600 + node_id)
                        // Write Controlword (Index 0x6040, SubIndex 0x00) with value 0x000F
                        RunOnMCU.WriteCANMessage(4, new CANMessage
                        {
                            ID = (ushort)(0x600 + nodeId),
                            RTR = false,
                            Payload = new byte[] { 0x2B, 0x40, 0x60, 0x00, 0x0F, 0x00, 0x00, 0x00 }
                            // CCS=0x2B (Expedited, 2 bytes), Index=0x6040, SubIndex=0x00, Data=0x000F
                        });
                        break;
                }

                // ========================================
                // Modbus RTU Messages on Serial port 0
                // ========================================
                byte slaveAddr = 0x01;
                byte[] modbusFrame;

                // Alternate between different Modbus function codes
                int modbusType = _msgCounter % 4;

                switch (modbusType)
                {
                    case 0:
                        // Read Holding Registers (FC 0x03)
                        // Read 10 registers starting at address 0x0000
                        modbusFrame = new byte[8];
                        modbusFrame[0] = slaveAddr;
                        modbusFrame[1] = 0x03; // Function code
                        modbusFrame[2] = 0x00; // Start addr high
                        modbusFrame[3] = 0x00; // Start addr low
                        modbusFrame[4] = 0x00; // Quantity high
                        modbusFrame[5] = 0x0A; // Quantity low (10 registers)
                        {
                            ushort crc = CalcModbusCRC(modbusFrame, 6);
                            modbusFrame[6] = (byte)(crc & 0xFF);
                            modbusFrame[7] = (byte)(crc >> 8);
                        }
                        RunOnMCU.WriteStream(modbusFrame, 0);
                        break;

                    case 1:
                        // Read Input Registers (FC 0x04)
                        // Read 5 registers starting at address 0x0010
                        modbusFrame = new byte[8];
                        modbusFrame[0] = slaveAddr;
                        modbusFrame[1] = 0x04; // Function code
                        modbusFrame[2] = 0x00; // Start addr high
                        modbusFrame[3] = 0x10; // Start addr low (16)
                        modbusFrame[4] = 0x00; // Quantity high
                        modbusFrame[5] = 0x05; // Quantity low (5 registers)
                        {
                            ushort crc = CalcModbusCRC(modbusFrame, 6);
                            modbusFrame[6] = (byte)(crc & 0xFF);
                            modbusFrame[7] = (byte)(crc >> 8);
                        }
                        RunOnMCU.WriteStream(modbusFrame, 0);
                        break;

                    case 2:
                        // Write Single Register (FC 0x06)
                        // Write value 0x1234 to register 0x0001
                        modbusFrame = new byte[8];
                        modbusFrame[0] = slaveAddr;
                        modbusFrame[1] = 0x06; // Function code
                        modbusFrame[2] = 0x00; // Register addr high
                        modbusFrame[3] = 0x01; // Register addr low
                        modbusFrame[4] = 0x12; // Value high
                        modbusFrame[5] = 0x34; // Value low
                        {
                            ushort crc = CalcModbusCRC(modbusFrame, 6);
                            modbusFrame[6] = (byte)(crc & 0xFF);
                            modbusFrame[7] = (byte)(crc >> 8);
                        }
                        RunOnMCU.WriteStream(modbusFrame, 0);
                        break;

                    case 3:
                        // Write Multiple Registers (FC 0x10)
                        // Write 3 registers starting at address 0x0010
                        modbusFrame = new byte[15]; // 7 header + 6 data + 2 CRC
                        modbusFrame[0] = slaveAddr;
                        modbusFrame[1] = 0x10; // Function code
                        modbusFrame[2] = 0x00; // Start addr high
                        modbusFrame[3] = 0x10; // Start addr low (16)
                        modbusFrame[4] = 0x00; // Quantity high
                        modbusFrame[5] = 0x03; // Quantity low (3 registers)
                        modbusFrame[6] = 0x06; // Byte count (3 regs * 2 bytes)
                        // Register values (big-endian)
                        modbusFrame[7] = 0x00; modbusFrame[8] = 0x0A;   // Reg 0: 10
                        modbusFrame[9] = 0x01; modbusFrame[10] = 0x02;  // Reg 1: 258
                        modbusFrame[11] = 0x00; modbusFrame[12] = (byte)(iteration & 0xFF); // Reg 2: iteration
                        {
                            ushort crc = CalcModbusCRC(modbusFrame, 13);
                            modbusFrame[13] = (byte)(crc & 0xFF);
                            modbusFrame[14] = (byte)(crc >> 8);
                        }
                        RunOnMCU.WriteStream(modbusFrame, 0);
                        break;
                }

                // Every 100 iterations, send a longer Modbus frame (Write Multiple Registers with more data)
                if (iteration % 100 == 0)
                {
                    // Write 64 registers (128 bytes of data) - larger frame for testing
                    int regCount = 64;
                    modbusFrame = new byte[7 + regCount * 2 + 2]; // header + data + CRC
                    modbusFrame[0] = slaveAddr;
                    modbusFrame[1] = 0x10; // Function code
                    modbusFrame[2] = 0x00; // Start addr high
                    modbusFrame[3] = 0x00; // Start addr low
                    modbusFrame[4] = 0x00; // Quantity high
                    modbusFrame[5] = (byte)regCount; // Quantity low
                    modbusFrame[6] = (byte)(regCount * 2); // Byte count
                    // Fill register data
                    for (int i = 0; i < regCount; i++)
                    {
                        modbusFrame[7 + i * 2] = (byte)(i >> 8);
                        modbusFrame[7 + i * 2 + 1] = (byte)(i & 0xFF);
                    }
                    {
                        ushort crc = CalcModbusCRC(modbusFrame, 7 + regCount * 2);
                        modbusFrame[7 + regCount * 2] = (byte)(crc & 0xFF);
                        modbusFrame[7 + regCount * 2 + 1] = (byte)(crc >> 8);
                    }
                    RunOnMCU.WriteStream(modbusFrame, 0);
                }
            }

            // Read from Serial port 3
            byte[] serial3RxData = RunOnMCU.ReadStream(3);
            cart.serial3RxLen = serial3RxData != null ? serial3RxData.Length : 0;

            // Read from Serial port 0
            byte[] serial0RxData = RunOnMCU.ReadStream(0);
            cart.serial0RxLen = serial0RxData != null ? serial0RxData.Length : 0;

            // Read from CAN port 4 using CANMessage
            CANMessage can4Rx = RunOnMCU.ReadCANMessage(4, 0x100);
            cart.can4RxLen = can4Rx?.DLC ?? 0;

            // ========================================
            // Snapshot (Input) Test
            // ========================================

            // Read input snapshot
            byte[] snapshot = RunOnMCU.ReadSnapshot();
            if (snapshot != null && snapshot.Length >= 4)
            {
                // Use first 4 bytes as an int for testing
                cart.result += snapshot[0];
            }

            byte[] snapshotData = BitConverter.GetBytes(cart.digital_output);
            RunOnMCU.WriteSnapshot(snapshotData);

            // Print progress (Console.WriteLine is built-in)
            Console.WriteLine("DOG BARKS: Woof!");
        }
    }
}
