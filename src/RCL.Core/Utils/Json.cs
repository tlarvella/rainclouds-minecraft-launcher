using System.Text.Json;

namespace RCL.Core.Utils;

/// <summary>统一的 JSON 序列化选项。</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
