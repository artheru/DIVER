using CartActivator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace DiverTest.DIVER.CoralinkerAdaption
{
    public abstract class Coralinking
    {
        public class CoralinkerRoot // root is PC or linux-arm device.
        {
            public CoralinkerNode EldestNode;
            public CoralinkerNode Downlink(Type ladderlogicCls)
            {
                return EldestNode = CoralinkerNode.GetFromLadderLogicType(ladderlogicCls);
            }
        }

        public class CoralinkerNode 
        {
            public static CoralinkerNode GetFromLadderLogicType(Type type)
            {
                var ret = new CoralinkerNode();
                foreach (var attr in type.GetCustomAttributes())
                {
                    if (attr.GetType().IsSubclassOfRawGeneric(typeof(UseCoralinkerMCUAttribute<>), out var gType))
                    {
                        var node_type = gType.GenericTypeArguments[0];
                        var ntype = (CoralinkerNodeDefinition)Activator.CreateInstance(node_type);
                        ret.node_def = ntype;
                        ret.node_def.define();
                    }
                }

                if (ret.node_def == null)
                    throw new Exception($"LadderLogic {type.Name} doesn't specify Coralinker Node type!");
                return ret;
            }

            public CoralinkerNodeDefinition node_def;

            public CoralinkerNode Downlink(Type ladderlogicCls, int id = 0)
            {
                return GetFromLadderLogicType(ladderlogicCls);
            }

            public Pin ArbitaryPin<T>(string name) where T: Pin, new()
            {
                return new T() { name = name };
            }

            public Pin ResolvedPin<T>(string name, string placement) where T: Pin, new()
            {
                return new T() { name = name };
            }
        }

        public CoralinkerRoot Root = new();
        public abstract void Define();

        public Func<CoralinkerDIVERVehicle.WiringLayout[], bool> GatherRequirements()
        {
            return (_) => false; // debug: just say requirements not meet.
        }

        public struct NodeSolution
        {
            public string url;
            public Dictionary<string, bool> matrix;
        }

        public void RequireConnect(Pin A, Pin B)
        {

        }

        public NodeSolution[] Solve()
        { 
            // just reprogram all nodes.
            Console.WriteLine("Solve linking problem");

            // 1> group all requirement pin, dye color.
            // 2> for each node use sorting network to solve.
            return null;
        }
    }

    public abstract class Pin
    {
        internal string name;
        private string grouping = "/";

        public abstract bool CanFlowTo(Pin what);
    }

    public class A10Pin : Pin
    {
        public override bool CanFlowTo(Pin what)
        {
            throw new NotImplementedException();
        }
    }


    public abstract class CoralinkerNodeDefinition
    {
        public abstract string SKU { get; }

        public abstract class FunctionModule
        {
            public abstract Dictionary<string, bool> Solve();
        }
        public class SortingNetworkAllConnecting<T>:FunctionModule where T : Pin, new()
        {
            private T[] enteringTs;
            private List<T> intermediateTs = [];
            internal SortingNetworkAllConnecting(T[] Ts)
            {
                enteringTs = Ts;
            }


            private List<(T A, T B, T X, T Y, string name)> comparators = [];
            private List<(T A, T B, string name)> relays = [];

            internal (T compared1, T compared2) declareComparator(T cmp1, T cmp2, string comparatorName)
            {
                var p1 = new T() { name = comparatorName + "_p1" };
                var p2 = new T() { name = comparatorName + "_p2" };
                comparators.Add((cmp1, cmp2, p1, p2, comparatorName));
                return (p1, p2);
            }

            internal void declareRelay(T T1, T T2, string relayName)
            {
                relays.Add((T1, T2, relayName));
            }

            // actually we cannot determine if a network is a sorting network, co-NP
            // so we just solve and validate if the connection is ok.
            public override Dictionary<string, bool> Solve()
            {
                return [];
            }
        }

        public enum ExtPinType
        {
            MainPower, Uplink, Downlink, Resource, Inbound, Left, Right
        }

        internal List<Pin> extPins = [];
        internal List<FunctionModule> functionModules = [];

        internal T DeclareCablePin<T>(ExtPinType type, string name) where T: Pin
        {
            return null;
        }

        internal T DeclareResourcePin<T>(ExtPinType type, string name) where T:Pin, new()
        {
            var ret = new T() { name = name };
            extPins.Add(ret);
            return ret;
        }

        internal T DeclareDomain<T>(T[] pins) where T : Pin
        {
            return null;
        }

        // can omit pin capacity.
        internal void DeclareRelay(Pin T1, Pin T2, string relayName)
        {

        }

        internal SortingNetworkAllConnecting<T> allConnectable<T>(T[] pins) where T:Pin, new()
        {
            var ret = new SortingNetworkAllConnecting<T>(pins);
            functionModules.Add(ret);
            return ret;
        }

        internal abstract void define();

        //
        public string Solve()
        {
            return "/";
        }
    }
}
