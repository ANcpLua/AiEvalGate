using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiEvalGate.Observability.Models;

/// <summary>
/// Declares a tool call that a scenario expects an agent to make, used as the assertion
/// side of the <c>ToolCallAccuracy</c> observability metric.
/// </summary>
/// <remarks>
/// Each expected record is matched against the actual <see cref="ToolCallRecord"/> entries
/// captured for a run. Matching is permissive: <see cref="Name"/> is compared
/// case-insensitively, and <see cref="Arguments"/> is treated as a required subset rather
/// than an exact replica, so an actual call may carry additional arguments and still match.
/// The accuracy metric passes only when every expected record finds a distinct matching call.
/// </remarks>
public sealed record ExpectedToolCallRecord
{
    /// <summary>
    /// Gets the name of the tool the agent is expected to invoke.
    /// </summary>
    /// <remarks>
    /// Compared against the actual tool name case-insensitively (ordinal, ignore case),
    /// so casing differences between the expectation and the captured call do not cause a mismatch.
    /// </remarks>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the subset of arguments that the matching tool call must contain.
    /// </summary>
    /// <remarks>
    /// Every key/value pair listed here must be present in the actual call's arguments for the
    /// call to match: keys are looked up case-insensitively (so expected <c>"Query"</c> matches
    /// actual <c>"query"</c>) and values are compared with <see cref="JsonElement.DeepEquals(JsonElement, JsonElement)"/>.
    /// Arguments present on the actual call but absent here are ignored. Defaults to an empty,
    /// ordinal-keyed dictionary, which matches any call with the expected <see cref="Name"/> regardless of its arguments.
    /// </remarks>
    [JsonPropertyName("arguments")]
    public IReadOnlyDictionary<string, JsonElement> Arguments { get; init; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
