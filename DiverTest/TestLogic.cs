using System;
using CartActivator;

namespace DiverTest;
  
public class TestVehicle : LocalDebugDIVERVehicle
{
    [AsLowerIO] public int read_from_mcu;
    [AsUpperIO] public int write_to_mcu;
}  

[LogicRunOnMCU(scanInterval = 1000)]
public class TestLogic : LadderLogic<TestVehicle> 
{ 
    private static int counter = 0;
     
    private byte[] stest;
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
        if (i == 0)
        {
            stest = new byte[10]; 
        }  
        modify(ref counter, vvv); 
        vvv = (ee)(i % 3);
        stest[i % 10] = (byte)(i*2);

        var vstr = string.Join(" ", stest.Select(p => $"{p:X2}")) + 1032.ToString();

        Console.WriteLine($"p:{(int)vvv}, stest=[{vstr}]");
    }
}        