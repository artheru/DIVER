using CartActivator;

/// <summary>
/// 数值计算示例的变量表
/// </summary>
public class NumericCart : CartDefinition
{
    [AsUpperIO] public int X1;
    [AsUpperIO] public int X2;
    [AsLowerIO] public int Y;
}

/// <summary>
/// 示例 2: 数值计算
/// 演示 UpperIO/LowerIO 变量表和 Variables 面板使用
/// 
/// 功能: Y = X1 + X2
/// </summary>
[LogicRunOnMCU(scanInterval = 100)]
public class NumericDemo : LadderLogic<NumericCart>
{
    public override void Operation(int iteration)
    {
        // 简单的加法计算
        // X1, X2 是 UpperIO (Host → MCU)
        // Y 是 LowerIO (MCU → Host)
        cart.Y = cart.X1 + cart.X2;
    }
}
