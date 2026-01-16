using MCUSerialBridgeCLR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoralinkerSDK;

/// <summary>
/// 配置序列化/反序列化工具
/// </summary>
public static class ConfigurationHelper
{
    /// <summary>
    /// 从 JSON 文件加载启动配置
    /// </summary>
    /// <param name="jsonPath">JSON 配置文件路径</param>
    /// <returns>SessionConfiguration</returns>
    public static SessionConfiguration LoadFromFile(string jsonPath)
    {
        var fullPath = Path.GetFullPath(jsonPath);
        var json = File.ReadAllText(fullPath);
        var basePath = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(basePath))
            basePath = Directory.GetCurrentDirectory();
        return LoadFromJson(json, basePath);
    }

    /// <summary>
    /// 从 JSON 字符串加载启动配置
    /// </summary>
    /// <param name="json">JSON 字符串</param>
    /// <param name="basePath">基础路径（用于解析相对路径）</param>
    /// <returns>SessionConfiguration</returns>
    public static SessionConfiguration LoadFromJson(string json, string basePath = ".")
    {
        var root = JObject.Parse(json);
        var config = new SessionConfiguration();

        // 解析 assemblyPath（可选）
        var assemblyToken = root["assemblyPath"];
        if (assemblyToken != null && assemblyToken.Type != JTokenType.Null)
        {
            var assemblyPath = assemblyToken.ToString();
            if (!string.IsNullOrWhiteSpace(assemblyPath))
            {
                config.AssemblyPath = ResolvePath(assemblyPath, basePath);
            }
        }

        // 解析节点列表
        var nodesArray = root["nodes"] as JArray ?? new JArray();
        var nodes = new List<NodeConfiguration>();

        foreach (var nodeJson in nodesArray.Cast<JObject>())
        {
            var node = ParseNodeConfiguration(nodeJson, basePath);
            nodes.Add(node);
        }

        config.Nodes = nodes.ToArray();
        return config;
    }

    /// <summary>
    /// 将配置保存到 JSON 文件
    /// </summary>
    public static void SaveToFile(SessionConfiguration config, string jsonPath)
    {
        var json = SaveToJson(config);
        File.WriteAllText(jsonPath, json);
    }

    /// <summary>
    /// 将配置序列化为 JSON 字符串
    /// </summary>
    public static string SaveToJson(SessionConfiguration config)
    {
        var root = new JObject();

        if (!string.IsNullOrEmpty(config.AssemblyPath))
        {
            root["assemblyPath"] = config.AssemblyPath;
        }

        var nodesArray = new JArray();
        foreach (var node in config.Nodes)
        {
            nodesArray.Add(SerializeNodeConfiguration(node));
        }
        root["nodes"] = nodesArray;

        return root.ToString(Formatting.Indented);
    }

    /// <summary>
    /// 生成示例配置 JSON
    /// </summary>
    public static string GenerateSampleConfig()
    {
        var config = new SessionConfiguration
        {
            AssemblyPath = "builds/current/LogicBuild.dll",
            Nodes = new[]
            {
                new NodeConfiguration
                {
                    NodeId = "node1",
                    McuUri = "serial://name=COM3&baudrate=1000000",
                    LogicName = "TestLogic",
                    PortConfigs = HostRuntime.DefaultPortConfigs
                }
            }
        };

        // 生成带注释的完整示例
        var root = new JObject
        {
            ["assemblyPath"] = config.AssemblyPath,
            ["nodes"] = new JArray
            {
                new JObject
                {
                    ["nodeId"] = "node1",
                    ["mcuUri"] = "serial://name=COM3&baudrate=1000000",
                    ["binPath"] = "generated/TestLogic.bin",
                    ["metaJsonPath"] = "generated/TestLogic.bin.json",
                    ["logicName"] = "TestLogic",
                    ["portConfigs"] = new JArray
                    {
                        new JObject { ["type"] = "serial", ["baud"] = 9600, ["receiveFrameMs"] = 20 },
                        new JObject { ["type"] = "serial", ["baud"] = 9600, ["receiveFrameMs"] = 20 },
                        new JObject { ["type"] = "serial", ["baud"] = 9600, ["receiveFrameMs"] = 20 },
                        new JObject { ["type"] = "serial", ["baud"] = 9600, ["receiveFrameMs"] = 20 },
                        new JObject { ["type"] = "can", ["baud"] = 500000, ["retryTimeMs"] = 10 },
                        new JObject { ["type"] = "can", ["baud"] = 500000, ["retryTimeMs"] = 10 }
                    }
                }
            }
        };

        return root.ToString(Formatting.Indented);
    }

    private static NodeConfiguration ParseNodeConfiguration(JObject nodeJson, string basePath)
    {
        var node = new NodeConfiguration
        {
            NodeId = nodeJson["nodeId"]?.ToString() ?? Guid.NewGuid().ToString("N")[..8],
            McuUri = nodeJson["mcuUri"]?.ToString() ?? "",
            LogicName = nodeJson["logicName"]?.ToString()
        };

        // 解析 binPath
        var binPath = nodeJson["binPath"]?.ToString();
        if (!string.IsNullOrEmpty(binPath))
        {
            var resolvedBinPath = ResolvePath(binPath, basePath);
            if (File.Exists(resolvedBinPath))
            {
                node.ProgramBytes = File.ReadAllBytes(resolvedBinPath);
            }
            else
            {
                Console.WriteLine($"[ConfigurationHelper] Warning: binPath not found: {resolvedBinPath}");
            }

            // 解析 metaJsonPath（可选，默认为 binPath + ".json"）
            var metaJsonPath = nodeJson["metaJsonPath"]?.ToString();
            if (string.IsNullOrEmpty(metaJsonPath))
            {
                metaJsonPath = binPath + ".json";
            }
            var resolvedMetaJsonPath = ResolvePath(metaJsonPath, basePath);
            if (File.Exists(resolvedMetaJsonPath))
            {
                node.MetaJson = File.ReadAllText(resolvedMetaJsonPath);
            }
            else
            {
                Console.WriteLine($"[ConfigurationHelper] Warning: metaJsonPath not found: {resolvedMetaJsonPath}");
            }
        }

        // 解析 portConfigs（可选）
        if (nodeJson["portConfigs"] is JArray portConfigsArray)
        {
            node.PortConfigs = PortConfig.FromJsonArray(portConfigsArray);
        }

        // 调试用路径
        if (nodeJson["diverSrc"] != null)
        {
            var diverSrcPath = ResolvePath(nodeJson["diverSrc"]!.ToString(), basePath);
            if (File.Exists(diverSrcPath))
            {
                node.DiverSrc = File.ReadAllText(diverSrcPath);
            }
        }

        if (nodeJson["diverMapJson"] != null)
        {
            var diverMapPath = ResolvePath(nodeJson["diverMapJson"]!.ToString(), basePath);
            if (File.Exists(diverMapPath))
            {
                node.DiverMapJson = File.ReadAllText(diverMapPath);
            }
        }

        return node;
    }

    private static JObject SerializeNodeConfiguration(NodeConfiguration node)
    {
        var obj = new JObject
        {
            ["nodeId"] = node.NodeId,
            ["mcuUri"] = node.McuUri
        };

        if (!string.IsNullOrEmpty(node.LogicName))
        {
            obj["logicName"] = node.LogicName;
        }

        // 注意：ProgramBytes 和 MetaJson 不序列化回 JSON（它们从文件加载）
        // 只序列化 portConfigs
        if (node.PortConfigs != null && node.PortConfigs.Length > 0)
        {
            var portConfigsArray = new JArray();
            foreach (var pc in node.PortConfigs)
            {
                portConfigsArray.Add(pc.ToJson());
            }
            obj["portConfigs"] = portConfigsArray;
        }

        return obj;
    }

    private static string ResolvePath(string path, string basePath)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.GetFullPath(Path.Combine(basePath, path));
    }
}
