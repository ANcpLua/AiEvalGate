using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiEvalGate.Observability.Models;

/// <summary>
/// One captured piece of OpenTelemetry evidence (a span, metric, or log) produced during an agent run and
/// later inspected by the deterministic observability evaluators.
/// </summary>
/// <remarks>
/// Instances are deserialized from the <c>telemetry</c> array of an
/// <see cref="ObservabilityEvaluationRecord"/> and consumed only by non-LLM, fail-closed evaluators, in
/// keeping with the harness's AI-only policy invariant that observability verdicts are computed purely from
/// the captured record without any model call. <see cref="AiEvalGate.Observability.Evaluators.TelemetryEvidenceEvaluator"/>
/// matches <see cref="Id"/> against the scenario's required evidence ids and citations;
/// <see cref="AiEvalGate.Observability.Evaluators.TraceCorrelationEvaluator"/> selects records whose
/// <see cref="SignalType"/> is <c>"span"</c> and validates their trace/span correlation; and
/// <see cref="AiEvalGate.Observability.Evaluators.CardinalitySafetyEvaluator"/> scans <see cref="Attributes"/>
/// for high-cardinality or sensitive content.
/// </remarks>
public sealed record TelemetryEvidenceRecord
{
    /// <summary>
    /// Stable identifier of this evidence item. Used as the evidence id that a scenario can require via
    /// <c>requiredEvidenceIds</c>: it must both exist in the captured telemetry and appear verbatim (matched
    /// case-insensitively) in the agent's final response for the telemetry-evidence check to pass.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// The OpenTelemetry signal kind this record represents, such as <c>span</c>, <c>metric</c>, or <c>log</c>.
    /// Trace-correlation validation considers only records whose value equals <c>"span"</c>, compared
    /// case-insensitively.
    /// </summary>
    [JsonPropertyName("signalType")]
    public required string SignalType { get; init; }

    /// <summary>
    /// Name of the service that emitted this telemetry, capturing the OpenTelemetry service identity of the
    /// signal's origin.
    /// </summary>
    [JsonPropertyName("serviceName")]
    public required string ServiceName { get; init; }

    /// <summary>
    /// Name of the operation this telemetry describes, for example the span or activity name of the work the
    /// signal records.
    /// </summary>
    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    /// <summary>
    /// The OpenTelemetry trace identifier this record belongs to. Optional because non-span signals may omit
    /// it; for span records trace correlation requires it to be present and non-empty, and combined with
    /// <see cref="SpanId"/> it must form a unique key across the run's spans.
    /// </summary>
    [JsonPropertyName("traceId")]
    public string? TraceId { get; init; }

    /// <summary>
    /// The OpenTelemetry span identifier of this record. Optional because non-span signals may omit it; for
    /// span records trace correlation requires it to be present and non-empty, and together with
    /// <see cref="TraceId"/> it must be unique among the run's spans.
    /// </summary>
    [JsonPropertyName("spanId")]
    public string? SpanId { get; init; }

    /// <summary>
    /// The span identifier of this span's parent, or <see langword="null"/> for a root span. When present,
    /// trace correlation requires it to resolve to another span sharing the same <see cref="TraceId"/>;
    /// a reference with no matching parent in the trace is reported as a correlation failure.
    /// </summary>
    [JsonPropertyName("parentSpanId")]
    public string? ParentSpanId { get; init; }

    /// <summary>
    /// The OpenTelemetry attributes attached to this signal, keyed by attribute name with raw JSON values.
    /// Defaults to an empty, ordinally-compared dictionary. These are scanned by the cardinality-safety check,
    /// which fails when a key or value looks high-cardinality or sensitive (for example secrets, bearer tokens,
    /// raw prompts, or email addresses).
    /// </summary>
    [JsonPropertyName("attributes")]
    public IReadOnlyDictionary<string, JsonElement> Attributes { get; init; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
