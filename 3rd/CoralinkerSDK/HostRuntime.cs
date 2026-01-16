using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using MCUSerialBridgeCLR;
using Newtonsoft.Json;

namespace CoralinkerSDK;

/// <summary>
/// Host 端运行时工具，负责 Cart 对象的加载、序列化和反序列化
/// </summary>
public static class HostRuntime
{
    #region Variable Storage

    /// <summary>
    /// 节点变量存储: nodeId -> (fieldName -> value)
    /// </summary>
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, object>> _nodeVariables = new();

    /// <summary>
    /// 初始化节点变量存储（根据 CartFields 设置默认值）
    /// </summary>
    public static void InitializeNodeVariables(string nodeId, CartFieldInfo[] fields)
    {
        var vars = _nodeVariables.GetOrAdd(nodeId, _ => new ConcurrentDictionary<string, object>());
        foreach (var field in fields)
        {
            if (!vars.ContainsKey(field.Name))
            {
                vars[field.Name] = GetDefaultValue(field.TypeId);
            }
        }
    }

    /// <summary>
    /// 获取 Cart 变量值
    /// </summary>
    public static object? GetCartVariable(string nodeId, string fieldName)
    {
        if (_nodeVariables.TryGetValue(nodeId, out var vars) && vars.TryGetValue(fieldName, out var value))
        {
            return value;
        }
        return null;
    }

    /// <summary>
    /// 获取 Cart 变量值（泛型版本）
    /// </summary>
    public static T? GetCartVariable<T>(string nodeId, string fieldName)
    {
        var value = GetCartVariable(nodeId, fieldName);
        if (value is T typed)
            return typed;
        if (value != null)
        {
            try { return (T)Convert.ChangeType(value, typeof(T)); }
            catch { }
        }
        return default;
    }

    /// <summary>
    /// 设置 Cart 变量值
    /// </summary>
    public static void SetCartVariable(string nodeId, string fieldName, object value)
    {
        var vars = _nodeVariables.GetOrAdd(nodeId, _ => new ConcurrentDictionary<string, object>());
        vars[fieldName] = value;
    }

    /// <summary>
    /// 获取节点所有变量
    /// </summary>
    public static IReadOnlyDictionary<string, object>? GetAllVariables(string nodeId)
    {
        return _nodeVariables.TryGetValue(nodeId, out var vars) ? vars : null;
    }

    /// <summary>
    /// 清除节点变量
    /// </summary>
    public static void ClearNodeVariables(string nodeId)
    {
        _nodeVariables.TryRemove(nodeId, out _);
    }

    /// <summary>
    /// 清除所有变量
    /// </summary>
    public static void ClearAllVariables()
    {
        _nodeVariables.Clear();
    }

    #endregion

    #region Default Port Configuration

    /// <summary>
    /// 默认端口配置
    /// </summary>
    public static PortConfig[] DefaultPortConfigs => new PortConfig[]
    {
        new SerialPortConfig(9600, 20),  // Port 0: RS485-1
        new SerialPortConfig(9600, 20),  // Port 1: RS485-2
        new SerialPortConfig(9600, 20),  // Port 2: RS485-3
        new SerialPortConfig(9600, 20),  // Port 3: RS232-1
        new CANPortConfig(500000, 10),   // Port 4: CAN1
        new CANPortConfig(500000, 10),   // Port 5: CAN2
    };

    #endregion

    /// <summary>
    /// 从程序集路径创建 Cart 对象
    /// </summary>
    /// <param name="assemblyPath">程序集 DLL 路径</param>
    /// <returns>Cart 对象实例，如果路径为空则返回 null</returns>
    public static object? CreateCartTarget(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return null;
        if (!File.Exists(assemblyPath))
            throw new InvalidOperationException($"Assembly not found: {assemblyPath}");

        var dir = Path.GetDirectoryName(assemblyPath)!;
        var alc = new IsolatedLoadContext(dir);
        var asm = alc.LoadFromAssemblyPath(assemblyPath);

        // 启发式查找: 优先选择名称以 "Vehicle" 结尾的类型，否则选择第一个可实例化的类
        var type =
            asm.GetTypes().FirstOrDefault(t => t is { IsAbstract: false, IsClass: true } 
                && t.Name.EndsWith("Vehicle", StringComparison.OrdinalIgnoreCase))
            ?? asm.GetTypes().First(t => t is { IsAbstract: false, IsClass: true } 
                && t.GetConstructor(Type.EmptyTypes) != null);

        return Activator.CreateInstance(type)!;
    }

