using MCUSerialBridgeCLR;

namespace CoralinkerSDK;

/// <summary>
/// 会话配置（用于从 JSON 加载）
/// </summary>
public class SessionConfiguration
{
    /// <summary>程序集路径（可选）</summary>
    public string? AssemblyPath { get; set; }
    
    /// <summary>节点配置列表</summary>
    public NodeConfiguration[] Nodes { get; set; } = Array.Empty<NodeConfiguration>();
}

/// <summary>
/// MCU 节点配置
/// </summary>
public class NodeConfiguration
{
    /// <summary>节点唯一标识</summary>
    public string NodeId { get; set; } = "";
    
    /// <summary>MCU 串口 URI (如 "COM3" 或 "serial://name=COM3&baudrate=2000000")</summary>
    public string McuUri { get; set; } = "";
    
    /// <summary>DIVER 程序字节码</summary>
    public byte[] ProgramBytes { get; set; } = Array.Empty<byte>();
    
    /// <summary>Cart 字段元数据 JSON</summary>
    public string MetaJson { get; set; } = "";
    
    /// <summary>端口配置（可选，默认使用 HostRuntime.DefaultPortConfigs）</summary>
    public PortConfig[]? PortConfigs { get; set; }
    
    /// <summary>DIVER 源码（调试用，可选）</summary>
    public string? DiverSrc { get; set; }
    
    /// <summary>DIVER 映射 JSON（调试用，可选）</summary>
    public string? DiverMapJson { get; set; }
    
    /// <summary>逻辑名称（调试用，可选）</summary>
    public string? LogicName { get; set; }
}
