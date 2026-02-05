using CartActivator;

namespace DiverTest
{
    /// <summary>
    /// 遥控车辆数据定义
    /// 用于演示遥控操纵功能，包含摇杆输入和车辆状态反馈
    /// </summary>
    public class RemoteControlVehicle : LocalDebugDIVERVehicle
    {
        // ========================================
        // UpperIO: PC -> MCU (遥控输入)
        // ========================================

        /// <summary>
        /// 摇杆前后轴 (油门/刹车)
        /// 范围: -1 到 1
        /// -1 = 全速倒车, 0 = 空挡, 1 = 全速前进
        /// </summary>
        [AsUpperIO]
        public float joystickForwardBack;

        /// <summary>
        /// 摇杆左右轴 (转向)
        /// 范围: -1 到 1
        /// -1 = 左满舵, 0 = 居中, 1 = 右满舵
        /// </summary>
        [AsUpperIO]
        public float joystickLeftRight;

        /// <summary>
        /// 车辆使能开关
        /// true = 允许车辆运行, false = 车辆停止
        /// </summary>
        [AsUpperIO]
        public bool vehicleOn;

        // ========================================
        // LowerIO: MCU -> PC (车辆状态反馈)
        // ========================================

        /// <summary>
        /// 车轮实际转速 (RPM)
        /// 范围: -3000 到 3000
        /// 正值表示前进，负值表示倒车
        /// </summary>
        [AsLowerIO]
        public float actualSpeedRPM;

        /// <summary>
        /// 舵轮实际角度 (度)
        /// 范围: -30 到 30
        /// 负值表示左转，正值表示右转
        /// </summary>
        [AsLowerIO]
        public float actualSteeringAngle;

        /// <summary>
        /// 超速警告标志
        /// 当车轮转速绝对值超过 1500 RPM 时为 true
        /// </summary>
        [AsLowerIO]
        public bool overspeedWarning;
    }

    /// <summary>
    /// 遥控车辆仿真逻辑
    /// 
    /// 模拟简单的车辆动力学模型，用于演示遥控操纵功能:
    /// - 摇杆前后控制油门，慢慢改变车轮转速
    /// - 摇杆左右控制舵轮角度
    /// - 车辆关闭时自动减速停车并回正舵轮
    /// 
    /// 车辆参数:
    /// - 最大转速: 3000 RPM
    /// - 加速性能: 0 到 3000 RPM 约 10 秒 (加速度 300 RPM/s)
    /// - 舵轮范围: -30° 到 +30°
    /// - 打舵速度: 10°/秒
    /// - 扫描周期: 100ms
    /// </summary>
    [LogicRunOnMCU(scanInterval = 100)]
    public class TestLogic2 : LadderLogic<RemoteControlVehicle>
    {
        // 车辆参数
        private const float MAX_SPEED_RPM = 3000.0f;        // 最大转速 (RPM)
        private const float MAX_STEERING_ANGLE = 30.0f;     // 最大舵轮角度 (度)
        private const float OVERSPEED_THRESHOLD = 1500.0f;  // 超速警告阈值 (RPM)

        // 变化速率 (每 100ms 扫描周期)
        // 加速度: 3000 RPM / 10s = 300 RPM/s = 30 RPM / 100ms
        private const float SPEED_CHANGE_PER_SCAN = 30.0f;
        // 打舵速度: 10°/s = 1° / 100ms
        private const float STEERING_CHANGE_PER_SCAN = 1.0f;
        // 关闭时减速更快: 600 RPM/s
        private const float DECEL_RATE_PER_SCAN = 60.0f;

        public override void Operation(int iteration)
        {
            // 限制摇杆输入范围
            float throttle = Clamp(cart.joystickForwardBack, -1.0f, 1.0f);
            float steering = Clamp(cart.joystickLeftRight, -1.0f, 1.0f);

            if (cart.vehicleOn)
            {
                // ========================================
                // 速度控制 (油门)
                // ========================================
                
                // 根据摇杆位置计算目标转速
                float targetSpeed = throttle * MAX_SPEED_RPM;

                // 带速率限制地逼近目标转速
                float speedDiff = targetSpeed - cart.actualSpeedRPM;
                
                if (speedDiff > SPEED_CHANGE_PER_SCAN)
                {
                    cart.actualSpeedRPM += SPEED_CHANGE_PER_SCAN;
                }
                else if (speedDiff < -SPEED_CHANGE_PER_SCAN)
                {
                    cart.actualSpeedRPM -= SPEED_CHANGE_PER_SCAN;
                }
                else
                {
                    cart.actualSpeedRPM = targetSpeed;
                }

                // ========================================
                // 转向控制 (舵轮)
                // ========================================
                
                // 根据摇杆位置计算目标舵轮角度
                float targetSteering = steering * MAX_STEERING_ANGLE;

                // 带速率限制地逼近目标角度
                float steeringDiff = targetSteering - cart.actualSteeringAngle;
                
                if (steeringDiff > STEERING_CHANGE_PER_SCAN)
                {
                    cart.actualSteeringAngle += STEERING_CHANGE_PER_SCAN;
                }
                else if (steeringDiff < -STEERING_CHANGE_PER_SCAN)
                {
                    cart.actualSteeringAngle -= STEERING_CHANGE_PER_SCAN;
                }
                else
                {
                    cart.actualSteeringAngle = targetSteering;
                }
            }
            else
            {
                // ========================================
                // 车辆关闭 - 自动停车
                // ========================================
                
                // 减速至零
                if (cart.actualSpeedRPM > DECEL_RATE_PER_SCAN)
                {
                    cart.actualSpeedRPM -= DECEL_RATE_PER_SCAN;
                }
                else if (cart.actualSpeedRPM < -DECEL_RATE_PER_SCAN)
                {
                    cart.actualSpeedRPM += DECEL_RATE_PER_SCAN;
                }
                else
                {
                    cart.actualSpeedRPM = 0.0f;
                }

                // 舵轮回正
                if (cart.actualSteeringAngle > STEERING_CHANGE_PER_SCAN)
                {
                    cart.actualSteeringAngle -= STEERING_CHANGE_PER_SCAN;
                }
                else if (cart.actualSteeringAngle < -STEERING_CHANGE_PER_SCAN)
                {
                    cart.actualSteeringAngle += STEERING_CHANGE_PER_SCAN;
                }
                else
                {
                    cart.actualSteeringAngle = 0.0f;
                }
            }

            // 输出值安全限幅
            cart.actualSpeedRPM = Clamp(cart.actualSpeedRPM, -MAX_SPEED_RPM, MAX_SPEED_RPM);
            cart.actualSteeringAngle = Clamp(cart.actualSteeringAngle, -MAX_STEERING_ANGLE, MAX_STEERING_ANGLE);

            // 超速警告判断
            float absSpeed = cart.actualSpeedRPM >= 0 ? cart.actualSpeedRPM : -cart.actualSpeedRPM;
            cart.overspeedWarning = absSpeed > OVERSPEED_THRESHOLD;
        }

        /// <summary>
        /// 将数值限制在指定范围内
        /// </summary>
        private float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
