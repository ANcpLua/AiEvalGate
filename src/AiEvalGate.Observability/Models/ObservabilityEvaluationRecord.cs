using System.Text.Json.Serialization;

namespace AiEvalGate.Observability.Models;

public sealed record ObservabilityEvaluationRecord
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("scenario")]
    public required string Scenario { get; init; }

    [JsonPropertyName("agent")]
    public required AgentInfo Agent { get; init; }

    [JsonPropertyName("userInput")]
    public required string UserInput { get; init; }

    [JsonPropertyName("toolCalls")]
    public IReadOnlyList<ToolCallRecord> ToolCalls { get; init; } = [];

    [JsonPropertyName("finalResponse")]
    public required string FinalResponse { get; init; }

    [JsonPropertyName("telemetry")]
    public IReadOnlyList<TelemetryEvidenceRecord> Telemetry { get; init; } = [];

    [JsonPropertyName("requiredEvidenceIds")]
    public IReadOnlyList<string> RequiredEvidenceIds { get; init; } = [];

    [JsonPropertyName("forbiddenClaims")]
    public IReadOnlyList<string> ForbiddenClaims { get; init; } = [];

    [JsonPropertyName("expectedToolCalls")]
    public IReadOnlyList<ExpectedToolCallRecord> ExpectedToolCalls { get; init; } = [];

    [JsonPropertyName("expectedFailedMetrics")]
    public IReadOnlyList<string> ExpectedFailedMetrics { get; init; } = [];

    [JsonPropertyName("shouldPass")]
    public required bool ShouldPass { get; init; }
}
