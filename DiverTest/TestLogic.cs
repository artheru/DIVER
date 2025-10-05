using CartActivator;

namespace DiverTest;


public class TestVehicle : LocalDebugDIVERVehicle
{
    [AsLowerIO] public int prim;
    [AsLowerIO] public string str;
    [AsLowerIO] public int[] arr;

    [AsUpperIO] public bool prim_b;
    [AsUpperIO] public int[] arr_send = [1, 2, 3, 4];

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

    private (int a, IFace b) vv = (1, null);

    public class TI:IFace
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
            return ll.vv.a + 4;
        }
    }

    public TestLogic()
    {
        vv.b = new TI(this);
    }

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
        {
            cart.str = testDict[vv.a];
        }
        else cart.str = "?" + vv.b.good(vv.a);
        Console.WriteLine($"arr=[{string.Join(",", arr)}], upload={cart.str}");
    }
}        