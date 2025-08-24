namespace Coralinker_arch;

/// <summary>
/// a super simple 2 node linking test.
/// </summary>

public class TestLinking: Coralinking
{
    public override void Define()
    {
        // basically, all the definitions are  
        Console.WriteLine("Coralinker Definition");
        var node1 = Root.Downlink(typeof(TestNodeMCUController1));
        var battery_48V = node1.GetPin("input-pwr1").SetName("battery_48V");
        var batteryGnd = node1.GetPin("input-pwr2").SetName("battery_Gnd");
        // Alloc can only alloc to "input" cable group.
        var lidar_12V = node1.Alloc(pin => pin.amp_limit > 5).SetName("lidar_12V");
        var lidar_gnd = node1.Alloc(pin => pin.amp_limit > 5).SetName("lidar_gnd");
        var converter_48_to_12V_48in = node1.Alloc().SetName("converter_48in");
        var converter_48_to_12V_12out = node1.Alloc().SetName("converter_12out");
        var converter_48_to_12V_48gnd = node1.Alloc().SetName("converter_48gnd");
        var converter_48_to_12V_12gnd = node1.Alloc().SetName("converter_12gnd");


        var node2 = node1.Downlink(typeof(TestNodeMCUController2));
        var safe_switch_A = node2.Alloc().SetName("safe_switch_A");
        var safe_switch_B = node2.Alloc().SetName("safe_switch_B");
        var motor_48V = node2.Alloc(pin => pin.amp_limit > 40).SetName("motor_48V");
        var motor_GND = node2.Alloc(pin => pin.amp_limit > 40).SetName("motor_GND");

        RequireConnect(lidar_12V, converter_48_to_12V_12out); // todo vargs
        RequireConnect(lidar_gnd, converter_48_to_12V_12gnd);
        RequireConnect(converter_48_to_12V_48in, battery_48V);
        RequireConnect(converter_48_to_12V_48gnd, batteryGnd);
        RequireConnect(battery_48V, safe_switch_A);
        RequireConnect(safe_switch_B, motor_48V);
        RequireConnect(motor_GND, batteryGnd);
    }
}

[DefineCoralinking<TestLinking>]
public class TestRootController
{
}

// Logic and MCU is strictly 1:1
[UseCoralinkerMCU<TeskSKU>]
[LogicRunOnMCU(mcuUri = "serial://com1", scanInterval = 50)]
public class TestNodeMCUController1
{
}

// Logic and MCU is strictly 1:1
[UseCoralinkerMCU<TeskSKU>]
[LogicRunOnMCU(mcuUri = "serial://com2", scanInterval = 50)]
public class TestNodeMCUController2
{
}


public class TestCable : Cable
{
    public WireInstance a100_0, a100_1;
}

public class TeskSKU: CoralinkerNodeDefinition
{
    public override string SKU => "CL1.0-3F3U5I0R3D";

    internal override void define()
    {
        var up0=DefineExtPin(new() { amp_limit = 100, name = "up0" }, ExposedPinCableType.CableUplink);
        var up1=DefineExtPin(new() { amp_limit = 100, name = "up1" }, ExposedPinCableType.CableUplink);
        var pwr1=DefineExtPin(new() { amp_limit = 100, name = "input-pwr1" }, ExposedPinCableType.Input);
        var pwr2=DefineExtPin(new() { amp_limit = 100, name = "input-pwr2" }, ExposedPinCableType.Input);
        var pwr3 = DefineExtPin(new() { amp_limit = 100, name = "input-pwr3" }, ExposedPinCableType.Input);
        var pwr4 = DefineExtPin(new() { amp_limit = 100, name = "input-pwr4" }, ExposedPinCableType.Input);

        var lo0=DefineExtPin(new() { amp_limit = 100, name = "lo0" }, ExposedPinCableType.CableDownlink);
        var lo1=DefineExtPin(new() { amp_limit = 100, name = "lo1" }, ExposedPinCableType.CableDownlink);
        DefineWire(up0, lo0, new WireInstance() { amp_limit = 100 });
        DefineWire(pwr1, lo0, new WireInstance() { amp_limit = 100 });

        DefineSortingNetwork([pwr1, pwr2, pwr3, pwr4, lo1, up1], new() { name = "sn_pwr", 
        swaps = [
            [(0,2,"swap0"),(1,3,"swap0")],
            [(0,1,"swap1"),(2,3,"swap1")],
            [(1,2,"swap2")]
        ], switch_names = ["sw0","sw1","sw2","sw3","sw4","sw5"] });

        var sig1 = DefineExtPin(new() { amp_limit = 10, name = "input-sig1" }, ExposedPinCableType.Input);
        var sig2 = DefineExtPin(new() { amp_limit = 10, name = "input-sig2" }, ExposedPinCableType.Input);
        var sig3 = DefineExtPin(new() { amp_limit = 10, name = "input-sig3" }, ExposedPinCableType.Input);
        var sig4 = DefineExtPin(new() { amp_limit = 10, name = "input-sig4" }, ExposedPinCableType.Input);
        DefineWire(sig1, sig2, new WireInstance() { amp_limit = 100 });
        DefineWire(sig3, sig4, new WireInstance() { amp_limit = 10 });

        DefineSortingNetwork([sig1, sig2, sig3, sig4], new() { name = "sn_sig", swaps = [
            [(0,1,"swap0")],
        ], switch_names = ["sw0","sw1","sw2","sw3"] });

        DefineCable<TestCable>(ExposedPinCableType.CableUplink, cable =>
        {
            cable.a100_0 = new WireInstance();
            cable.a100_1 = new WireInstance();
            cable.a100_0.Connect(up0);
            cable.a100_1.Connect(up1);
        });
        
        DefineCable<TestCable>(ExposedPinCableType.CableDownlink, cable =>
        {
            cable.a100_0 = new WireInstance();
            cable.a100_1 = new WireInstance();
            cable.a100_0.Connect(lo0);
            cable.a100_1.Connect(lo1);
        });
    }

}

// note: the above case has a solution:
// lidar_12V -> node1.inpug_sig1
// lidar_gnd -> node1.input_sig2
// converter_48_to_12V_48in -> node1.input_pwr4
// converter_48_to_12V_48gnd -> node1.input_pwr3
// battery_48V -> node1.input_pwr2
// batteryGnd -> node1.input_pwr1
// converter_48_to_12V_12out -> node1.input_sig3
// converter_48_to_12V_12gnd -> node1.input_sig4
// safe_switch_A -> node2.input_pwr1
// safe_switch_B -> node2.input_pwr2
// motor_48V -> node2.input_pwr3
// motor_GND -> node2.input_pwr4