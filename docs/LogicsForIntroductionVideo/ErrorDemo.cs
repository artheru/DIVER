using CartActivator;

/// <summary>
/// 示例: 错误处理
/// 演示运行时错误诊断和源码定位
/// 
/// 功能: 故意制造数组越界错误
/// 运行约 8 秒后会触发错误弹窗，点击可跳转到出错的源码行
/// </summary>
[LogicRunOnMCU(scanInterval = 1000)]
public class ErrorDemo : LadderLogic<CartDefinition>
{
    // 定义一个长度为 8 的数组
    private byte[] buffer = new byte[8];
    
    // 索引变量，每次递增
    private int index = 0;
    
    public override void Operation(int iteration)
    {
        // 打印当前索引
        Console.WriteLine($"index = {index}");
        
        // 写入数组
        // 当 index >= 8 时，这行会触发 Array Index Out of Bounds 错误
        buffer[index] = (byte)iteration;  // <-- 错误发生在这里
        
        // 递增索引
        index++;
        
        // 注意: 这个 Logic 故意不检查边界
        // 目的是演示 DIVER 的错误诊断功能
    }
}
