using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CartActivator;
using DiverTest.DIVER.CoralinkerAdaption;
using DiverTest.DIVER.CoralinkerAdaption.SKUs;
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
            var p1= node1.ResolvedPin<A10Pin>("battery-12V","input-1"); // denote a pin is forcefully placed.
            var p2 = node1.ArbitaryPin<A10Pin>("gnd");

            // requirements:
            RequireConnect(p1, p2);

            var node2 = node1.Downlink(typeof(TestMCURoutineNode2));
            var p3 = node2.ArbitaryPin<A10Pin>("lidar-12V");
            var p4 = node2.ArbitaryPin<A10Pin>("lidar-gnd");
            //.. list all connection here.

            RequireConnect(p1, p3);
            RequireConnect(p2, p4);

    
            var node3 = node2.Downlink(typeof(TestMCURoutineNode3));
            var p5 = node3.ArbitaryPin<A10Pin>("motor-24V");
            var p6 = node3.ArbitaryPin<A10Pin>("motor-gnd");

            var node4 = node3.Downlink(typeof(TestMCURoutineNode4));
            var p7 = node4.ArbitaryPin<A10Pin>("DC-12V-to-24V converter in 12V");
            var p8 = node4.ArbitaryPin<A10Pin>("DC-12V-to-24V converter in gnd");
            var p9 = node4.ArbitaryPin<A10Pin>("DC-12V-to-24V converter out 24V");
            var p10 = node4.ArbitaryPin<A10Pin>("DC-12V-to-24V converter out gnd");

            RequireConnect(p5, p9);
            RequireConnect(p6, p10);
            RequireConnect(p1, p7);
            RequireConnect(p2, p8);
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
                Console.WriteLine("Port Serial2 received: " +  readPayload.Length);
            }
        }
    }

    [UseCoralinkerMCU<CoralinkerCL1_0_12p>]
    [LogicRunOnMCU(mcuUri = "serial://name=COMxxx", scanInterval = 500)]
    public class TestMCURoutineNode2 : LadderLogic<TestVehicle>
    {
        public override void Operation(int iteration)
        {
            Console.WriteLine("Iteration = " + iteration + "on node 2");
        }
    }

    [UseCoralinkerMCU<CoralinkerCL1_0_12p>]
    [LogicRunOnMCU(mcuUri = "serial://name=COMxxx", scanInterval = 500)]
    public class TestMCURoutineNode3 : LadderLogic<TestVehicle>
    {
        public override void Operation(int iteration)
        {
            Console.WriteLine("Iteration = " + iteration + "on node 2");
        }
    }

    [UseCoralinkerMCU<CoralinkerCL1_0_12p>]
    [LogicRunOnMCU(mcuUri = "serial://name=COMxxx", scanInterval = 500)]
    public class TestMCURoutineNode4 : LadderLogic<TestVehicle>
    {
        public override void Operation(int iteration)
        {
            Console.WriteLine("Iteration = " + iteration + "on node 2");
        }
    }
}
