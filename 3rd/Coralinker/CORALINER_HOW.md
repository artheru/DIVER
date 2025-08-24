Coralinker solves the electric connection requirements.

the control architecture is a root ARM/x86 high computing controller running Lin/Win control platform. root connect to a series of node MCU controllers.

root program like:
```
[DefineCoralinking<TestLinking>] // this define the actual topology.
public class TestVehicle : CoralinkerDIVERVehicle
{
}
```

defint a node like:
```
[UseCoralinkerMCU<CoralinkerCL1_0_12p>] // this define what type of node MCU controller used.
public class TestMCURoutineNode2 : LadderLogic<TestVehicle>{
	public override void Operation(int iteration)
}
```

define the topology like:
```
public class TestLinking: Coralinking
{
    public override void Define()
    {
        Console.WriteLine("Coralinker Definition");
        var node1 = Root.Downlink(typeof(TestMCURoutineNode1));
        //var p1= node1.ResolvedPin("battery-12V","input-1"); // denote a pin is forcefully placed.
        //var p2 = node1.UnresolvedPin("gnd");
        //node1.RequireConnect(p1, p2); // todo vargs
        //// .. 
           
        var node2 = node1.Downlink(typeof(TestMCURoutineNode2));
        //.. list all connection here.
    }
}
```
