using CartActivator;

public static class DemoCanIo
{
    public static void WriteCANPayload(byte[] payload, int port, int canId)
    {
        RunOnMCU.WriteCANMessage(port, new CANMessage
        {
            ID = (ushort)canId,
            RTR = false,
            Payload = payload
        });
    }

    public static byte[] ReadCANPayload(int port, int canId)
    {
        var msg = RunOnMCU.ReadCANMessage(port, canId);
        return msg == null ? null : msg.Payload;
    }
}

public class FrontNodeCart : CartDefinition
{
    [AsUpperIO] public float run_motor_target_velocity_front;
    [AsUpperIO] public int turn_motor_target_position_front;
    [AsLowerIO] public float run_motor_actual_velocity_front;
    [AsLowerIO] public int turn_motor_actual_position_front;
    [AsLowerIO] public int front_stage;
}

public class RearNodeCart : CartDefinition
{
    [AsUpperIO] public float run_motor_target_velocity_rear;
    [AsUpperIO] public int turn_motor_target_position_rear;
    [AsLowerIO] public float run_motor_actual_velocity_rear;
    [AsLowerIO] public int turn_motor_actual_position_rear;
    [AsLowerIO] public int rear_stage;
}

[LogicRunOnMCU(scanInterval = 100)]
public class FrontNodeLogic : LadderLogic<FrontNodeCart>
{
    public override void Operation(int iteration)
    {
        // Replace with your Node1 (Motor1/2) boot + control.
        cart.front_stage = 1;
    }
}

[LogicRunOnMCU(scanInterval = 100)]
public class RearNodeLogic : LadderLogic<RearNodeCart>
{
    public override void Operation(int iteration)
    {
        // Replace with your Node2 (Motor3/4) boot + control.
        cart.rear_stage = 1;
    }
}
