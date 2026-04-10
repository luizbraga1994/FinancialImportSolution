namespace FinancialImport.Application.Messaging;

/// <summary>
/// Abstracts message serialization so the broker pipeline does not
/// depend on System.Text.Json directly. Enables future adoption of
/// Protobuf/Avro without touching the business layer.
/// </summary>
public interface IMessageSerializer
{
    byte[] Serialize<T>(T payload) where T : class;
    T Deserialize<T>(ReadOnlySpan<byte> payload) where T : class;
    object Deserialize(ReadOnlySpan<byte> payload, Type type);
}
