using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiEvalGate.Observability.Models;

/// <summary>
/// Captured record of a single agent run that the deterministic observability evaluation suite assesses.
/// It bundles what the agent did (its identity, the user input, the tool calls it made, and its final
/// response) together with the telemetry it emitted and the per-scenario expectations the evaluators check
/// it against.
/// </summary>
/// <remarks>
/// Deserialized from one JSON line of a JSONL fixture by <c>ScenarioLoader.LoadJsonl</c> using the
/// <see cref="JsonPropertyNameAttribute"/> mappings on each property. The same record instance is injected
/// into all four built-in deterministic evaluators
/// (<c>TelemetryEvidenceEvaluator</c>, <c>ToolCallAccuracyEvaluator</c>, <c>TraceCorrelationEvaluator</c>,
/// and <c>CardinalitySafetyEvaluator</c>), each of which computes its boolean verdict purely from these
/// fields with no model call — keeping evaluation on the deterministic, AI-only side of the gate and making
/// every verdict fully reproducible for a given record. <c>ScenarioRunResult.Create</c> then compares the
/// actually-failed metric names against <see cref="ExpectedFailedMetrics"/> and cross-checks that against
/// <see cref="ShouldPass"/> to decide whether the run matched its declared expectation.
/// </remarks>
public sealed record ObservabilityEvaluationRecord
{
    /// <summary>
    /// Stable identifier for this record. Flows through to <c>ScenarioRunResult.Id</c> and is used to
    /// identify the scenario in evaluation output.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Provenance label describing where the record originated (for example the evaluation harness or
    /// fixture set that produced it).
    /// </summary>
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    /// <summary>
    /// Human-readable name of the scenario the agent was exercised under (for example the incident or
    /// situation being diagnosed).
    /// </summary>
    [JsonPropertyName("scenario")]
    public required string Scenario { get; init; }

    /// <summary>
    /// Identity and configuration of the agent under evaluation: its name, model provider and model name,
    /// system instructions, and the tools it was allowed to use.
    /// </summary>
    [JsonPropertyName("agent")]
    public required AgentInfo Agent { get; init; }

    /// <summary>
    /// The user's input prompt that the agent was asked to respond to in this run.
    /// </summary>
    [JsonPropertyName("userInput")]
    public required string UserInput { get; init; }

    /// <summary>
    /// The sequence of tool calls the agent actually made during the run. Defaults to an empty list.
    /// <c>ToolCallAccuracyEvaluator</c> matches each entry in <see cref="ExpectedToolCalls"/> against this
    /// list (tool names compared case-insensitively, expected arguments required as a deep-equal subset).
    /// </summary>
    [JsonPropertyName("toolCalls")]
    public IReadOnlyList<ToolCallRecord> ToolCalls { get; init; } = [];

    /// <summary>
    /// The agent's final natural-language response to the user. <c>TelemetryEvidenceEvaluator</c> scans this
    /// text (case-insensitive substring matching) to confirm every required evidence id is cited and no
    /// forbidden claim appears.
    /// </summary>
    [JsonPropertyName("finalResponse")]
    public required string FinalResponse { get; init; }

    /// <summary>
    /// The telemetry evidence captured during the run — OpenTelemetry signals such as metrics, spans, and
    /// logs, each carrying its signal type, service name, operation, trace/span/parent-span identifiers, and
    /// attributes. Defaults to an empty list. This is the evidence pool the telemetry, trace-correlation, and
    /// cardinality-safety evaluators inspect.
    /// </summary>
    [JsonPropertyName("telemetry")]
    public IReadOnlyList<TelemetryEvidenceRecord> Telemetry { get; init; } = [];

    /// <summary>
    /// The evidence ids that must both exist in <see cref="Telemetry"/> and be cited in
    /// <see cref="FinalResponse"/>. <c>TelemetryEvidenceEvaluator</c> fails the record when any required id
    /// is missing from the captured telemetry or absent from the response text. Defaults to an empty list.
    /// </summary>
    [JsonPropertyName("requiredEvidenceIds")]
    public IReadOnlyList<string> RequiredEvidenceIds { get; init; } = [];

    /// <summary>
    /// Claim strings the agent must not assert. <c>TelemetryEvidenceEvaluator</c> fails the record when any
    /// of these appears (case-insensitive substring) in <see cref="FinalResponse"/>. Defaults to an empty
    /// list.
    /// </summary>
    [JsonPropertyName("forbiddenClaims")]
    public IReadOnlyList<string> ForbiddenClaims { get; init; } = [];

    /// <summary>
    /// The tool calls the agent is expected to have made, each with a name and an expected argument subset.
    /// <c>ToolCallAccuracyEvaluator</c> requires every entry here to be matched by an actual call in
    /// <see cref="ToolCalls"/>. Defaults to an empty list.
    /// </summary>
    [JsonPropertyName("expectedToolCalls")]
    public IReadOnlyList<ExpectedToolCallRecord> ExpectedToolCalls { get; init; } = [];

    /// <summary>
    /// The agent's structured (machine-consumable) output for this run, if any, as a raw JSON value.
    /// <c>StructuredOutputConformanceEvaluator</c> validates it against <see cref="OutputContract"/>.
    /// Absent (<see langword="null"/>) when the run produced no structured output to check.
    /// </summary>
    [JsonPropertyName("structuredOutput")]
    public JsonElement? StructuredOutput { get; init; }

    /// <summary>
    /// The contract that <see cref="StructuredOutput"/> must satisfy: the required properties (with their
    /// JSON value kinds) and the forbidden properties. When <see langword="null"/> or empty, the
    /// structured-output conformance check passes vacuously. Defaults to <see langword="null"/>.
    /// </summary>
    [JsonPropertyName("outputContract")]
    public OutputContract? OutputContract { get; init; }

    /// <summary>
    /// The metric names (for example <c>observability.telemetry.evidence</c> or
    /// <c>observability.trace.correlation</c>) that this record is expected to fail. <c>ScenarioRunResult.Create</c>
    /// compares the set of metrics that actually failed against this list and records a mismatch when they
    /// differ. Defaults to an empty list; a passing record (<see cref="ShouldPass"/> is <see langword="true"/>)
    /// is expected to leave this empty, while a failing record is expected to populate it.
    /// </summary>
    [JsonPropertyName("expectedFailedMetrics")]
    public IReadOnlyList<string> ExpectedFailedMetrics { get; init; } = [];

    /// <summary>
    /// Whether this record is expected to pass all observability metrics. <c>ScenarioRunResult.Create</c>
    /// treats it as a consistency invariant: a <see langword="true"/> value should pair with an empty
    /// <see cref="ExpectedFailedMetrics"/>, and a <see langword="false"/> value should pair with a non-empty
    /// one; any deviation is reported as a mismatch.
    /// </summary>
    [JsonPropertyName("shouldPass")]
    public required bool ShouldPass { get; init; }
}
