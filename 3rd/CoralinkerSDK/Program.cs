using System.Text;
using CoralinkerSDK;
using MCUSerialBridgeCLR;

namespace CoralinkerSDK;

/// <summary>
/// CoralinkerSDK 独立测试程序
/// </summary>
class Program
{
    private static int _loopCount = 0;

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
        session.OnLowerIOReceived += (nodeId, data) =>
        {
            if (session.Nodes.TryGetValue(nodeId, out var node))
            {
                var formatted = HostRuntime.FormatLowerIO(nodeId, data, node.CartFields);
                Console.WriteLine($"[LowerIO] {nodeId}: {formatted}");
            }
        };
        session.OnConsoleOutput += (nodeId, msg) => Console.WriteLine($"[{nodeId}] {msg}");

        try
        {
            // 配置会话
            Console.WriteLine("Configuring session...");
            session.Configure(config);
            Console.WriteLine($"Session state: {session.State}");
            Console.WriteLine();

            // 连接所有节点
            Console.WriteLine("Connecting nodes...");
            var connected = session.ConnectAll();
            Console.WriteLine($"Connected: {connected}/{config.Nodes.Length}");

            if (connected == 0)
            {
                Console.WriteLine("No nodes connected. Check port configuration.");
                PrintNodeErrors(session);
                return;
            }
            Console.WriteLine();

            // 配置并编程
            Console.WriteLine("Configuring and programming nodes...");
            var programmed = session.ConfigureAndProgramAll();
            Console.WriteLine($"Programmed: {programmed}/{connected}");

            if (programmed == 0)
            {
                Console.WriteLine("No nodes programmed. Check program bytes.");
                PrintNodeErrors(session);
                return;
            }
            Console.WriteLine();

            // 启动
            Console.WriteLine("Starting nodes...");
            var started = session.StartAll();
            Console.WriteLine($"Started: {started}/{programmed}");
            Console.WriteLine($"Session state: {session.State}");
            Console.WriteLine();

            if (started > 0)
            {
                Console.WriteLine();
                Console.WriteLine("=== Auto UpperIO Test ===");
                Console.WriteLine("Updating digital_output every 1s: 1,2,4,...,1<<14 (loop).");
                Console.WriteLine("Press Ctrl+C to stop.");

                var running = true;
                Console.CancelKeyPress += (_, e) =>
                {
                    running = false;
                    e.Cancel = true;
                };

                int value = 1;
                while (running)
                {
                    foreach (var node in session.Nodes.Values)
                    {
                        HostRuntime.SetCartVariable(node.NodeId, "digital_output", value);
                    }

                    Console.WriteLine($"[UpperIO] digital_output = {value}");

                    value <<= 1;
                    if (value > (1 << 14))
                        value = 1;

                    Thread.Sleep(1000);
                }

                Console.WriteLine("Stopping nodes...");
                session.StopAll();
            }

            Console.WriteLine("Disconnecting...");
            session.DisconnectAll();
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

    static void PrintNodeErrors(DIVERSession session)
    {
        foreach (var node in session.Nodes.Values)
        {
            if (!string.IsNullOrEmpty(node.LastError))
            {
                Console.WriteLine($"  [{node.NodeId}] Error: {node.LastError}");
            }
        }
    }
}