    /// <summary>
    /// 解析 MetaJson 获取字段信息
    /// </summary>
    /// <param name="metaJson">字段元数据 JSON 字符串</param>
    /// <returns>字段信息数组</returns>
    public static CartFieldInfo[] ParseMetaJson(string metaJson)
    {
        var entries = JsonConvert.DeserializeObject<MetaEntry[]>(metaJson);
        if (entries == null) return Array.Empty<CartFieldInfo>();

        return entries.Select(e => new CartFieldInfo
        {
            Name = e.field ?? "",
            Offset = e.offset,
            TypeId = (byte)e.typeid,
            Flags = (byte)e.flags
        }).ToArray();
    }

    /// <summary>
    /// 绑定 Cart 字段的反射信息
    /// </summary>
    public static void BindCartFields(object cartTarget, CartFieldInfo[] fields)
    {
        var type = cartTarget.GetType();
        foreach (var field in fields)
        {
            field.ReflectionInfo = type.GetField(field.Name, 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            
            if (field.ReflectionInfo == null)
            {
                Console.WriteLine($"[HostRuntime] Warning: Field '{field.Name}' not found in {type.FullName}");
            }
        }
    }

    /// <summary>
    /// 反序列化 LowerIO 数据到 Cart 对象
    /// </summary>
    /// <param name="data">从 MCU 接收的 LowerIO 字节数据</param>
    /// <param name="cartTarget">Cart 对象</param>
    /// <param name="fields">字段信息（已绑定反射信息）</param>
    public static void DeserializeLowerIO(byte[] data, object cartTarget, CartFieldInfo[] fields)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        // LowerIO buffer starts with iteration (int32)
        if (ms.Length >= 4)
        {
            br.ReadInt32();
        }

        foreach (var field in fields)
        {
            // 跳过 UpperIO 字段（LowerIO 数据中不包含）
            if (field.IsUpperIO) continue;
            if (ms.Position >= data.Length) break;
            if (field.ReflectionInfo == null) continue;

            var typeid = br.ReadByte();
            var value = ReadTypedValue(br, typeid);
            if (value != null)
            {
                field.ReflectionInfo.SetValue(cartTarget, value);
            }
        }
    }

    /// <summary>
    /// 序列化 Cart 对象为 UpperIO 数据
    /// </summary>
    /// <param name="cartTarget">Cart 对象</param>
    /// <param name="fields">字段信息（已绑定反射信息）</param>
    /// <returns>UpperIO 字节数据</returns>
    public static byte[] SerializeUpperIO(object cartTarget, CartFieldInfo[] fields)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        foreach (var field in fields)
        {
            // 跳过 LowerIO 字段（UpperIO 数据中不包含）
            if (field.IsLowerIO) continue;
            if (field.ReflectionInfo == null) continue;

            bw.Write(field.TypeId);
            var val = field.ReflectionInfo.GetValue(cartTarget);
            WriteTypedValue(bw, field.TypeId, val);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// 序列化 UpperIO 数据（从变量存储中读取）
    /// </summary>
    /// <param name="nodeId">节点 ID</param>
    /// <param name="fields">字段信息</param>
    /// <returns>UpperIO 字节数据</returns>
    public static byte[] SerializeUpperIO(string nodeId, CartFieldInfo[] fields)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        foreach (var field in fields)
        {
            // 只序列化 UpperIO 字段
            if (!field.IsUpperIO && !field.IsMutual) continue;

            bw.Write(field.TypeId);
            var val = GetCartVariable(nodeId, field.Name) ?? GetDefaultValue(field.TypeId);
            WriteTypedValue(bw, field.TypeId, val);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// 反序列化 LowerIO 数据（写入变量存储）
    /// </summary>
    /// <param name="nodeId">节点 ID</param>
    /// <param name="data">LowerIO 字节数据</param>
    /// <param name="fields">字段信息</param>
    /// <returns>解析结果（字段名 -> 值）</returns>
    public static Dictionary<string, object> DeserializeLowerIO(string nodeId, byte[] data, CartFieldInfo[] fields)
    {
        var result = new Dictionary<string, object>();
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        // LowerIO buffer starts with iteration (int32)
        if (ms.Length >= 4)
        {
            var iteration = br.ReadInt32();
            result["__iteration"] = iteration;
            SetCartVariable(nodeId, "__iteration", iteration);
        }

        foreach (var field in fields)
        {
            // 只反序列化 LowerIO 字段
            if (field.IsUpperIO) continue;
            if (ms.Position >= data.Length) break;

            var typeid = br.ReadByte();
            var value = ReadTypedValue(br, typeid);
            if (value != null)
            {
                result[field.Name] = value;
                SetCartVariable(nodeId, field.Name, value);
            }
        }

        return result;
    }

    /// <summary>
    /// 格式化 LowerIO 数据为可读字符串
    /// </summary>
    public static string FormatLowerIO(string nodeId, byte[] data, CartFieldInfo[] fields)
    {
        var values = DeserializeLowerIO(nodeId, data, fields);
        var sb = new StringBuilder();
        var first = true;
        if (values.TryGetValue("__iteration", out var iteration))
        {
            sb.Append($"iteration={iteration}");
            first = false;
        }
        foreach (var kv in values)
        {
            if (kv.Key == "__iteration") continue;
            if (!first) sb.Append(", ");
            sb.Append($"{kv.Key}={kv.Value}");
            first = false;
        }
        return sb.ToString();
    }

    /// <summary>
    /// 格式化 UpperIO 当前值为可读字符串
    /// </summary>
    public static string FormatUpperIO(string nodeId, CartFieldInfo[] fields)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var field in fields)
        {
            if (!field.IsUpperIO && !field.IsMutual) continue;
            var val = GetCartVariable(nodeId, field.Name);
            if (!first) sb.Append(", ");
            sb.Append($"{field.Name}={val}");
            first = false;
        }
        return sb.ToString();
    }

