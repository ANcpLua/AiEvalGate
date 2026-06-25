using System.Text.Json.Serialization;

namespace AiEvalGate.Observability.Models;

/// <summary>
/// A declarative, model-free contract describing the shape an agent's structured output must take.
/// Consumed by <c>StructuredOutputConformanceEvaluator</c>, which checks the run's
/// <c>StructuredOutput</c> against it deterministically — no LLM judge, so the verdict is fully
/// reproducible for a given output.
/// </summary>
/// <remarks>
/// The contract intentionally captures only what can be decided exactly from the JSON itself:
/// which top-level properties must be present (and with which JSON value kind) and which must be
/// absent. It is not a full JSON Schema; it is the airtight subset that cannot be mis-evaluated.
/// </remarks>
public sealed record OutputContract
{
    /// <summary>
    /// Top-level properties the structured output must contain, mapping each property name to its
    /// required JSON value kind. Accepted kinds (case-insensitive): <c>string</c>, <c>number</c>,
    /// <c>boolean</c>, <c>object</c>, <c>array</c>, and <c>null</c>. A missing property, or one whose
    /// value kind differs, is a violation; an unrecognized expected kind never matches and so surfaces
    /// the typo as a violation. Property names are matched case-sensitively, per the JSON specification.
    /// Defaults to empty.
    /// </summary>
    [JsonPropertyName("requiredProperties")]
    public IReadOnlyDictionary<string, string> RequiredProperties { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Top-level property names that must NOT appear in the structured output — for example internal
    /// debug or trace fields the agent should never leak to a caller. Any present property named here
    /// is a violation. Defaults to empty.
    /// </summary>
    [JsonPropertyName("forbiddenProperties")]
    public IReadOnlyList<string> ForbiddenProperties { get; init; } = [];
}
