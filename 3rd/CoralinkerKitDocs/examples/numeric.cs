using CartActivator;

public class NumericCart : CartDefinition
{
    [AsUpperIO] public int X1;
    [AsUpperIO] public int X2;
    [AsLowerIO] public int Y;
}

[LogicRunOnMCU(scanInterval = 100)]
public class NumericDemo : LadderLogic<NumericCart>
{
    public override void Operation(int iteration)
    {
        cart.Y = cart.X1 + cart.X2;
    }
}
