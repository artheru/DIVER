using System.Text;
using CoralinkerSDK;
using MCUSerialBridgeCLR;

namespace CoralinkerSDK;

/// <summary>
/// CoralinkerSDK 独立测试程序
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== CoralinkerSDK Test ===");
        Console.WriteLine();

        // 解析命令行参数
        string? configPath = null;
        bool generateSample = false;
        bool listPorts = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-c":
                case "--config":
                    if (i + 1 < args.Length)
                        configPath = args[++i];
                    break;
                case "-g":
                case "--generate":
                    generateSample = true;
                    break;
                case "-l":
                case "--list-ports":
                    listPorts = true;
                    break;
                case "-h":
                case "--help":
                    PrintHelp();
                    return;
            }
        }

        // 列出可用端口
        if (listPorts)
        {
            ListAvailablePorts();
            return;
        }

        // 生成示例配置
        if (generateSample)
        {
            GenerateSampleConfig();
            return;
        }

        // 运行测试
        if (string.IsNullOrEmpty(configPath))
        {
            Console.WriteLine("Error: No config file specified.");
            Console.WriteLine("Use -c <path> or --help for usage.");
            Console.WriteLine();
            PrintHelp();
            return;
        }

        RunTest(configPath);
    }

    static void PrintHelp()
    {
        Console.WriteLine("Usage: CoralinkerSDK [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -c, --config <path>   Load configuration from JSON file and run test");
        Console.WriteLine("  -g, --generate        Generate sample configuration JSON");
        Console.WriteLine("  -l, --list-ports      List available serial ports");
        Console.WriteLine("  -h, --help            Show this help message");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  CoralinkerSDK -g > config.json       # Generate sample config");
        Console.WriteLine("  CoralinkerSDK -c config.json         # Run with config");
    }

    static void ListAvailablePorts()
    {
        Console.WriteLine("Available serial ports:");
        var ports = SerialPortResolver.ListAllPorts();
        if (ports.Length == 0)
        {
            Console.WriteLine("  (none)");
        }
        else
        {
            foreach (var port in ports)
            {
                Console.WriteLine($"  {port}");
            }
        }
    }

    static void GenerateSampleConfig()
    {
        Console.WriteLine(ConfigurationHelper.GenerateSampleConfig());
    }

    static void RunTest(string configPath)
    {
        Console.WriteLine($"Loading configuration from: {configPath}");
        Console.WriteLine();

        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Error: Config file not found: {configPath}");
            return;
        }

        SessionConfiguration config;
        try
        {
            config = ConfigurationHelper.LoadFromFile(configPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading config: {ex.Message}");
            return;
        }

        Console.WriteLine($"Loaded {config.Nodes.Length} node(s):");
        foreach (var node in config.Nodes)
        {
            Console.WriteLine($"  - {node.NodeId}: {node.McuUri}");
            Console.WriteLine($"    Logic: {node.LogicName ?? "(unnamed)"}");
            Console.WriteLine($"    Program: {node.ProgramBytes.Length} bytes");
            var metaInfo = string.IsNullOrEmpty(node.MetaJson)
                ? "(empty)"
                : $"{node.MetaJson.Length} chars";
            Console.WriteLine($"    MetaJson: {metaInfo}");
            Console.WriteLine($"    PortConfigs: {node.PortConfigs?.Length ?? 0}");
        }
        Console.WriteLine();

        // 获取 DIVERSession 单例
        var session = DIVERSession.Instance;

        // 注册事件
        session.OnStateChanged += state => Console.WriteLine($"[Session] State changed: {state}");
        session.OnNodeLog += (uuid, msg) => Console.WriteLine($"[{uuid[..8]}] {msg}");

        try
        {
            // 添加并配置节点
            Console.WriteLine("Adding nodes...");
            var nodeUuids = new List<string>();
            
            foreach (var nodeConfig in config.Nodes)
            {
                Console.WriteLine($"  Adding {nodeConfig.McuUri}...");
                var uuid = session.AddNode(nodeConfig.McuUri);
                
                if (uuid != null)
                {
                    nodeUuids.Add(uuid);
                    var info = session.GetNodeInfo(uuid);
                    Console.WriteLine($"    ✓ Added: {info?.NodeName ?? uuid}");
                    Console.WriteLine($"      Version: {info?.Version?.ProductionName ?? "Unknown"}");
                    
                    // 配置端口
                    if (nodeConfig.PortConfigs != null && nodeConfig.PortConfigs.Length > 0)
                    {
                        session.ConfigureNode(uuid, new NodeSettings
                        {
                            PortConfigs = nodeConfig.PortConfigs
                        });
                        Console.WriteLine($"      Configured {nodeConfig.PortConfigs.Length} port(s)");
                    }
                    
                    // 编程
                    if (nodeConfig.ProgramBytes.Length > 0 && !string.IsNullOrEmpty(nodeConfig.MetaJson))
                    {
                        var programmed = session.ProgramNode(uuid, nodeConfig.ProgramBytes, nodeConfig.MetaJson, nodeConfig.LogicName);
                        if (programmed)
                        {
                            Console.WriteLine($"      Programmed: {nodeConfig.ProgramBytes.Length} bytes, logic={nodeConfig.LogicName ?? "(none)"}");
                        }
                        else
                        {
                            Console.WriteLine($"      ✗ Program failed");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"    ✗ Failed to add node");
                }
            }
            
            Console.WriteLine();
            Console.WriteLine($"Added {nodeUuids.Count}/{config.Nodes.Length} node(s)");
            Console.WriteLine($"Session state: {session.State}");
            Console.WriteLine();

            if (nodeUuids.Count == 0)
            {
                Console.WriteLine("No nodes added. Check port configuration.");
                return;
            }

            // 启动
            Console.WriteLine("Starting session...");
            var startResult = session.Start();
            Console.WriteLine($"Started: {startResult.SuccessNodes}/{startResult.TotalNodes}");
            
            if (startResult.Errors.Count > 0)
            {
                foreach (var error in startResult.Errors)
                {
                    Console.WriteLine($"  ✗ {error.NodeName}: {error.Error}");
                }
            }
            
            Console.WriteLine($"Session state: {session.State}");
            Console.WriteLine();

            if (startResult.SuccessNodes > 0)
            {
                Console.WriteLine("=== Running Test ===");
                Console.WriteLine("Press Ctrl+C to stop.");
                Console.WriteLine();

                var running = true;
                Console.CancelKeyPress += (_, e) =>
                {
                    running = false;
                    e.Cancel = true;
                };

                int loopCount = 0;
                while (running)
                {
                    // 显示变量
                    var fields = session.GetAllCartFields();
                    if (fields.Count > 0 && loopCount % 5 == 0)  // 每 5 秒显示一次
                    {
                        Console.WriteLine($"[Variables] {fields.Count} field(s):");
                        foreach (var kv in fields.Take(10))
                        {
                            Console.WriteLine($"  {kv.Key} = {kv.Value.Value} ({kv.Value.Type})");
                        }
                        if (fields.Count > 10)
                        {
                            Console.WriteLine($"  ... and {fields.Count - 10} more");
                        }
                    }
                    
                    // 显示节点状态
                    var states = session.GetNodeStates();
                    foreach (var kv in states)
                    {
                        var state = kv.Value;
                        if (state.Stats != null)
                        {
                            Console.WriteLine($"[{state.NodeName}] Uptime: {state.Stats.UptimeMs}ms, DI: {state.Stats.DigitalInputs}, DO: {state.Stats.DigitalOutputs}");
                        }
                    }

                    loopCount++;
                    Thread.Sleep(1000);
                }

                Console.WriteLine();
                Console.WriteLine("Stopping session...");
                session.Stop();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("Test completed.");
        }
    }
}
