using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CartActivator;
using TEST;

namespace DiverTest
{
    // interaction interface.
    public class TestVehicle : LocalDIVERVehicle
    {
        [AsLowerIO] public int read_from_mcu;
        [AsUpperIO] public int write_to_mcu;
    }

    [LogicRunOnMCU(scanInterval = 50)]
    public class TestMCURoutine: LadderLogic<TestVehicle>
    {
        public override void Operation(int iteration)
        {
            Console.WriteLine("Hello world iter=" + iteration);
            cart.read_from_mcu = (cart.write_to_mcu << 2) + TESTCls.TestFunc(iteration);
        }
    }
}
