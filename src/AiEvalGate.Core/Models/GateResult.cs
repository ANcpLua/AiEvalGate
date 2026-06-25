using System.Text;

namespace AiEvalGate.Core.Models;

/// <summary>
/// The aggregated AI-only gate decision for a single scenario, produced by
/// <see cref="AiEvalGate.Core.Evaluation.AiEvalGatekeeper.Evaluate"/> after combining metric
/// scores, agent reviewer verdicts, and service-boundary checks. It is the verdict record the
/// runner, the report/artifact writer, and the JUnit/summary outputs consume.
/// </summary>
/// <remarks>
/// The result is the rendered counterpart to the un-scored <see cref="AiRunResult"/>: it carries
/// the pass/fail outcome plus the human-readable blocker and warning messages collected during
/// evaluation. Consistent with the AI-only policy invariant the gatekeeper enforces, a broken
/// invariant (human review required, manual override allowed, or non-zero manual-approval steps)
/// surfaces here as a blocker.
/// </remarks>
public sealed record GateResult
{
    /// <summary>
    /// Identifier of the scenario this gate decision belongs to; set by the gatekeeper from the
    /// scenario's <c>Id</c> and used to correlate the result with its run and report artifacts.
    /// </summary>
    public required string ScenarioId { get; init; }
    /// <summary>
    /// Whether the scenario cleared the gate. Set by the gatekeeper to <see langword="true"/> only
    /// when no blockers were collected, and to <see langword="false"/> as soon as any blocker exists.
    /// </summary>
    public required bool Passed { get; init; }
    /// <summary>
    /// The gate-failing reasons accumulated during evaluation (AI-only policy violations, missing
    /// required reviewers, blocking reviewer verdicts or scores, metric minimums not met, and
    /// service-boundary failures under strict mode). A non-empty list forces <see cref="Passed"/>
    /// to <see langword="false"/>. Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Non-blocking advisories that do not fail the gate, such as service-boundary failures
    /// downgraded to warnings when strict boundary mode is off. Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Renders a human-readable summary of the gate outcome for console and report output. When the
    /// scenario passed, returns a single confirmation line; otherwise returns a multi-line message
    /// headed with the AI-only-gate failure for this scenario, followed by one line per blocker.
    /// Warnings are not included.
    /// </summary>
    /// <returns>
    /// <c>"Scenario {ScenarioId} passed."</c> when <see cref="Passed"/> is <see langword="true"/>;
    /// otherwise a multi-line string beginning with the failure header and listing each entry of
    /// <see cref="Blockers"/>.
    /// </returns>
    public string ToFailureMessage()
    {
        if (Passed) return $"Scenario {ScenarioId} passed.";

        var sb = new StringBuilder();
        sb.AppendLine($"Scenario {ScenarioId} failed AI-only gates:");
        foreach (string blocker in Blockers)
        {
            sb.AppendLine($"- {blocker}");
        }

        return sb.ToString();
    }
}
