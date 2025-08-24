using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Coralinker_arch
{

	public class Layout
	{
		private readonly HashSet<string> links = new();

		private static string Key(string a, string b)
		{
			if (string.CompareOrdinal(a, b) <= 0) return a + "|" + b;
			return b + "|" + a;
		}

		public void Link(string pinAName, string pinBName)
		{
			links.Add(Key(pinAName, pinBName));
		}

		public bool HasLink(string pinAName, string pinBName)
		{
			return links.Contains(Key(pinAName, pinBName));
		}

		public IEnumerable<(string A, string B)> EnumerateLinks()
		{
			foreach (var k in links)
			{
				var parts = k.Split('|');
				yield return (parts[0], parts[1]);
			}
		}

		public override string ToString()
		{
			return string.Join(", ", EnumerateLinks().Select(p => $"{p.A}<->{p.B}"));
		}
	}

	public class LinkingRequirements
	{
		// what pins are connected, give connecting group.
		internal readonly List<(Pin A, Pin B)> requiredConnections = new();

		public LinkingRequirements()
		{
		}

		public LinkingRequirements(IEnumerable<(Pin A, Pin B)> connections)
		{
			if (connections != null)
				requiredConnections.AddRange(connections);
		}

		private List<List<Pin>> ComputeGroups()
		{
			var indexByPin = new Dictionary<Pin, int>(ReferenceEqualityComparer<Pin>.Instance);
			var parent = new List<int>();
			int Find(int x)
			{
				if (parent[x] != x) parent[x] = Find(parent[x]);
				return parent[x];
			}
			void Union(int a, int b)
			{
				var ra = Find(a);
				var rb = Find(b);
				if (ra == rb) return;
				parent[rb] = ra;
			}
			int AddPin(Pin p)
			{
				if (!indexByPin.TryGetValue(p, out var idx))
				{
					idx = parent.Count;
					indexByPin[p] = idx;
					parent.Add(idx);
				}
				return idx;
			}
			foreach (var (A, B) in requiredConnections)
			{
				var ia = AddPin(A);
				var ib = AddPin(B);
				Union(ia, ib);
			}
			var groups = new Dictionary<int, List<Pin>>();
			foreach (var (p, idx) in indexByPin)
			{
				var root = Find(idx);
				if (!groups.TryGetValue(root, out var list))
				{
					list = new List<Pin>();
					groups[root] = list;
				}
				list.Add(p);
			}
			return groups.Values.ToList();
		}

		public bool Test(Layout existingLayout)
		{
			if (existingLayout == null) return false;
			var groups = ComputeGroups();
			foreach (var group in groups)
			{
				// all pairs in the group must be linked
				for (int i = 0; i < group.Count; i++)
				{
					for (int j = i + 1; j < group.Count; j++)
					{
						var A = group[i];
						var B = group[j];
						var aName = A.assignment?.name ?? A.desiredPinName;
						var bName = B.assignment?.name ?? B.desiredPinName;
						if (string.IsNullOrWhiteSpace(aName) || string.IsNullOrWhiteSpace(bName)) return false;
						if (!existingLayout.HasLink(aName, bName)) return false;
					}
				}
			}
			return true;
		}

		public bool Test(Coralinking.NodeSolution[] solutions)
		{
			if (solutions == null || solutions.Length == 0) return false;
			var groups = ComputeGroups();
			// build connectivity graph from NodeSolution matrices: treat any "pinA<->pinB" == "on" as an undirected edge
			var edges = new HashSet<(string, string)>();
			foreach (var sol in solutions)
			{
				foreach (var kv in sol.matrix)
				{
					if (kv.Value != "on") continue;
					var key = kv.Key;
					var idx = key.IndexOf("<->", StringComparison.Ordinal);
					if (idx <= 0) continue;
					var a = key.Substring(0, idx);
					var b = key.Substring(idx + 3);
					if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) continue;
					// Normalize for undirected using full qualified endpoints
					var k = string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
					edges.Add(k);
				}
			}
			bool Connected(string a, string b)
			{
				if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
				// BFS over implicit graph
				var q = new Queue<string>();
				var visited = new HashSet<string>(StringComparer.Ordinal);
				q.Enqueue(a);
				visited.Add(a);
				while (q.Count > 0)
				{
					var cur = q.Dequeue();
					if (cur == b) return true;
					foreach (var (x, y) in edges)
					{
						if (x == cur && !visited.Contains(y)) { visited.Add(y); q.Enqueue(y); }
						else if (y == cur && !visited.Contains(x)) { visited.Add(x); q.Enqueue(x); }
					}
				}
				return false;
			}
			// 1) pins in same group must connect
			foreach (var group in groups)
			{
				for (int i = 0; i < group.Count; i++)
				{
					for (int j = i + 1; j < group.Count; j++)
					{
						var A = group[i];
						var B = group[j];
						string Qualify(Pin p) {
							var n = p.assignment?.name ?? p.desiredPinName;
							return $"node{p.originNode?.node_id ?? 0}.{n}";
						}
						var aName = Qualify(A);
						var bName = Qualify(B);
						if (!Connected(aName, bName)) return false;
					}
				}
			}
			// 2) pins in different groups must not connect
			for (int gi = 0; gi < groups.Count; gi++)
			{
				for (int gj = gi + 1; gj < groups.Count; gj++)
				{
											foreach (var A in groups[gi])
						{
							foreach (var B in groups[gj])
							{
								string Qualify(Pin p) {
									var n = p.assignment?.name ?? p.desiredPinName;
									return $"node{p.originNode?.node_id ?? 0}.{n}";
								}
								var aName = Qualify(A);
								var bName = Qualify(B);
								if (Connected(aName, bName)) return false;
							}
						}
				}
			}
			return true;
		}

		public string Dump()
		{
			string PinStr(Pin p)
			{
				var label = !string.IsNullOrWhiteSpace(p.name) ? p.name : "?";
				var inner = !string.IsNullOrWhiteSpace(p.desiredPinName) ? "@" + p.desiredPinName : "?";
				return $"{label}({inner})";
			}
			var groups = ComputeGroups();
			var lines = new List<string>();
			for (int gi = 0; gi < groups.Count; gi++)
			{
				var members = string.Join(", ", groups[gi].Select(PinStr));
				lines.Add($"connection group {gi + 1}: {members}");
			}
			return string.Join("\n", lines);
		}

		public string DumpAssignments()
		{
			string PinAssignStr(Pin p)
			{
				var label = !string.IsNullOrWhiteSpace(p.name) ? p.name : "?";
				var assigned = p.assignment?.name ?? p.desiredPinName ?? "?";
				return $"{label} -> {assigned}";
			}
			var groups = ComputeGroups();
			var lines = new List<string>();
			for (int gi = 0; gi < groups.Count; gi++)
			{
				var members = string.Join(", ", groups[gi].Select(PinAssignStr));
				lines.Add($"assignment group {gi + 1}: {members}");
			}
			return string.Join("\n", lines);
		}

		public bool HasUnassigned()
		{
			foreach (var (A, B) in requiredConnections)
			{
				if (A.assignment == null || B.assignment == null) return true;
			}
			return false;
		}

		public string DumpUnassigned()
		{
			var groups = ComputeGroups();
			var lines = new List<string>();
			for (int gi = 0; gi < groups.Count; gi++)
			{
				var missing = groups[gi]
					.Where(p => p.assignment == null)
					.Select(p => !string.IsNullOrWhiteSpace(p.name) ? p.name : p.desiredPinName ?? "?");
				var s = string.Join(", ", missing);
				if (!string.IsNullOrWhiteSpace(s)) lines.Add($"group {gi + 1} unassigned: {s}");
			}
			return string.Join("\n", lines);
		}

		internal List<List<Pin>> GetGroupsForSolve() => ComputeGroups();
	}

	public abstract class Coralinking
	{
		public CoralinkerRoot Root = new();
		private readonly List<(Pin A, Pin B)> requiredConnections = new();
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
			public CoralinkerNodeDefinition node_def;
			public int node_id;
			public Type controllerType;
			public string mcuUri = "unknown://";
			public int scanInterval = 50;
			private readonly HashSet<PinInstance> allocatedPins = new();

			public static CoralinkerNode GetFromLadderLogicType(Type type)
			{
				var ret = new CoralinkerNode();
				ret.controllerType = type;
				foreach (var attr in type.GetCustomAttributes())
				{
					if (attr.GetType().IsSubclassOfRawGeneric(typeof(UseCoralinkerMCUAttribute<>), out var gType))
					{
						var node_type = gType.GenericTypeArguments[0];
						var ntype = (CoralinkerNodeDefinition)Activator.CreateInstance(node_type);
						ret.node_def = ntype;
						ret.node_def.define();
					}
					if (attr is LogicRunOnMCUAttribute m)
					{
						ret.mcuUri = m.mcuUri;
						ret.scanInterval = m.scanInterval;
					}
				}

				if (ret.node_def == null)
					throw new Exception($"LadderLogic {type.Name} doesn't specify Coralinker Node type!");
				return ret;
			}


			// Node topology

			public CoralinkerNode node_down, node_left, node_right;
			public CoralinkerNode Downlink(Type ladderlogicCls)
			{
				return node_down=GetFromLadderLogicType(ladderlogicCls);
			}
			public CoralinkerNode SibLeftlink(Type ladderlogicCls)
			{
				return node_left=GetFromLadderLogicType(ladderlogicCls);
			}
			public CoralinkerNode SibRightlink(Type ladderlogicCls)
			{
				return node_right=GetFromLadderLogicType(ladderlogicCls);
			}


			// On node external pin placement.
			public Pin Alloc(Func<PinInstance, bool> filter=null)
			{
				// Do not assign here; just create a requirement with a filter
				return new Pin { originNode = this, Filter = filter, role = PinRole.Input };
			}

			public Pin GetPin(string Name)
			{
				// Reference a specific hardware pin name but do not assign yet
				var ret = new Pin() { originNode = this, desiredPinName = Name };
				try
				{
					var pi = this.node_def.GetPin(Name);
					ret.role = CoralinkerNodeDefinition.MapCableTypeToRole(this.node_def.GetExposedType(pi));
				}
				catch { ret.role = PinRole.Any; }
				return ret;
			}
		}
		public abstract void Define();

		public void RequireConnect(Pin A, Pin B)
		{
			if (A == null || B == null) throw new ArgumentNullException("Pins for RequireConnect cannot be null");
			requiredConnections.Add((A, B));
		}

		public LinkingRequirements GatherRequirements()
		{
			return new LinkingRequirements(requiredConnections);
		}

		private IEnumerable<CoralinkerNode> EnumerateNodes()
		{
			var visited = new HashSet<CoralinkerNode>(ReferenceEqualityComparer<CoralinkerNode>.Instance);
			var order = new List<CoralinkerNode>();
			int nextId = 1;
			void Visit(CoralinkerNode n)
			{
				if (n == null || visited.Contains(n)) return;
				n.node_id = nextId++;
				visited.Add(n);
				order.Add(n);
				Visit(n.node_left);
				Visit(n.node_right);
				Visit(n.node_down);
			}
			Visit(Root.EldestNode);
			return order;
		}

		public class NodeSolution
		{
			public string url = "unknown://";
			public Dictionary<string, string> matrix = new Dictionary<string, string>();
			public int scanInterval = 50;
		}
        
		public NodeSolution[] Solve(Layout unused=null)
		{ 
			Console.WriteLine("Solve linking problem");

			var usedPins = new HashSet<PinInstance>();
			var groups = GatherRequirements().GetGroupsForSolve();

			bool GroupSpansMultipleNodes(List<Pin> g)
			{
				var nodes = new HashSet<CoralinkerNode>(ReferenceEqualityComparer<CoralinkerNode>.Instance);
				foreach (var p in g) if (p.originNode != null) nodes.Add(p.originNode);
				return nodes.Count > 1;
			}

			// helper to score preference for a candidate pin instance
			int ScoreCandidate(Pin reqPin, PinInstance candidate, IEnumerable<Pin> groupPeers)
			{
				int score = 0;
				// preference 1: already wired within the same connection group (dropped with no Layout)
				// preference 2: candidate participates in any sorting network
				var node = reqPin.originNode;
				if (node?.node_def != null)
				{
					if (node.node_def.IsPinInAnySortingNetwork(candidate))
					{
						score += 1;
					}
				}
				// preference 3: consider peer filter compatibility to promote feasible links
				foreach (var peer in groupPeers)
				{
					if (peer.Filter != null)
					{
						bool ok = peer.Filter(candidate);
						score += ok ? 2 : -3;
					}
				}
				// preference 4: amp capacity heuristics
				bool requiresHighAmp = false;
				if (reqPin.Filter != null)
				{
					var probeLow = new PinInstance { name = "__probe_low__", amp_limit = 10 };
					var probeMid = new PinInstance { name = "__probe_mid__", amp_limit = 40 };
					bool lowOk = reqPin.Filter(probeLow);
					bool midOk = reqPin.Filter(probeMid);
					requiresHighAmp = !lowOk && midOk;
				}
				if (requiresHighAmp)
				{
					if (candidate.amp_limit >= 80) score += 2; else if (candidate.amp_limit >= 40) score += 1;
				}
				else
				{
					if (candidate.amp_limit >= 80) score -= 1;
				}
				return score;
			}

			IEnumerable<PinInstance> EnumerateCandidates(Pin p, bool routingFallback)
			{
				var nodeDef = p.originNode.node_def;
				if (!string.IsNullOrWhiteSpace(p.desiredPinName))
				{
					PinInstance only;
					try { only = nodeDef.GetPin(p.desiredPinName); }
					catch { yield break; }
					yield return only;
					yield break;
				}
				IReadOnlyList<PinInstance> primary = Array.Empty<PinInstance>();
				switch (p.role)
				{
					case PinRole.Input:
						primary = nodeDef.GetPinsByType(CoralinkerNodeDefinition.ExposedPinCableType.Input);
						break;
					case PinRole.Uplink:
						primary = nodeDef.GetPinsByType(CoralinkerNodeDefinition.ExposedPinCableType.CableUplink);
						break;
					case PinRole.Downlink:
						primary = nodeDef.GetPinsByType(CoralinkerNodeDefinition.ExposedPinCableType.CableDownlink);
						break;
					case PinRole.LeftSib:
						primary = nodeDef.GetPinsByType(CoralinkerNodeDefinition.ExposedPinCableType.CableLeftSibLink);
						break;
					case PinRole.RightSib:
						primary = nodeDef.GetPinsByType(CoralinkerNodeDefinition.ExposedPinCableType.CableRightSibLink);
						break;
					case PinRole.Resource:
						primary = Array.Empty<PinInstance>();
						break;
					case PinRole.Any:
						primary = nodeDef.extPins;
						break;
				}
				foreach (var pi in primary) yield return pi;
				if (routingFallback)
				{
					foreach (var (snName, pins) in nodeDef.GetSortingNetworks())
					{
						foreach (var pi in pins) yield return pi;
					}
				}
			}

			bool IsCandidateAllowed(Pin p, PinInstance candidate)
			{
				if (p.Filter != null && !p.Filter(candidate)) return false;
				switch (p.role)
				{
					case PinRole.Input:
						if (!p.originNode.node_def.GetPinsByType(CoralinkerNodeDefinition.ExposedPinCableType.Input).Contains(candidate)) return false;
						break;
					case PinRole.Uplink:
						if (!p.originNode.node_def.GetPinsByType(CoralinkerNodeDefinition.ExposedPinCableType.CableUplink).Contains(candidate)) return false;
						break;
					case PinRole.Downlink:
						if (!p.originNode.node_def.GetPinsByType(CoralinkerNodeDefinition.ExposedPinCableType.CableDownlink).Contains(candidate)) return false;
						break;
					case PinRole.LeftSib:
						if (!p.originNode.node_def.GetPinsByType(CoralinkerNodeDefinition.ExposedPinCableType.CableLeftSibLink).Contains(candidate)) return false;
						break;
					case PinRole.RightSib:
						if (!p.originNode.node_def.GetPinsByType(CoralinkerNodeDefinition.ExposedPinCableType.CableRightSibLink).Contains(candidate)) return false;
						break;
				}
				return true;
			}

			bool AssignGroupWithBacktracking(List<Pin> group)
			{
				foreach (var p in group)
				{
					if (!string.IsNullOrWhiteSpace(p.desiredPinName) && p.assignment == null)
					{
						try
						{
							var pi = p.originNode.node_def.GetPin(p.desiredPinName);
							if (!usedPins.Contains(pi) && IsCandidateAllowed(p, pi))
							{
								p.assignment = pi;
								usedPins.Add(pi);
							}
							else if (!usedPins.Contains(pi))
							{
								return false;
							}
						}
						catch { return false; }
					}
				}

				var vars = group.ToList();
				vars.Sort((a, b) => string.IsNullOrWhiteSpace(b.desiredPinName).CompareTo(string.IsNullOrWhiteSpace(a.desiredPinName)));
				bool routing = GroupSpansMultipleNodes(group);

				bool Dfs(int idx)
				{
					if (idx >= vars.Count) return true;
					var p = vars[idx];
					if (p.assignment != null) return Dfs(idx + 1);
					var candidates = EnumerateCandidates(p, routing)
						.Distinct()
						.Where(pi => !usedPins.Contains(pi) && IsCandidateAllowed(p, pi))
						.OrderByDescending(pi => ScoreCandidate(p, pi, vars.Where(x => !ReferenceEquals(x, p))))
						.ToList();
					foreach (var cand in candidates)
					{
						p.assignment = cand;
						usedPins.Add(cand);
						if (Dfs(idx + 1)) return true;
						p.assignment = null;
						usedPins.Remove(cand);
					}
					return false;
				}

				return Dfs(0);
			}

			groups.Sort((ga, gb) =>
				gb.Count(p => !string.IsNullOrWhiteSpace(p.desiredPinName))
					.CompareTo(ga.Count(p => !string.IsNullOrWhiteSpace(p.desiredPinName))));

			bool SolveGroupsRec(int gi)
			{
				if (gi >= groups.Count) return true;
				var group = groups[gi];
				var backupAssignments = new Dictionary<Pin, PinInstance>(ReferenceEqualityComparer<Pin>.Instance);
				foreach (var p in group) backupAssignments[p] = p.assignment;
				var usedBefore = new HashSet<PinInstance>(usedPins);

				bool success = AssignGroupWithBacktracking(group);
				if (success)
				{
					if (SolveGroupsRec(gi + 1)) return true;
				}
				foreach (var p in group)
				{
					p.assignment = backupAssignments[p];
				}
				usedPins.Clear();
				foreach (var pi in usedBefore) usedPins.Add(pi);

				return false;
			}

			SolveGroupsRec(0);

			// Build NodeSolutions
			var nodes = EnumerateNodes().ToList();
			var sols = new List<NodeSolution>();
			foreach (var node in nodes)
			{
				var sol = new NodeSolution { url = node.mcuUri, scanInterval = node.scanInterval };
				// Generate full swap and switch states for each sorting network
				foreach (var sn in node.node_def.GetSortingNetworks())
				{
					var conf = node.node_def.GetSortingNetworkConfig(sn.name);

					if (conf != null)
					{
						var orderedPins = sn.pins.Select(p => p.name).ToList();
						var groupIndexByPin = new Dictionary<string, int>(StringComparer.Ordinal);
						foreach (var name in orderedPins) groupIndexByPin[name] = 0;
						for (int gi2 = 0; gi2 < groups.Count; gi2++)
						{
							foreach (var p in groups[gi2])
							{
								var n = p.assignment?.name;
								if (string.IsNullOrWhiteSpace(n)) continue;
								if (groupIndexByPin.ContainsKey(n)) groupIndexByPin[n] = gi2 + 1;
							}
						}
						var plan = CoralinkerNodeDefinition.SortingNetworkAllConnecting.SolvePlanFull(conf, orderedPins, groupIndexByPin);
						foreach (var kv in plan) sol.matrix[kv.Key] = kv.Value;
					}
				}
				sols.Add(sol);

				// switches already determined in SolvePlanFull
			}

			// Now encode group connectivity into NodeSolutions as link closures directly (no Layout)
			for (int gi = 0; gi < groups.Count; gi++)
			{
				var group = groups[gi];
				for (int i = 0; i < group.Count; i++)
				{
					for (int j = i + 1; j < group.Count; j++)
					{
						var A = group[i];
						var B = group[j];
						var aName = A.assignment?.name;
						var bName = B.assignment?.name;
						if (string.IsNullOrWhiteSpace(aName) || string.IsNullOrWhiteSpace(bName)) continue;
							if (aName == bName && A.originNode == B.originNode) continue;
							bool okAB = A.Filter == null || A.Filter(B.assignment);
							bool okBA = B.Filter == null || B.Filter(A.assignment);
							if (!okAB || !okBA) continue;
							string Qualify(Pin p, string n) => $"node{p.originNode?.node_id ?? 0}.{n}";
							var key = $"{Qualify(A, aName)}<->{Qualify(B, bName)}";
							// put links on the first node solution (or determine node based on originNode if needed). keep simple here.
							sols[0].matrix[key] = "on";
					}
				}
			}

			// Print sorting network partitions
			for (int ni = 0; ni < nodes.Count; ni++)
			{
				var node = nodes[ni];
				foreach (var sn in node.node_def.GetSortingNetworks())
				{
					var parts = new List<string>();
					foreach (var group in groups)
					{
						var pinsForThisSN = group
							.Select(p => p.assignment)
							.Where(a => a != null && sn.pins.Contains(a))
							.Select(a => a.name)
							.ToList();
						if (pinsForThisSN.Count == 0) continue;
						parts.Add("(" + string.Join(", ", pinsForThisSN) + ")");
					}
					if (parts.Count > 0)
					{
						Console.WriteLine($"node{node.node_id}.{sn.name}: {string.Join(", ", parts)}");
					}
				}
			}

			// Per-node, per-network matrix printout
			for (int ni = 0; ni < nodes.Count; ni++)
					{
				var node = nodes[ni];
				var sol = sols[ni];
				foreach (var sn in node.node_def.GetSortingNetworks())
				{
					var prefix = sn.name + ".";
					var kvps = sol.matrix
						.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
						.Select(kv => kv.Key + "=" + kv.Value)
						.ToList();
					if (kvps.Count > 0)
					{
						Console.WriteLine($"node{node.node_id}.{sn.name} matrix: {string.Join(", ", kvps)}");
					}
				}
			}

			return sols.ToArray();
		}
	}

	// Pin Requirement.
	public enum PinRole
	{
		Input,
		Uplink,
		Downlink,
		LeftSib,
		RightSib,
		Resource,
		Any
	}
	public class Pin //Requirement
	{
		internal string name = "";
		internal string connection_grouping = "/";
		internal string placing = "/";
		internal string desiredPinName;
		internal Coralinking.CoralinkerNode originNode;

		public Func<PinInstance, bool> Filter;
		public PinInstance assignment;
		public PinRole role = PinRole.Input;

		public Pin SetName(string name)
		{
			this.name = name;
			return this;
		}
	}

	public class PinInstance
	{
		internal string name = "";
		public float amp_limit = 10;
	}

	public class WireInstance
	{
		public float amp_limit = 10;
		private PinInstance A, B;

		public void Connect(PinInstance up0)
		{
			if (A == null) { A = up0; return; }
			B = up0;
		}
	}

	public abstract class CoralinkerNodeDefinition
	{
		public abstract string SKU { get; }

		public class SortingNetworkAllConnecting
		{
			// use a sorting network to allow any partition of the input wires. (bell number possibilities)
			public string name;
			public List<List<(int i, int j, string relay_name)>> swaps;
			public List<string> switch_names;

			public void Solve()
			{
				// placeholder for future in-depth solve
			}

			public static Dictionary<string, string> SolvePlanFull(SortingNetworkAllConnecting conf, List<string> orderedPins)
			{
				var result = new Dictionary<string, string>();
				if (conf == null || orderedPins == null || orderedPins.Count == 0) return result;
				int n = orderedPins.Count;
				int middle = Math.Max(0, n - 2);
				int offset = 1; // head fixed
				// Prepare current middle pin order by name
				var middlePins = orderedPins.Skip(1).Take(middle).ToList();
				if (conf.swaps != null)
				{
					foreach (var stage in conf.swaps)
					{
						// default all relays in this stage to no; set yes if used
						foreach (var (_, _, rn) in stage)
						{
							if (!string.IsNullOrWhiteSpace(rn)) result[$"{conf.name}.{rn}"] = "no";
						}
						// for each comparator (i,j), compare strings at those positions; if out of order, swap
						foreach (var (i, j, rn) in stage)
						{
							if (i < 0 || j < 0 || i >= middle || j >= middle || i == j || string.IsNullOrWhiteSpace(rn)) continue;
							var a = middlePins[i];
							var b = middlePins[j];
							if (StringComparer.Ordinal.Compare(a, b) > 0)
							{
								result[$"{conf.name}.{rn}"] = "yes";
								// swap positions
								middlePins[i] = b;
								middlePins[j] = a;
							}
						}
					}
				}
									// Emit all output switch states explicitly as off; higher-level Solve will turn on only those required by grouping
					if (conf.switch_names != null)
				{
						foreach (var sw in conf.switch_names)
						{
							if (string.IsNullOrWhiteSpace(sw)) continue;
							result[$"{conf.name}.{sw}"] = "off";
						}
				}
				return result;
			}

			public static Dictionary<string, string> SolvePlanFull(SortingNetworkAllConnecting conf, List<string> orderedPins, Dictionary<string, int> groupIndexByPin)
			{
				var result = new Dictionary<string, string>();
				if (conf == null || orderedPins == null || orderedPins.Count == 0) return result;

				int n = orderedPins.Count;
				int middle = Math.Max(0, n - 2);
				// Use the same middle window as legacy, but sort by group id
				var middlePins = orderedPins.Skip(1).Take(middle).ToList();
				int GetGroup(string name) => (groupIndexByPin != null && groupIndexByPin.TryGetValue(name, out var g)) ? g : 0;
				if (conf.swaps != null)
				{
					foreach (var stage in conf.swaps)
					{
						foreach (var (_, _, rn) in stage)
						{
							if (!string.IsNullOrWhiteSpace(rn)) result[$"{conf.name}.{rn}"] = "no";
						}
						foreach (var (i, j, rn) in stage)
						{
							if (i < 0 || j < 0 || i >= middle || j >= middle || i == j || string.IsNullOrWhiteSpace(rn)) continue;
							var gi = GetGroup(middlePins[i]);
							var gj = GetGroup(middlePins[j]);
							if (gi > gj)
							{
								result[$"{conf.name}.{rn}"] = "yes";
								(middlePins[i], middlePins[j]) = (middlePins[j], middlePins[i]);
							}
						}
					}
				}
				// Switches: on only when the pin belongs to a group that has >=2 members present in the SN
				var countByGroup = new Dictionary<int, int>();
				if (groupIndexByPin != null)
				{
					foreach (var p in orderedPins)
					{
						var g = GetGroup(p);
						if (g <= 0) continue;
						countByGroup[g] = countByGroup.TryGetValue(g, out var c) ? c + 1 : 1;
					}
				}
				if (conf.switch_names != null)
				{
					for (int i = 0; i < Math.Min(conf.switch_names.Count, orderedPins.Count); i++)
					{
						var sw = conf.switch_names[i];
						if (string.IsNullOrWhiteSpace(sw)) continue;
						var g = GetGroup(orderedPins[i]);
						bool on = g > 0 && countByGroup.TryGetValue(g, out var c2) && c2 >= 2;
						result[$"{conf.name}.{sw}"] = on ? "on" : "off";
					}
				}
				return result;
			}
		}
		internal List<SortingNetworkAllConnecting> sortingNetworks = [];
		private readonly Dictionary<string, PinInstance> nameToPin = new();
		private readonly List<(string name, List<PinInstance> pins)> sortingNetworkPins = new();
		private readonly HashSet<(PinInstance A, PinInstance B)> internalWires = new();
		private readonly Dictionary<PinInstance, ExposedPinCableType> extPinType = new(ReferenceEqualityComparer<PinInstance>.Instance);
		private readonly Dictionary<ExposedPinCableType, List<PinInstance>> extPinsByType = new();
		private readonly Dictionary<string, SortingNetworkAllConnecting> sortingNetworkConfigs = new(StringComparer.Ordinal);
		public void DefineSortingNetwork(PinInstance[] swaped, SortingNetworkAllConnecting configuration)
		{
			var list = new List<PinInstance>(swaped);
			sortingNetworkPins.Add((configuration.name, list));
			if (!string.IsNullOrWhiteSpace(configuration?.name))
			{
				sortingNetworkConfigs[configuration.name] = configuration;
			}
		}

		public enum ExposedPinCableType
		{
			CableUplink,
			CableDownlink,
			CableLeftSibLink,
			CableRightSibLink,
			Resource,
			Input
		}

		internal List<PinInstance> extPins = [];

		public PinInstance DefineExtPin(PinInstance pi, ExposedPinCableType cableType)
		{
			// Register externally exposed pins with name lookup
			extPins.Add(pi);
			extPinType[pi] = cableType;
			if (!extPinsByType.TryGetValue(cableType, out var list))
			{
				list = new List<PinInstance>();
				extPinsByType[cableType] = list;
			}
			list.Add(pi);
			if (!string.IsNullOrWhiteSpace(pi.name))
			{
				nameToPin[pi.name] = pi;
				}
			return pi;
		}

		public void DefineWire(PinInstance A, PinInstance B, WireInstance w)
		{
			internalWires.Add((A, B));
		}

		public void DefineCable<T>(ExposedPinCableType cableType, Action<T> cableLinker) where T:Cable
		{
			// Store or apply cable mapping if needed. For now just instantiate and let user Connect pins.
			var cable = Activator.CreateInstance<T>();
			cableLinker?.Invoke(cable);
		}


		internal abstract void define();

		//
		public string Solve()
		{
			return "/";
		}

		public PinInstance GetPin(string name)
		{
			if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
			if (nameToPin.TryGetValue(name, out var pi)) return pi;
			// try base name before first dot
			var dot = name.IndexOf('.');
			if (dot > 0)
			{
				var baseName = name.Substring(0, dot);
				if (nameToPin.TryGetValue(baseName, out var basePi)) return basePi;
			}
			throw new KeyNotFoundException($"Pin '{name}' not defined in SKU {SKU}");
		}

		internal bool IsPinInAnySortingNetwork(PinInstance pi)
		{
			foreach (var sn in sortingNetworkPins)
			{
				if (sn.pins.Contains(pi)) return true;
			}
			return false;
		}

		internal IEnumerable<(string name, List<PinInstance> pins)> GetSortingNetworks() => sortingNetworkPins;
		internal IReadOnlyList<PinInstance> GetPinsByType(ExposedPinCableType type) => extPinsByType.TryGetValue(type, out var list) ? list : Array.Empty<PinInstance>();
		internal IReadOnlyList<PinInstance> GetInputPins() => GetPinsByType(ExposedPinCableType.Input);
		internal ExposedPinCableType GetExposedType(PinInstance pi) => extPinType.TryGetValue(pi, out var t) ? t : ExposedPinCableType.Input;
		internal static PinRole MapCableTypeToRole(ExposedPinCableType t)
		{
			switch (t)
			{
				case ExposedPinCableType.CableUplink: return PinRole.Uplink;
				case ExposedPinCableType.CableDownlink: return PinRole.Downlink;
				case ExposedPinCableType.CableLeftSibLink: return PinRole.LeftSib;
				case ExposedPinCableType.CableRightSibLink: return PinRole.RightSib;
				case ExposedPinCableType.Resource: return PinRole.Resource;
				case ExposedPinCableType.Input: default: return PinRole.Input;
			}
		}
		internal SortingNetworkAllConnecting GetSortingNetworkConfig(string name)
		{
			if (string.IsNullOrWhiteSpace(name)) return null;
			return sortingNetworkConfigs.TryGetValue(name, out var conf) ? conf : null;
		}
	}

	public abstract class Cable{}

}
