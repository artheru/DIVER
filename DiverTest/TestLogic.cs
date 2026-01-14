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

            // Print progress (Console.WriteLine is built-in)
            Console.WriteLine(
                "DOG BARKS: Woof!"
            );
        }
    }
}
