using CartActivator;
using DiverTest.DIVER.CoralinkerAdaption;

namespace CoralinkerDIVER
{
    public class TestLinking: Coralinking
    {
        public override void Define()
        {
            Console.WriteLine("Coralinker Definition");
            var node1 = Root.Downlink(typeof(TestMCURoutineNode1));
            var battery_48V = node1.GetPin("input-pwr1"); // denote a pin is forcefully placed.
            var batter_Gnd = node1.GetPin("input-pwr2");
            var lidar_12V = node1.Alloc(pin => pin.amp_limit > 5);
            var lidar_gnd = node1.Alloc(pin => pin.amp_limit > 5);
            var Converter_48Vto12V = node1
            node1.RequireConnect(p1, p2); // todo vargs
            //// .. multi

           
            var node2 = node1.Downlink(typeof(TestMCURoutineNode2));
            //.. list all connection here.
        }
    }

    [DefineCoralinking<TestLinking>]
    public class TestVehicle : CoralinkerDIVERVehicle
    {
        [AsLowerIO] public int turn_motor_actual_position_front;
        [AsUpperIO] public int turn_motor_target_position_front;
        public override void Init()
        {
            throw new NotImplementedException();
        }
    }

    // Logic and MCU is strictly 1:1
    [UseCoralinkerMCU<CoralinkerCL1_0_12p>]
    public class TestMCURoutineNode1 : LadderLogic<TestVehicle>
    {
        public override void Operation(int iteration)
        {
        }
    }

    // Logic and MCU is strictly 1:1
    [UseCoralinkerMCU<CoralinkerCL1_0_12p>]
    public class TestMCURoutineNode2 : LadderLogic<TestVehicle>
    {
        public override void Operation(int iteration)
        {
        }
    }
}
