using CartActivator;

[LogicRunOnMCU(scanInterval = 1000)]
public class HelloWorld : LadderLogic<CartDefinition>
{
    public override void Operation(int iteration)
    {
        Console.WriteLine($"Hello DIVER! iteration={iteration}");
    }
}
