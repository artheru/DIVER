using CartActivator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Intrinsics.X86;
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

            public pin UnplacedPin(string group)
            {
                return new();
            }

            public pin PlacedPin(string group, string placement)
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

    public abstract class ExposedCable;

    public class UplinkType0 : ExposedCable
    {
        public void Define()
        {

        }
    };

    public abstract class CoralinkerNodeDefinition
    {
        public abstract string SKU { get; }
        
        public abstract class ConnectingGroup
        {
            public float amp_limit;
            public float volt_limit;
            public float bandwidth;
        }

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

        public enum ExposedPinType
        {
            Uplink, 
            Downlink,
            LeftSibLink,
            RightSibLink,
            Resource,
            Input
        }

        internal List<pin> extPins = [];
        internal List<FunctionModule> functionModules = [];
        internal pin defineResourcePin(ExposedPinType type, string name)
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
        public override string SKU => "CL1.0-3F3U5I0R3D";

        public class PowerGroup : ConnectingGroup
        {
            public UpperLinkPin upper1;
            public LowerLinkPin lower1;
        }

        public PowerGroup power_group1, power_group2;

        internal override void define()
        {
            var powergroup1 = DefineConnectingGroup();
        }

    }
}
