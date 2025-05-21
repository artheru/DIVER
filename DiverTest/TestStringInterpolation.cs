using System;
using CartActivator;

namespace DiverTest;
  
public class TestVehicle2 : LocalDebugDIVERVehicle
{
    [AsLowerIO] public int read_from_mcu;
    [AsUpperIO] public int write_to_mcu;
}  

[LogicRunOnMCU(scanInterval = 1000)]
public class TestStringInterpolation : LadderLogic<TestVehicle2> 
{ 
    private static int counter = 0; 

    enum ee
    { 
        A, B, C 
    }
    void modify(ref int val, ee xxx)  
    {
        val += 1;
        if (xxx == ee.C) 
            val += 1000;
    }

    private ee vvv; 
    // This method will be processed by our StringInterpolationHandler 
    // String interpolation ($"...") will be converted to String.Format calls
    public override void Operation(int i)
    {
        modify(ref counter, vvv);
        vvv = (ee)(i % 3);

        Console.WriteLine($"p:{(int)vvv}, counter={counter}");
    }
}     