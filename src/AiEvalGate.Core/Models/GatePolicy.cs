using System.Text.Json;
using AiEvalGate.Core;

namespace AiEvalGate.Core.Models;

/// <summary>
/// Declarative configuration for the AI-only release gate, supplying the reviewer roster,
/// metric thresholds, and severity rules that <see cref="AiEvalGate.Core.Evaluation.AiEvalGatekeeper"/>
/// evaluates a scenario against to produce a pass/fail decision.
/// </summary>
/// <remarks>
/// The policy is loaded from JSON via <see cref="Load(string)"/> using the shared
/// <see cref="AiEvalGate.Core.JsonOptions.Default"/> options. It encodes an AI-only invariant:
/// the gate enforces that no human-in-the-loop control is enabled (see <see cref="AiOnlyPolicy"/>),
/// and any such control is treated as a broken policy that blocks release.
/// </remarks>
public sealed record GatePolicy
{
    /// <summary>
    /// Reviewer names (matched case-insensitively) that must each have produced a review for the
    /// scenario; a missing reviewer from this list is recorded as a gate blocker.
    /// </summary>
    public required IReadOnlyList<string> RequiredReviewers { get; init; }
    /// <summary>
    /// Minimum required score per evaluator metric name (matched case-insensitively). A scenario may
    /// raise an individual threshold via its own <c>Thresholds</c> override; a metric whose score falls
    /// below the effective minimum, or that is missing entirely, is recorded as a gate blocker.
    /// </summary>
    public required IReadOnlyDictionary<string, double> MinMetrics { get; init; }

    /// <summary>
    /// Minimum acceptable reviewer score in <c>[0, 1]</c>; any review whose score is below this value
    /// is recorded as a gate blocker.
    /// </summary>
    public required double MinReviewerScore { get; init; }

    /// <summary>
    /// Severity labels (matched case-insensitively) that are gate-blocking when emitted by any reviewer.
    /// Severities follow the P0&#8211;P3 scale (P0 critical blocker, P1 release blocker, P2 non-blocking
    /// quality issue, P3 minor polish); listing a severity here causes a matching review to block release.
    /// </summary>
    public required IReadOnlyList<string> BlockSeverities { get; init; }

    /// <summary>
    /// When <see langword="true"/>, every reviewer must report <c>Passed = true</c>; any review with
    /// <c>Passed = false</c> is recorded as a gate blocker.
    /// </summary>
    public required bool RequireAllReviewerPasses { get; init; }

    /// <summary>
    /// When <see langword="true"/>, service-boundary contract failures are treated as gate blockers;
    /// otherwise they are downgraded to non-blocking warnings.
    /// </summary>
    public required bool ServiceBoundaryStrict { get; init; }

    /// <summary>
    /// The human-in-the-loop settings the gate inspects to enforce its AI-only invariant; any enabled
    /// human control here is treated as a broken policy that blocks release.
    /// </summary>
    public required AiOnlyPolicy AiOnlyPolicy { get; init; }

    /// <summary>
    /// Reads and deserializes a <see cref="GatePolicy"/> from a JSON file using the shared
    /// <see cref="AiEvalGate.Core.JsonOptions.Default"/> options.
    /// </summary>
    /// <param name="path">Path to the JSON gate-policy file to load.</param>
    /// <returns>The deserialized <see cref="GatePolicy"/>.</returns>
    /// <exception cref="FileNotFoundException">Thrown when no file exists at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the JSON cannot be parsed (wrapping the underlying <see cref="JsonException"/>) or
    /// when deserialization yields <see langword="null"/>.
    /// </exception>
    public static GatePolicy Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Gate policy file not found.", path);
        }

        string json = File.ReadAllText(path);
        try
        {
            return JsonSerializer.Deserialize<GatePolicy>(json, JsonOptions.Default)
                   ?? throw new InvalidOperationException($"Gate policy deserialized to null: {path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Unable to parse gate policy: {path}", ex);
        }
    }
}

/// <summary>
/// Human-in-the-loop settings inspected by the gate to enforce its AI-only invariant. For a valid
/// AI-only policy all three members must indicate no human involvement
/// (<see cref="HumanReviewRequired"/> and <see cref="ManualOverrideAllowed"/> both
/// <see langword="false"/> and <see cref="ManualApprovalSteps"/> equal to zero); any other combination
/// is treated as a broken policy that blocks release.
/// </summary>
public sealed record AiOnlyPolicy
{
    /// <summary>
    /// Whether a human review is required. Must be <see langword="false"/> under the AI-only invariant;
    /// when <see langword="true"/> the policy is considered broken and blocks release.
    /// </summary>
    public required bool HumanReviewRequired { get; init; }

    /// <summary>
    /// Whether a manual human override of the gate decision is permitted. Must be <see langword="false"/>
    /// under the AI-only invariant; when <see langword="true"/> the policy is considered broken and blocks release.
    /// </summary>
    public required bool ManualOverrideAllowed { get; init; }

    /// <summary>
    /// The number of manual human approval steps. Must be zero under the AI-only invariant; any non-zero
    /// value marks the policy as broken and blocks release.
    /// </summary>
    public required int ManualApprovalSteps { get; init; }
}
