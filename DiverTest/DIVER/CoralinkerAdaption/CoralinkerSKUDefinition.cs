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

            public pin UnresolvedPin(string name)
            {
                return new();
            }

            public pin ResolvedPin(string name, string placement)
            {
                return new();
            }

            public void RequireConnect(pin A, pin B)
            {

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
        public NodeSolution[] Solve()
        { 
            // just reprogram all nodes.
            Console.WriteLine("Solve linking problem");

            // 1> group all requirement pin, dye color.
            // 2> for each node use sorting network to solve.
            return null;
        }
    }

    public class pin
    {
        internal string name;
        private string grouping = "/";
    }


    public abstract class CoralinkerNodeDefinition
    {
        public abstract string SKU { get; }

        public abstract class FunctionModule
        {
            public abstract Dictionary<string, bool> Solve();
        }
        public class SortingNetworkAllConnecting:FunctionModule
        {
            private pin[] enteringPins;
            private List<pin> intermediatePins = [];
            internal SortingNetworkAllConnecting(pin[] pins)
            {
                enteringPins = pins;
            }


            private List<(pin A, pin B, pin X, pin Y, string name)> comparators = [];
            private List<(pin A, pin B, string name)> relays = [];

            internal (pin compared1, pin compared2) declareComparator(pin cmp1, pin cmp2, string comparatorName)
            {
                var p1 = new pin() { name = comparatorName + "_p1" };
                var p2 = new pin() { name = comparatorName + "_p2" };
                comparators.Add((cmp1, cmp2, p1, p2, comparatorName));
                return (p1, p2);
            }

            internal void declareRelay(pin pin1, pin pin2, string relayName)
            {
                relays.Add((pin1, pin2, relayName));
            }

            // actually we cannot determine if a network is a sorting network, co-NP
            // so we just solve and validate if the connection is ok.
            public override Dictionary<string, bool> Solve()
            {
                return [];
            }
        }

        public enum ExtPinGroup
        {
            Uplink, Downlink, Resource, Input
        }

        internal List<pin> extPins = [];
        internal List<FunctionModule> functionModules = [];
        internal pin defineResourcePin(ExtPinGroup group, string name)
        {
            var ret = new pin() { name = name };
            extPins.Add(ret);
            return ret;
        }
        

        internal SortingNetworkAllConnecting allConnectable(pin[] pins)
        {
            var ret = new SortingNetworkAllConnecting(pins);
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

    public class CoralinkerCL1_0_12p: CoralinkerNodeDefinition
    {
        public override string SKU => "cl1.0-12p";
        internal override void define()
        {
            Console.WriteLine("Reading definition of Coralinker1.0-12P");
            // todo: use 'coralinker compiler' to allow syntax like var up1=defineResourcePin(xxx), no "up1".
            var up1 = defineResourcePin(ExtPinGroup.Uplink, "up1");
            var up2 = defineResourcePin(ExtPinGroup.Uplink, "up2");
            var up3 = defineResourcePin(ExtPinGroup.Uplink, "up3");

            var down1 = defineResourcePin(ExtPinGroup.Uplink, "down1");
            var down2 = defineResourcePin(ExtPinGroup.Uplink, "down2");
            var down3 = defineResourcePin(ExtPinGroup.Uplink, "down3");

            var res1 = defineResourcePin(ExtPinGroup.Uplink, "res1");
            var res2 = defineResourcePin(ExtPinGroup.Uplink, "res2");

            var input1 = defineResourcePin(ExtPinGroup.Uplink, "input1");
            var input2 = defineResourcePin(ExtPinGroup.Uplink, "input2");
            var input3 = defineResourcePin(ExtPinGroup.Uplink, "input3");

            var sn=allConnectable([up1, up2, up3, down1, down2, down3, res1, res2, input1, input2, input3]);

            // sort up2 to input2, leave up1 and input3 directly connected.
            var (tmp1, tmp2) = sn.declareComparator(up2, up3, "comp1");
            // ...
            
        }

    }
}
