using CartActivator;

public class CarCart : CartDefinition
{
    [AsUpperIO] public int joystickX;
    [AsUpperIO] public int joystickY;

    [AsLowerIO] public int leftRPM;
    [AsLowerIO] public int rightRPM;
    [AsLowerIO] public int speed;
    [AsLowerIO] public int steerAngle;
}

[LogicRunOnMCU(scanInterval = 50)]
public class CarDemo : LadderLogic<CarCart>
{
    public override void Operation(int iteration)
    {
        int throttle = cart.joystickY;
        int steering = cart.joystickX;

        int baseSpeed = throttle * 10;
        int diff = steering * 5;

        cart.leftRPM = baseSpeed + diff;
        cart.rightRPM = baseSpeed - diff;
        cart.speed = throttle;
        cart.steerAngle = steering;

        if (iteration % 20 == 0)
        {
            Console.WriteLine($"Throttle={throttle}, Steering={steering}, L={cart.leftRPM}, R={cart.rightRPM}");
        }
    }
}
