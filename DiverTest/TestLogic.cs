using CartActivator;

namespace DiverTest
{
    public class TestVehicle : LocalDebugDIVERVehicle
    {
        [AsLowerIO] public int prim;
        [AsLowerIO] public string str;
        [AsLowerIO] public int[] arr;

        [AsUpperIO] public bool prim_b;
        [AsUpperIO] public int[] arr_send = { 1, 2, 3, 4 };

        public int test_shared_var;
    }

// LIOQ
    [LogicRunOnMCU(scanInterval = 50)]
    public class TestLogic : LadderLogic<TestVehicle>
    {
        private Dictionary<int, string> testDict = new();

        interface IFace
        {
            int good(int b);
        }

        public abstract class AClass : IFace
        {
            public int good(int b)
            {
                Console.WriteLine("A-good");
                return 0;
            }

            public virtual int VMethod() => 0;
            public abstract float GG();
            internal IEnumerator<bool> Running;
        }

        private (int a, IFace b) vv = (1, null);

        public class TI : AClass, IFace
        {
            private TestLogic ll;

            public TI(TestLogic ll) 
            {
                this.ll = ll;
            }

            public int good(int b)
            {
                ll.vv.a += 3;
                if (ll.vv.a > 7) ll.vv.a %= 6;
                var zz = (int)GG();
                Console.WriteLine($"A={ll.vv.a}, zz={zz}");
                return ll.vv.a + 4 + zz;
            }

            public override int VMethod() => 7 + ll.test;

            public override float GG()
            {
                Console.WriteLine("GG");
                Running ??= StateMachine().GetEnumerator();
                Running.MoveNext();
                return ll.test = VMethod() + 1;
            }
             
            public IEnumerable<bool> StateMachine()
            {
                var z = ll.vv.a;
                for (int i = 0; i < z + 1; ++i)
                {
                    yield return true;
                }

                Console.WriteLine("Yield done");
                ll.act = (j) =>
                {
                    Console.WriteLine($"j={j}");
                    ll.act = null;
                    return -3;
                };
                Running = null;
            }
        } 

        public TestLogic() 
        { 
            vv.b = new TI(this);
            testDict = new Dictionary<int, string>();
        }

        private Func<int, int> act = null;
        private int test = 3;
        private byte[] cache = new byte[64 * 32];

        [RequireNativeCode]
        static byte[] RenderPattern(byte[] buffer, float bias)
        {
            int width = 64;
            int height = 32;
            int output = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double value = Math.Sin(x * 0.1 + bias) * Math.Cos(y * 0.1 - bias);
                    if (value > 0.3)
                    {
                        buffer[(y / 8) * width + x] |= (byte)(1 << (y % 8));
                    }
                }
            }
            return buffer;
        }

        public override void Operation(int iteration)
        {
            // ============ TEST AsLowerIO/AsUpperIO (Cart read/write) ============
            var ls = new List<int>();
            for (int i = 0; i < Math.Min(iteration,20); ++i)
            {
                ls.Add(i);
                testDict[i % 5] = $"i={i}";
            }

            var arr = ls.Where(p => p % 2 == 1).ToArray();
            cart.arr = arr;
            if (testDict.ContainsKey(vv.a))
            {
                Console.WriteLine($"has {vv.a}");
                cart.str = testDict[vv.a];
            }
            else
            {
                Console.WriteLine($"no {vv.a}");
                cart.str = vv.a + ">" + vv.b.good(vv.a);
            }

            int I(int id)
            {
                if (ls.Count > 100) ls.Clear();
                ls.Add(99); 
                return ls[id % ls.Count] + 100;
            }

            act ??= _ => I(iteration);
            cart.str += "::" + test;
            if (iteration % 3 == 1)
            {
                Console.WriteLine($"iteration={iteration}, GG!");
                testDict[act(iteration) % 100] = $"{new TI(this).GG()}.xxx";
            }

            RunOnMCU.WriteSnapshot(RenderPattern(cache, iteration));
            Console.WriteLine($"{iteration}:arr=[{string.Join(",", arr)}], upload={cart.str}..");
        }
    }
}