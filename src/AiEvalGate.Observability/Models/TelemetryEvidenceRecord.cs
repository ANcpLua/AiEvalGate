using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiEvalGate.Observability.Models;

public sealed record TelemetryEvidenceRecord
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("signalType")]
    public required string SignalType { get; init; }

    [JsonPropertyName("serviceName")]
    public required string ServiceName { get; init; }

    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    [JsonPropertyName("traceId")]
    public string? TraceId { get; init; }

    [JsonPropertyName("spanId")]
    public string? SpanId { get; init; }

    [JsonPropertyName("parentSpanId")]
    public string? ParentSpanId { get; init; }

    [JsonPropertyName("attributes")]
    public IReadOnlyDictionary<string, JsonElement> Attributes { get; init; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
