using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiEvalGate.Observability.Models;

/// <summary>
/// Captures a single tool call an agent actually made during a run, forming the observed
/// side of the <c>ToolCallAccuracy</c> observability metric.
/// </summary>
/// <remarks>
/// These records are the actual calls that <see cref="Evaluators.ToolCallAccuracyEvaluator"/>
/// matches each <see cref="ExpectedToolCallRecord"/> against. A record matches an expectation when
/// its <see cref="Name"/> equals the expected name case-insensitively and its <see cref="Arguments"/>
/// contain the expected argument subset; extra arguments are ignored. Each actual call is matched to
/// at most one expectation. This model describes the agent's tool usage only; it does not carry any
/// pass/fail or severity judgement, which is produced by the evaluators rather than stored here.
/// </remarks>
public sealed record ToolCallRecord
{
    /// <summary>
    /// Gets the name of the tool that was invoked.
    /// </summary>
    /// <remarks>
    /// Compared against an expectation's name case-insensitively (ordinal, ignore case), so casing
    /// differences between the captured call and the expectation do not cause a mismatch.
    /// </remarks>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the arguments the tool was actually called with.
    /// </summary>
    /// <remarks>
    /// Holds the complete set of arguments captured for the call. During matching, an expectation's
    /// arguments are treated as a required subset of this dictionary: each expected key is looked up
    /// case-insensitively and its value compared with
    /// <see cref="JsonElement.DeepEquals(JsonElement, JsonElement)"/>, while any keys present here but
    /// absent from the expectation are ignored. Defaults to an empty, ordinal-keyed dictionary.
    /// </remarks>
    [JsonPropertyName("arguments")]
    public IReadOnlyDictionary<string, JsonElement> Arguments { get; init; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    /// <summary>
    /// Gets an optional human-readable summary of the tool call's result, or <see langword="null"/> when none was captured.
    /// </summary>
    /// <remarks>
    /// Descriptive metadata only; it does not participate in tool-call accuracy matching, which
    /// compares only <see cref="Name"/> and <see cref="Arguments"/>.
    /// </remarks>
    [JsonPropertyName("resultSummary")]
    public string? ResultSummary { get; init; }
}
