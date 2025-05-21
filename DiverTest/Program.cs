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
            new TestVehicle2(){write_to_mcu = 114514}.RunDIVER();
        }
    }
}
