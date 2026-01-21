using System.Collections.Concurrent;
using System.Reflection;
using MCUSerialBridgeCLR;
using Newtonsoft.Json;

namespace CoralinkerSDK;

/// <summary>
/// DIVER 运行时会话管理器（单例）
/// 负责管理所有 MCU 节点的生命周期和数据交换
/// </summary>
public sealed class DIVERSession : IDisposable
{
    private static readonly Lazy<DIVERSession> _instance = new(() => new DIVERSession());
    private readonly ConcurrentDictionary<string, MCUNode> _nodes = new();
    private readonly ConcurrentDictionary<string, int> _upperPending = new();
    private readonly CancellationTokenSource _upperCts = new();
    private readonly Thread _upperWorker;
    private readonly AutoResetEvent _upperSignal = new(false);
    private readonly object _statePollLock = new();
    private CancellationTokenSource _stateCts = new();
    private Thread? _stateWorker;
    private readonly object _stateLock = new();
    private object? _cartTarget;
    private CartFieldInfo[] _cartFields = Array.Empty<CartFieldInfo>();
    private bool _disposed;

    /// <summary>获取单例实例</summary>
    public static DIVERSession Instance => _instance.Value;

    /// <summary>会话状态</summary>
    public DIVERSessionState State { get; private set; } = DIVERSessionState.Idle;

    /// <summary>Cart 对象</summary>
    public object? CartTarget => _cartTarget;

    /// <summary>Cart 字段元数据</summary>
    public CartFieldInfo[] CartFields => _cartFields;

    /// <summary>所有节点</summary>
    public IReadOnlyDictionary<string, MCUNode> Nodes => _nodes;

    /// <summary>LowerIO 数据接收事件</summary>
    public event Action<string, byte[]>? OnLowerIOReceived;

    /// <summary>控制台输出事件</summary>
    public event Action<string, string>? OnConsoleOutput;

    /// <summary>状态变更事件</summary>
    public event Action<DIVERSessionState>? OnStateChanged;

    private DIVERSession()
    {
        _upperWorker = new Thread(UpperIOLoop)
        {
            IsBackground = true,
            Name = "DIVERSession-UpperIO"
        };
        _upperWorker.Start();
    }

    #region Node Management

    /// <summary>
    /// 添加节点
    /// </summary>
    /// <param name="nodeId">节点唯一标识</param>
    /// <param name="mcuUri">MCU 串口 URI</param>
    /// <returns>创建的节点</returns>
    public MCUNode AddNode(string nodeId, string mcuUri)
    {
        if (_nodes.ContainsKey(nodeId))
            throw new InvalidOperationException($"Node '{nodeId}' already exists");

        var node = new MCUNode(nodeId, mcuUri);
        node.OnLowerIOReceived += data => HandleLowerIO(nodeId, data);
        node.OnConsoleOutput += msg => OnConsoleOutput?.Invoke(nodeId, msg);
        _nodes[nodeId] = node;
        return node;
    }

    /// <summary>
    /// 添加并连接节点（用于 Probe 后保持连接）
    /// </summary>
    /// <param name="nodeId">节点唯一标识</param>
    /// <param name="mcuUri">MCU 串口 URI</param>
    /// <returns>创建并连接的节点，如果连接失败返回 null</returns>
    public MCUNode? AddAndConnectNode(string nodeId, string mcuUri)
    {
        // 如果已存在相同 mcuUri 的节点，先移除
        var existingNode = _nodes.Values.FirstOrDefault(n => 
            string.Equals(n.McuUri, mcuUri, StringComparison.OrdinalIgnoreCase));
        if (existingNode != null)
        {
            RemoveNode(existingNode.NodeId);
        }
        
        var node = AddNode(nodeId, mcuUri);
        if (node.Connect())
        {
            // 启动状态轮询（如果还没启动）
            StartStatePolling();
            return node;
        }
        else
        {
            // 连接失败，移除节点
            RemoveNode(nodeId);
            return null;
        }
    }

