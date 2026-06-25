using System.Text.Json.Serialization;

namespace AiEvalGate.Observability.Models;

/// <summary>
/// A happens-before ordering constraint between two tool calls: every call to <see cref="After"/>
/// must be preceded by at least one call to <see cref="Before"/>. Checked deterministically by
/// <c>ToolCallOrderingEvaluator</c> against the recorded tool-call sequence.
/// </summary>
/// <remarks>
/// Models real operational-safety requirements such as "authorize before charging" or
/// "retrieve before answering". Because it is decided purely from the order of the recorded calls,
/// the verdict is exact and reproducible — no model judgement.
/// </remarks>
public sealed record ToolOrderConstraint
{
    /// <summary>
    /// The tool that must occur first. A call to <see cref="After"/> with no earlier call to this
    /// tool is a violation. Compared to recorded tool names case-insensitively.
    /// </summary>
    [JsonPropertyName("before")]
    public required string Before { get; init; }

    /// <summary>
    /// The tool that must not occur until <see cref="Before"/> has occurred. Compared to recorded
    /// tool names case-insensitively. If this tool never occurs, the constraint is satisfied vacuously.
    /// </summary>
    [JsonPropertyName("after")]
    public required string After { get; init; }
}
