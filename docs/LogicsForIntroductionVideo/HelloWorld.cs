using CartActivator;

/// <summary>
/// 示例 1: Hello World
/// 演示最基本的 DIVER Logic 结构
/// </summary>
[LogicRunOnMCU(scanInterval = 1000)]  // 每 1000ms 执行一次
public class HelloWorld : LadderLogic<CartDefinition>
{
    public override void Operation(int iteration)
    {
        Console.WriteLine($"Hello DIVER! iteration={iteration}");
    }
}
