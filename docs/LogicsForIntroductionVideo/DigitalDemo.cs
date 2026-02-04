using CartActivator;

/// <summary>
/// 数字输出控制示例的变量表
/// </summary>
public class DigitalCart : CartDefinition
{
    [AsUpperIO] public bool ledEnable;      // 控制 LED 开关
    [AsUpperIO] public int blinkInterval;   // 闪烁间隔 (ms)
}

/// <summary>
/// 示例: 数字输出控制
/// 演示 WriteSnapshot 控制数字 IO
/// 
/// 功能: 可配置的 LED 闪烁
/// - ledEnable: 启用/禁用闪烁
/// - blinkInterval: 闪烁间隔 (毫秒)
/// </summary>
[LogicRunOnMCU(scanInterval = 50)]
public class DigitalDemo : LadderLogic<DigitalCart>
{
    private int timer = 0;
    private bool ledState = false;
    
    public override void Operation(int iteration)
    {
        if (cart.ledEnable && cart.blinkInterval > 0)
        {
            timer += 50;  // 扫描周期 50ms
            if (timer >= cart.blinkInterval)
            {
                timer = 0;
                ledState = !ledState;
                
                // 写入数字输出，端口 0，位 0
                WriteSnapshot(ledState ? 1 : 0, 0);
                Console.WriteLine($"LED = {ledState}");
            }
        }
        else
        {
            WriteSnapshot(0, 0);  // 关闭 LED
            timer = 0;
        }
    }
}
