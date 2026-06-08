using CartActivator;

namespace DiverBench
{
    // Telemetry/CPU benchmark vehicle.
    //   LowerIO  (MCU -> PC): values we read back to confirm the program runs.
    //   UpperIO  (PC -> MCU): a knob to scale the per-iteration workload at runtime.
    public class BenchVehicle : LocalDebugDIVERVehicle
    {
        // STABLE: depends only on `rounds`, so it is the SAME value every cycle and
        // MUST be identical on the non-CCM (v2.1) and the CCM build -> correctness check.
        [AsLowerIO] public int checksum;
        // This one legitimately changes every cycle (it is just the loop index echo).
        [AsLowerIO] public int iteration;
        // Inner operations performed this cycle = effRounds * 256.
        [AsLowerIO] public int workUnits;
        // The number of rounds actually used (so the default is not a mystery).
        [AsLowerIO] public int effRounds;

        [AsUpperIO] public int rounds; // 0 => default (8). Increase to make each cycle heavier.
    }

    /// <summary>
    /// Deterministic CPU benchmark for measuring DIVER VM cost per cycle.
    ///
    /// Each cycle: re-seed a 256-int buffer from a FIXED seed, then run `rounds`
    /// passes of "array read + read + Mix() + write" over it. This hammers the
    /// exact structures the CCM optimization targets — the heap object table
    /// (array element access) and the call stack (the Mix() helper).
    ///
    /// Because the seed is constant, `checksum` is a pure function of `rounds`:
    ///   - it is the SAME value every cycle (so it should look stable on screen), and
    ///   - it MUST match between the non-CCM and CCM firmware (proves CCM didn't
    ///     change behaviour).
    /// The cost per cycle is proportional to `rounds`, so the telemetry cycle/us
    /// numbers move predictably and are comparable between builds.
    /// </summary>
    [LogicRunOnMCU(scanInterval = 50)]
    public class BenchLogic : LadderLogic<BenchVehicle>
    {
        private const int BUF_LEN = 256;      // power of two so we can mask instead of mod
        private const int DEFAULT_ROUNDS = 8; // workload when the PC hasn't set `rounds`

        // Heap-allocated scratch buffer, allocated once by the static ctor (.cctor),
        // re-seeded from a constant every cycle (no cross-cycle state).
        private static int[] _buf = new int[BUF_LEN];

        // xorshift mixer: cheap pure-integer ops, forces a method call (stack frame) each use.
        private static int Mix(int x)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }

        public override void Operation(int it)
        {
            cart.iteration = it;

            int rounds = cart.rounds;
            if (rounds <= 0) rounds = DEFAULT_ROUNDS;
            if (rounds > 1000) rounds = 1000; // safety clamp so we never blow the scan interval
            cart.effRounds = rounds;

            // Deterministic re-seed from a constant (cheap LCG fill, no method call).
            int s = 0x12345678;
            for (int i = 0; i < BUF_LEN; i++)
            {
                s = s * 1103515245 + 12345;
                _buf[i] = s;
            }

            // The measured workload (the CCM hot path): heap array access + method calls.
            int acc = 0;
            for (int r = 0; r < rounds; r++)
            {
                for (int i = 0; i < BUF_LEN; i++)
                {
                    int j = (i * 7 + r) & (BUF_LEN - 1);
                    int v = _buf[i] * 31 + _buf[j]; // 2 heap array reads
                    v = Mix(v);                     // method call -> stack frame
                    _buf[i] = v;                    // heap array write
                    acc += v;
                }
            }

            cart.checksum = acc;                 // constant for a given `rounds`
            cart.workUnits = rounds * BUF_LEN;
        }
    }
}
