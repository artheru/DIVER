
using System;
using System.Reflection;

namespace Coralinker_arch
{

    public class DefineCoralinkingAttribute<T> : Attribute where T : Coralinking
    {

    }
    public class UseCoralinkerMCUAttribute<T> : Attribute where T : CoralinkerNodeDefinition
    {
    }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class LogicRunOnMCUAttribute : Attribute
    {
        public string mcuUri { get; set; } = "unknown://";
        public int scanInterval { get; set; } = 50;
    }

    internal static class Program
    {

        static void Main(string[] args)
        {
            foreach (var attr in typeof(TestRootController).GetCustomAttributes())
            {
                // gType is wiring requirements.
                if (attr.GetType().IsSubclassOfRawGeneric(typeof(DefineCoralinkingAttribute<>), out var gType))
                {
                    var linker_type = gType.GenericTypeArguments[0];
                    var clinking = Activator.CreateInstance(linker_type) as Coralinking;
                    clinking.Define();

                    // get pin partitions.
                    var req = clinking.GatherRequirements();
                    Console.WriteLine("Requirements:\n" + req.Dump());

                    // compute solutions directly; validation uses NodeSolution rather than Layout
                    var solutions = clinking.Solve();
                    Console.WriteLine("Assignments:\n" + req.DumpAssignments());
                    Console.WriteLine("Solutions computed: " + solutions.Length);

                    var ok = req.Test(solutions);
                    Console.WriteLine("Requirements met: " + ok);
                    if (!ok && req.HasUnassigned())
                    {
                        Console.WriteLine("Unassigned:\n" + req.DumpUnassigned());
                    }
                }
            }
        }
    }
}