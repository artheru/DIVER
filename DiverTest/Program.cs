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
            Console.WriteLine(">>> Genuine Dotnet Implementation:");
            var x = new TestLogic();
            x.cart = new TestVehicle();
            x.Operation(0); 
            for (int i = 1; i < 10; ++i) 
                x.Operation(i);

            Console.WriteLine(">>> DIVER Implementation:");
            new TestVehicle(){}.RunDIVER();
        }
    }
}