    private static object? ReadTypedValue(BinaryReader br, int typeid)
    {
        return typeid switch
        {
            0 => br.ReadBoolean(),
            1 => br.ReadByte(),
            2 => br.ReadSByte(),
            3 => br.ReadChar(),
            4 => br.ReadInt16(),
            5 => br.ReadUInt16(),
            6 => br.ReadInt32(),
            7 => br.ReadUInt32(),
            8 => br.ReadSingle(),
            _ => null
        };
    }

    private static void WriteTypedValue(BinaryWriter bw, int typeid, object? val)
    {
        val ??= GetDefaultValue(typeid);
        switch (typeid)
        {
            case 0: bw.Write(Convert.ToBoolean(val)); break;
            case 1: bw.Write(Convert.ToByte(val)); break;
            case 2: bw.Write(Convert.ToSByte(val)); break;
            case 3: bw.Write(Convert.ToChar(val)); break;
            case 4: bw.Write(Convert.ToInt16(val)); break;
            case 5: bw.Write(Convert.ToUInt16(val)); break;
            case 6: bw.Write(Convert.ToInt32(val)); break;
            case 7: bw.Write(Convert.ToUInt32(val)); break;
            case 8: bw.Write(Convert.ToSingle(val)); break;
        }
    }

    /// <summary>
    /// 获取类型默认值
    /// </summary>
    public static object GetDefaultValue(int typeid)
    {
        return typeid switch
        {
            0 => false,
            1 => (byte)0,
            2 => (sbyte)0,
            3 => '\0',
            4 => (short)0,
            5 => (ushort)0,
            6 => 0,
            7 => 0u,
            8 => 0f,
            _ => 0
        };
    }

    /// <summary>
    /// 获取类型名称
    /// </summary>
    public static string GetTypeName(int typeid)
    {
        return typeid switch
        {
            0 => "Boolean",
            1 => "Byte",
            2 => "SByte",
            3 => "Char",
            4 => "Int16",
            5 => "UInt16",
            6 => "Int32",
            7 => "UInt32",
            8 => "Single",
            _ => $"Unknown({typeid})"
        };
    }

    /// <summary>
    /// 隔离加载上下文，用于加载 Cart 程序集
    /// </summary>
    private sealed class IsolatedLoadContext : AssemblyLoadContext
    {
        private readonly string _dir;

        public IsolatedLoadContext(string dir) : base(isCollectible: true)
        {
            _dir = dir;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var candidate = Path.Combine(_dir, $"{assemblyName.Name}.dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }

    /// <summary>
    /// MetaJson 条目
    /// </summary>
    private class MetaEntry
    {
        public string? field { get; set; }
        public int typeid { get; set; }
        public int offset { get; set; }
        public int flags { get; set; }
    }
}
