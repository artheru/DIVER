using System.Text.Json;

namespace CoralinkerHost.Web;

/// <summary>
/// 统一的 JSON 序列化配置
/// C# 使用 PascalCase，JavaScript 使用 camelCase
/// </summary>
public static class JsonHelper
{
    /// <summary>
    /// 全局 JSON 序列化选项
    /// - 序列化时：PascalCase → camelCase
    /// - 反序列化时：不区分大小写
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 序列化对象为 JSON 字符串
    /// </summary>
    public static string Serialize<T>(T obj)
    {
        return JsonSerializer.Serialize(obj, Options);
    }

    /// <summary>
    /// 反序列化 JSON 字符串为对象
    /// </summary>
    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }

    /// <summary>
    /// 返回 JSON 结果（用于 Minimal API）
    /// </summary>
    public static IResult Json<T>(T obj)
    {
        return Results.Json(obj, Options);
    }
}
