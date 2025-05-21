using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CartActivator;

namespace DiverTest
{
    internal class Program
    {
        static void Main(string[] args)
        {
            new TestVehicle(){ motor_actual_velocity_A = 10000, motor_actual_velocity_B = 10000}.RunDIVER();
        }
    }
}
