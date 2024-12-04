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
            new TestVehicle(){write_to_mcu = 123}.Start(Assembly.GetAssembly(typeof(Program)));
        }
    }
}
