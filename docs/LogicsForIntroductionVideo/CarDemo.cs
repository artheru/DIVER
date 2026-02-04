using CartActivator;

/// <summary>
/// 小车控制示例的变量表
/// </summary>
public class CarCart : CartDefinition
{
    [AsUpperIO] public int joystickX;   // 转向 (-100 ~ 100)
    [AsUpperIO] public int joystickY;   // 油门 (-100 ~ 100)
    
    [AsLowerIO] public int leftRPM;     // 左轮转速
    [AsLowerIO] public int rightRPM;    // 右轮转速
    [AsLowerIO] public int speed;       // 车速
    [AsLowerIO] public int steerAngle;  // 转向角度
}

/// <summary>
/// 示例: 小车控制
/// 演示 Control Panel 遥控器面板使用
/// 
/// 功能: 摇杆控制差速小车
/// - 摇杆 Y 轴: 油门 (前进/后退)
/// - 摇杆 X 轴: 转向 (左转/右转)
/// - 输出: 左右轮转速
/// </summary>
[LogicRunOnMCU(scanInterval = 50)]
public class CarDemo : LadderLogic<CarCart>
{
    public override void Operation(int iteration)
    {
        // 获取摇杆输入
        int throttle = cart.joystickY;   // -100 ~ 100, 正值前进
        int steering = cart.joystickX;   // -100 ~ 100, 正值右转
        
        // 差速转向模型
        // 基础速度由油门决定
        int baseSpeed = throttle * 10;   // 最大 ±1000 RPM
        
        // 转向产生左右轮速度差
        int diff = steering * 5;         // 最大 ±500 RPM 差值
        
        // 计算左右轮速度
        // 右转时: 左轮快, 右轮慢
        // 左转时: 左轮慢, 右轮快
        cart.leftRPM = baseSpeed + diff;
        cart.rightRPM = baseSpeed - diff;
        
        // 输出状态
        cart.speed = throttle;
        cart.steerAngle = steering;
        
        // 每秒打印一次状态
        if (iteration % 20 == 0)
        {
            Console.WriteLine($"Throttle={throttle}, Steering={steering} => L={cart.leftRPM}, R={cart.rightRPM}");
        }
    }
}
