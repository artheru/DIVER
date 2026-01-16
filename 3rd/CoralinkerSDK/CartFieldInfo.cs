using System.Reflection;

namespace CoralinkerSDK;

/// <summary>
/// Cart 字段信息，用于 UpperIO/LowerIO 序列化
/// </summary>
public class CartFieldInfo
{
    /// <summary>字段名称</summary>
    public string Name { get; set; } = "";
    
    /// <summary>字段在 Cart 内存中的偏移</summary>
    public int Offset { get; set; }
    
    /// <summary>类型 ID (0=bool, 1=byte, 2=sbyte, 3=char, 4=short, 5=ushort, 6=int, 7=uint, 8=float)</summary>
    public byte TypeId { get; set; }
    
    /// <summary>IO 标志: 0x01=UpperIO, 0x02=LowerIO, 0x00=Mutual</summary>
    public byte Flags { get; set; }
    
    /// <summary>运行时绑定的反射信息</summary>
    internal FieldInfo? ReflectionInfo { get; set; }
    
    /// <summary>是否为 UpperIO (Host -> MCU)</summary>
    public bool IsUpperIO => (Flags & 0x01) != 0;
    
    /// <summary>是否为 LowerIO (MCU -> Host)</summary>
    public bool IsLowerIO => (Flags & 0x02) != 0;
    
    /// <summary>是否为 Mutual (双向)</summary>
    public bool IsMutual => Flags == 0x00;
    
    /// <summary>是否应在 UpperIO 中发送 (UpperIO 或 Mutual)</summary>
    public bool ShouldSendUpper => IsUpperIO || IsMutual;
    
    /// <summary>是否应从 LowerIO 接收 (LowerIO 或 Mutual)</summary>
    public bool ShouldReceiveLower => IsLowerIO || IsMutual;
}
