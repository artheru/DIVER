using System;
using CartActivator;

namespace DiverTest;

public class TestVehicle2 : LocalDebugDIVERVehicle
{
    [AsLowerIO] public int read_from_mcu;
    [AsUpperIO] public int write_to_mcu;
}

[LogicRunOnMCU(scanInterval = 1000)]
public class TestStringInterpolation : LadderLogic<TestVehicle2>
{
    private int counter = 0;
    private float temperature = 98.6f;
    private string name = "World";

    // This method will be processed by our StringInterpolationHandler
    // String interpolation ($"...") will be converted to String.Format calls
    public override void Operation(int i)
    {
        counter++;
        temperature += 0.1f;
        
        // Simple string interpolation
        Console.WriteLine($"Hello, {name}!");
        
        // String interpolation with formatting
        var formattedMessage = $"Counter: {counter}, Temperature: {temperature:0.00}°F";
        Console.WriteLine(formattedMessage);

        // Complex string interpolation with multiple values and format specifiers
        var complexMessage = $"Sensor {i} reading at {DateTime.Now:HH:mm:ss}: {temperature:0.00}°F (cycle {counter})";
        Console.WriteLine(complexMessage);
    }
} 