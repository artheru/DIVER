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

            public virtual float VMethod() => 0.5f;
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
                return ll.vv.a + 4 + (int)GG();
            }

            public override float VMethod() => 1.5f + ll.test;

            public override float GG()
            {
                Running ??= StateMachine().GetEnumerator();
                Running.MoveNext();
                return ll.test = VMethod() + 1;
            }

            public IEnumerable<bool> StateMachine()
            {
                var z = ll.vv.a;
                for (int i = 0; i < z + 1; ++i)
                    yield return true;
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
        }

        private Func<int, int> act = null;
        private float test = 0;

        public override void Operation(int iteration)
        {
            if (testDict == null) testDict = new Dictionary<int, string>();
            if (vv.b == null) vv.b = new TI(this);
            // ============ TEST AsLowerIO/AsUpperIO (Cart read/write) ============
            var ls = new List<int>();
            for (int i = 0; i < iteration; ++i)
            {
                ls.Add(i);
                testDict[i % 5] = $"i={i}";
            }

            var arr = ls.Where(p => p % 2 == 1).ToArray();
            cart.arr = arr;
            if (testDict.ContainsKey(vv.a))
                cart.str = testDict[vv.a];
            else
                cart.str = vv.a + ">" + vv.b.good(vv.a);

            int I(int id)
            {
                ls.Add(99);
                return ls[id % ls.Count] + 100;
            }

            act ??= _ => I(iteration);
            cart.str += "::" + test;
            if (iteration % 3 == 1) testDict[act(iteration)] = $"{new TI(this).GG()}.xxx";
            Console.WriteLine($"arr=[{string.Join(",", arr)}], upload={cart.str}..");
        }
    }
}