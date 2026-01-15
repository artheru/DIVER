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
    }

    /// <summary>
    /// Minimal DIVER test logic - performs simple arithmetic operations
    /// Good for testing basic MCU DIVER runtime functionality:
    /// - Field access
    /// - Basic arithmetic
    /// - Conditional logic
    /// - LowerIO/UpperIO data exchange
    /// - Serial/CAN port read/write
    /// </summary>
    [LogicRunOnMCU(scanInterval = 100)]
    public class TestLogic : LadderLogic<TestVehicle>
    {
        private int _accumulator = 0;
        private float _floatAcc = 0.0f;

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
            // Serial Port Tests
            // ========================================

            // Write to Serial port 3
            byte[] serial3TxData = new byte[] { 0xAA, 0xBB, (byte)(iteration & 0xFF), 0xCC };
            RunOnMCU.WriteStream(serial3TxData, 3);

            // Read from Serial port 3
            byte[] serial3RxData = RunOnMCU.ReadStream(3);
            cart.serial3RxLen = serial3RxData != null ? serial3RxData.Length : 0;
            if (serial3RxData is not null)
            {
                Console.WriteLine($"Serial 3 RX: Received OK!");
            }
            else
            {
                Console.WriteLine($"Serial 3 RX: Received NO DATA!");
            }

            // Read from Serial port 0
            byte[] serial0RxData = RunOnMCU.ReadStream(0);
            cart.serial0RxLen = serial0RxData != null ? serial0RxData.Length : 0;
            if (serial0RxData is not null)
            {
                Console.WriteLine($"Serial 0 RX: Received OK!");
            }
            else
            {
                Console.WriteLine($"Serial 0 RX: Received NO DATA!");
            }

            // ========================================
            // CAN Port Tests (port 4, event_id 0x100)
            // ========================================

            // Write to CAN port 4
            byte[] can4TxData = new byte[] { 0x11, 0x22, 0x33, 0x44, (byte)(iteration & 0xFF) };
            RunOnMCU.WriteEvent(can4TxData, 4, 0x100);

            // Read from CAN port 4
            byte[] can4RxData = RunOnMCU.ReadEvent(4, 0x100);
            cart.can4RxLen = can4RxData != null ? can4RxData.Length : 0;
            if (can4RxData is not null)
            {
                Console.WriteLine($"CAN 4 RX: Received OK!");
            }
            else
            {
                Console.WriteLine($"CAN 4 RX: Received NO DATA!");
            }

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

            byte[] snapshotData = new byte[] { 0xAA, 0x55, 0x00, 0x00 };
            RunOnMCU.WriteSnapshot(snapshotData);

            // Print progress (Console.WriteLine is built-in)
            Console.WriteLine("DOG BARKS: Woof!");
        }
    }
}
