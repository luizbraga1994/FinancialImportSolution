using System.Text.Json;
using FinancialImport.Application.Messaging;

namespace FinancialImport.Infrastructure.Messaging;

/// <summary>
/// Default JSON serializer backed by <see cref="System.Text.Json"/>.
/// Uses a shared, case-insensitive, camelCase-friendly configuration to
/// stay compatible with non-.NET consumers.
/// </summary>
public sealed class JsonMessageSerializer : IMessageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public byte[] Serialize<T>(T payload) where T : class
        => JsonSerializer.SerializeToUtf8Bytes(payload, Options);

    public T Deserialize<T>(ReadOnlySpan<byte> payload) where T : class
        => JsonSerializer.Deserialize<T>(payload, Options)
           ?? throw new InvalidOperationException($"Failed to deserialize payload to {typeof(T).FullName}.");

    public object Deserialize(ReadOnlySpan<byte> payload, Type type)
        => JsonSerializer.Deserialize(payload, type, Options)
           ?? throw new InvalidOperationException($"Failed to deserialize payload to {type.FullName}.");
}
