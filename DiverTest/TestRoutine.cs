using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CartActivator;
using DiverTest.DIVER.CoralinkerAdaption;
using TEST;

namespace DiverTest
{
    // interaction interface.
    public class TestLinking: Coralinking
    {
        public override void Define()
        {
            Console.WriteLine("Coralinker Definition");
            var node1 = Root.Downlink(typeof(TestMCURoutine));
            var p1= node1.ResolvedPin("battery-12V","input-1"); // denote a pin is forcefully placed.
            var p2 = node1.UnresolvedPin("gnd");
            node1.RequireConnect(p1, p2);

            //var node2 = node1.Downlink(typeof(TestMCURoutineNode2));
            //.. list all connection here.
        }
    }

    [DefineCoralinking<TestLinking>]
    public class TestVehicle : CoralinkerDIVERVehicle
    {
        [AsLowerIO] public int read_from_mcu;
        [AsUpperIO] public int write_to_mcu;
    }

    // Logic and MCU is strictly 1:1
    [UseCoralinkerMCU<CoralinkerCL1_0_12p>]
    [LogicRunOnMCU(mcuUri="serial://name=COM4", scanInterval = 500)]
    public class TestMCURoutine: LadderLogic<TestVehicle>
    {
        public override void Operation(int iteration)
        {
            Console.WriteLine("Iteration = " + iteration);
            cart.read_from_mcu = (cart.write_to_mcu << 2) + TESTCls.TestFunc(iteration);
            Console.WriteLine("Lower " + cart.read_from_mcu);
            Console.WriteLine("Upper " + cart.write_to_mcu);
            Console.WriteLine("Time = " + RunOnMCU.GetMillisFromStart());
            byte[] dummyPayload = new byte[4] { 0x01, 0x02, 0x03, 0x04};
            RunOnMCU.WriteStream(dummyPayload, (int)CoralinkerDIVERVehicle.PortIndex.Serial1);
            byte[] readPayload = RunOnMCU.ReadStream((int)CoralinkerDIVERVehicle.PortIndex.Serial2);
            if (readPayload != null)
            {
                Console.WriteLine("Port Serial2 received: " + readPayload.Length);
            }
        }
    }

    //[UseCoralinkerMCU<CoralinkerCL1_0_12p>]
    //[LogicRunOnMCU(mcuUri = "serial://name=COMxxx", scanInterval = 500)]
    //public class TestMCURoutineNode2 : LadderLogic<TestVehicle>
    //{
    //    public override void Operation(int iteration)
    //    {
    //        Console.WriteLine("Iteration node 2 = " + iteration);
    //    }
    //}
}