    /// <summary>
    /// 通过 mcuUri 获取节点
    /// </summary>
    public MCUNode? GetNodeByUri(string mcuUri)
    {
        return _nodes.Values.FirstOrDefault(n => 
            string.Equals(n.McuUri, mcuUri, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 移除节点
    /// </summary>
    /// <param name="nodeId">节点唯一标识</param>
    public bool RemoveNode(string nodeId)
    {
        if (_nodes.TryRemove(nodeId, out var node))
        {
            node.Dispose();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 获取节点
    /// </summary>
    public MCUNode? GetNode(string nodeId)
    {
        _nodes.TryGetValue(nodeId, out var node);
        return node;
    }

    /// <summary>
    /// 清空所有节点
    /// </summary>
    public void ClearNodes()
    {
        foreach (var node in _nodes.Values)
        {
            node.Dispose();
        }
        _nodes.Clear();
        _upperPending.Clear();
        StopStatePolling();
    }

    #endregion

    #region Session Lifecycle

    /// <summary>
    /// 配置会话（设置 Cart 和节点）- 传统模式，会清空现有节点
    /// </summary>
    /// <param name="config">配置</param>
    public void Configure(SessionConfiguration config)
    {
        lock (_stateLock)
        {
            if (State != DIVERSessionState.Idle)
                throw new InvalidOperationException($"Cannot configure in state: {State}");

            // 清空旧节点
            ClearNodes();

            // 创建 Cart 对象（可选，如果提供了 AssemblyPath）
            if (!string.IsNullOrEmpty(config.AssemblyPath))
            {
                _cartTarget = HostRuntime.CreateCartTarget(config.AssemblyPath);
            }

            // 清空变量存储
            HostRuntime.ClearAllVariables();

            // 添加节点，每个节点有自己的 MetaJson
            foreach (var nodeConfig in config.Nodes)
            {
                var node = AddNode(nodeConfig.NodeId, nodeConfig.McuUri);
                node.ProgramBytes = nodeConfig.ProgramBytes;
                node.PortConfigs = nodeConfig.PortConfigs ?? HostRuntime.DefaultPortConfigs;
                
                // 解析该节点的 MetaJson
                if (!string.IsNullOrEmpty(nodeConfig.MetaJson))
                {
                    node.CartFields = HostRuntime.ParseMetaJson(nodeConfig.MetaJson);
                    
                    // 初始化变量存储
                    HostRuntime.InitializeNodeVariables(node.NodeId, node.CartFields);
                    
                    // 如果有 Cart 对象，绑定反射信息
                    if (_cartTarget != null)
                    {
                        HostRuntime.BindCartFields(_cartTarget, node.CartFields);
                    }
                }
            }

            // 使用第一个节点的 CartFields 作为会话级别的字段（用于简单场景）
            if (_nodes.Count > 0)
            {
                _cartFields = _nodes.Values.First().CartFields;
            }

            SetState(DIVERSessionState.Configured);
        }
    }

    /// <summary>
    /// 配置已连接节点的程序（不清空节点，用于 Probe 后保持连接的场景）
    /// </summary>
    /// <param name="config">配置</param>
    public void ConfigureConnectedNodes(SessionConfiguration config)
    {
        lock (_stateLock)
        {
            // 允许在 Idle 或 Configured 状态下配置
            if (State == DIVERSessionState.Running)
                throw new InvalidOperationException($"Cannot configure in state: {State}");

            // 创建 Cart 对象（可选，如果提供了 AssemblyPath）
            if (!string.IsNullOrEmpty(config.AssemblyPath))
            {
                _cartTarget = HostRuntime.CreateCartTarget(config.AssemblyPath);
            }

            // 清空变量存储
            HostRuntime.ClearAllVariables();

            // 更新已连接节点的程序配置
            foreach (var nodeConfig in config.Nodes)
            {
                // 通过 mcuUri 查找已连接的节点
                var node = GetNodeByUri(nodeConfig.McuUri);
                if (node == null)
                {
                    Console.WriteLine($"[DIVERSession] Warning: Node with mcuUri '{nodeConfig.McuUri}' not found in session, skipping");
                    continue;
                }

                // 更新程序配置
                node.ProgramBytes = nodeConfig.ProgramBytes;
                if (nodeConfig.PortConfigs != null && nodeConfig.PortConfigs.Length > 0)
                {
                    node.PortConfigs = nodeConfig.PortConfigs;
                }
                
                // 解析该节点的 MetaJson
                if (!string.IsNullOrEmpty(nodeConfig.MetaJson))
                {
                    node.CartFields = HostRuntime.ParseMetaJson(nodeConfig.MetaJson);
                    
                    // 初始化变量存储
                    HostRuntime.InitializeNodeVariables(node.NodeId, node.CartFields);
                    
                    // 如果有 Cart 对象，绑定反射信息
                    if (_cartTarget != null)
                    {
                        HostRuntime.BindCartFields(_cartTarget, node.CartFields);
                    }
                }
                
                Console.WriteLine($"[DIVERSession] Configured node '{node.NodeId}' with {node.ProgramBytes.Length} bytes program");
            }

            // 使用第一个节点的 CartFields 作为会话级别的字段（用于简单场景）
            if (_nodes.Count > 0)
            {
                _cartFields = _nodes.Values.First().CartFields;
            }

            SetState(DIVERSessionState.Configured);
        }
    }

    /// <summary>
    /// 连接所有节点
    /// </summary>
    /// <returns>成功连接的节点数</returns>
    public int ConnectAll()
    {
        int connected = 0;
        foreach (var node in _nodes.Values)
        {
            if (node.Connect())
            {
                connected++;
            }
            else
            {
                Console.WriteLine($"[DIVERSession] Node '{node.NodeId}' connect failed: {node.LastError}");
            }
        }
        if (connected > 0)
            StartStatePolling();
        else
            StopStatePolling();
        return connected;
    }

    /// <summary>
    /// 断开所有节点
    /// </summary>
    public void DisconnectAll()
    {
        foreach (var node in _nodes.Values)
        {
            node.Disconnect();
        }
        SetState(DIVERSessionState.Idle);
        StopStatePolling();
    }

    /// <summary>
    /// 配置并编程所有节点
    /// </summary>
    public int ConfigureAndProgramAll()
    {
        int success = 0;
        foreach (var node in _nodes.Values)
        {
            if (!node.IsConnected) continue;
            
            if (!node.Configure())
            {
                Console.WriteLine($"[DIVERSession] Node '{node.NodeId}' configure failed: {node.LastError}");
                continue;
            }
            
            if (!node.Program())
            {
                Console.WriteLine($"[DIVERSession] Node '{node.NodeId}' program failed: {node.LastError}");
                continue;
            }
            
            success++;
        }
        return success;
    }

    /// <summary>
    /// 启动所有节点
    /// </summary>
    public int StartAll()
    {
        int started = 0;
        foreach (var node in _nodes.Values)
        {
            if (!node.IsConnected) continue;
            
            if (node.Start())
            {
                started++;
            }
            else
            {
                Console.WriteLine($"[DIVERSession] Node '{node.NodeId}' start failed: {node.LastError}");
            }
        }

        if (started > 0)
        {
            SetState(DIVERSessionState.Running);
        }
        return started;
    }

    /// <summary>
    /// 停止所有节点
    /// </summary>
    public int StopAll()
    {
        int stopped = 0;
        foreach (var node in _nodes.Values)
        {
            if (node.Stop())
            {
                stopped++;
            }
        }

        SetState(DIVERSessionState.Configured);
        return stopped;
    }

    private void StartStatePolling()
    {
        lock (_statePollLock)
        {
            if (_stateWorker != null && _stateWorker.IsAlive) return;
            _stateCts = new CancellationTokenSource();
            _stateWorker = new Thread(() => StateLoop(_stateCts.Token))
            {
                IsBackground = true,
                Name = "DIVERSession-State"
            };
            _stateWorker.Start();
        }
    }

    private void StopStatePolling()
    {
        lock (_statePollLock)
        {
            try
            {
                _stateCts.Cancel();
            }
            catch
            {
                // ignore
            }
            _stateWorker = null;
        }
    }

    private void StateLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            foreach (var node in _nodes.Values)
            {
                if (node.IsConnected)
                {
                    node.RefreshState();
                    
                    // 只在运行状态时刷新统计数据（避免非运行时的串口负载）
                    if (node.IsRunning)
                    {
                        node.RefreshStats();
                    }
                }
            }

            // 500ms 刷新一次状态和统计
            token.WaitHandle.WaitOne(500);
        }
    }

    #endregion

    #region Data Exchange

    /// <summary>
    /// 发送 UpperIO 数据到所有节点（从变量存储读取）
    /// </summary>
    public void BroadcastUpperIO()
    {
        foreach (var node in _nodes.Values)
        {
            if (node.IsRunning && node.CartFields.Length > 0)
            {
                // 使用全局变量存储序列化 UpperIO
                var data = HostRuntime.SerializeUpperIO(node.NodeId, node.CartFields);
                node.SendUpperIO(data);
            }
        }
    }

    /// <summary>
    /// 发送 UpperIO 数据到指定节点（从变量存储读取）
    /// </summary>
    public bool SendUpperIO(string nodeId)
    {
        var node = GetNode(nodeId);
        if (node == null || !node.IsRunning || node.CartFields.Length == 0) return false;

        // 使用全局变量存储序列化 UpperIO
        var data = HostRuntime.SerializeUpperIO(nodeId, node.CartFields);
        return node.SendUpperIO(data);
    }

    /// <summary>
    /// 设置 Cart 字段值
    /// </summary>
    public void SetCartField(string fieldName, object value)
    {
        HostRuntime.SetCartVariable(string.Empty, fieldName, value);
        if (_cartTarget == null) return;

        var field = _cartFields.FirstOrDefault(f => f.Name == fieldName);
        if (field?.ReflectionInfo != null)
        {
            field.ReflectionInfo.SetValue(
                _cartTarget,
                Convert.ChangeType(value, field.ReflectionInfo.FieldType));
        }
    }

    /// <summary>
    /// 获取 Cart 字段值
    /// </summary>
    public object? GetCartField(string fieldName)
    {
        var value = HostRuntime.GetCartVariable(string.Empty, fieldName);
        if (value != null) return value;
        if (_cartTarget == null) return null;

        var field = _cartFields.FirstOrDefault(f => f.Name == fieldName);
        return field?.ReflectionInfo?.GetValue(_cartTarget);
    }

    private void HandleLowerIO(string nodeId, byte[] data)
    {
        var node = GetNode(nodeId);
        if (node?.CartFields.Length > 0)
        {
            try
            {
                // 1. 更新到 HostRuntime 变量存储（总是执行）
                HostRuntime.DeserializeLowerIO(nodeId, data, node.CartFields);
                
                // 2. 如果有 Cart 对象，也更新到 Cart 对象
                if (_cartTarget != null)
                {
                    HostRuntime.DeserializeLowerIO(data, _cartTarget, node.CartFields);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DIVERSession] LowerIO deserialize error from {nodeId}: {ex.Message}");
            }
        }

        OnLowerIOReceived?.Invoke(nodeId, data);

        // Auto-send UpperIO back to the same node after receiving LowerIO
        if (node != null && node.IsRunning)
        {
            // Only set flag in callback, send from background thread.
            _upperPending[node.NodeId] = 1;
            _upperSignal.Set();
        }
    }

    private void UpperIOLoop()
    {
        var token = _upperCts.Token;
        while (!token.IsCancellationRequested)
        {
            _upperSignal.WaitOne(20);
            foreach (var kv in _upperPending.ToArray())
            {
                if (token.IsCancellationRequested) break;
                if (kv.Value == 0) continue;

                if (!_nodes.TryGetValue(kv.Key, out var node)) continue;
                if (!node.IsRunning) continue;

                // consume flag
                _upperPending[kv.Key] = 0;

                var upper = HostRuntime.SerializeUpperIO(node.NodeId, node.CartFields);
                node.SendUpperIO(upper, 20);
            }
        }
    }

    #endregion

    private void SetState(DIVERSessionState state)
    {
        State = state;
        OnStateChanged?.Invoke(state);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _upperCts.Cancel();
        _upperSignal.Set();
        try { _upperWorker.Join(500); } catch { }
        ClearNodes();
        _cartTarget = null;
        _cartFields = Array.Empty<CartFieldInfo>();
        SetState(DIVERSessionState.Idle);
    }
}

/// <summary>
/// 会话状态
/// </summary>
public enum DIVERSessionState
{
    /// <summary>空闲</summary>
    Idle,
    /// <summary>已配置</summary>
    Configured,
    /// <summary>运行中</summary>
    Running,
    /// <summary>错误</summary>
    Error
}

/// <summary>
/// 会话配置
/// </summary>
public class SessionConfiguration
{
    /// <summary>程序集路径（用于创建 Cart 对象，可选）</summary>
    public string? AssemblyPath { get; set; }
    
    /// <summary>节点配置列表</summary>
    public NodeConfiguration[] Nodes { get; set; } = Array.Empty<NodeConfiguration>();
}
