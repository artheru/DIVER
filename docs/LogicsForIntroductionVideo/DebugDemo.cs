using CartActivator;

/// <summary>
/// 调试示例的变量表
/// </summary>
public class DebugCart : CartDefinition
{
    [AsUpperIO] public int X1;
    [AsUpperIO] public int X2;
    [AsLowerIO] public int Y;
}

/// <summary>
/// 示例 3: 调试输出
/// 演示 Console.WriteLine 和 Terminal 面板使用
/// </summary>
[LogicRunOnMCU(scanInterval = 500)]
public class DebugDemo : LadderLogic<DebugCart>
{
    public override void Operation(int iteration)
    {
        // 使用 Console.WriteLine 输出调试信息
        // 输出会显示在 Terminal 面板
        Console.WriteLine($"[iter={iteration}] X1={cart.X1}, X2={cart.X2}, Y={cart.Y}");
        
        // 计算乘法
        cart.Y = cart.X1 * cart.X2;
    }
}
