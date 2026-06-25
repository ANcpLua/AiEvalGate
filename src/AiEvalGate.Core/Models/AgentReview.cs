namespace AiEvalGate.Core.Models;

/// <summary>
/// The structured verdict a single AI reviewer agent returns for one evaluation case.
/// </summary>
/// <remarks>
/// Instances are produced by an AI reviewer (see <c>AiReviewerAgent.ReviewAsync</c>) by
/// deserializing the strict JSON object the LLM judge emits, and are then consumed by
/// <c>AiEvalGatekeeper.Evaluate</c> to decide whether the gate passes. The pipeline is
/// AI-only: a reviewer is never allowed to defer to a human, so every review always carries
/// a definitive <see cref="Passed"/> decision rather than an "abstain" state. Properties map
/// one-to-one onto the camelCase JSON fields the reviewer prompt mandates
/// (<c>reviewer</c>, <c>passed</c>, <c>score</c>, <c>severity</c>, <c>findings</c>,
/// <c>rationale</c>, <c>metrics</c>).
/// </remarks>
public sealed record AgentReview
{
    /// <summary>
    /// The name of the reviewer agent that produced this review (for example
    /// <c>SafetyReviewer</c> or <c>ArchitectureReviewer</c>).
    /// </summary>
    /// <remarks>
    /// The gatekeeper matches this value case-insensitively against the policy's required
    /// reviewers, so a missing or mismatched name causes a "missing required reviewer" blocker.
    /// The reviewer agent overwrites whatever name the model returned with its own canonical
    /// <c>Name</c> before handing the review back, keeping the reported reviewer authoritative.
    /// </remarks>
    public required string Reviewer { get; init; }
    /// <summary>
    /// The reviewer's binary pass/fail verdict for the evaluation case: <see langword="true"/>
    /// when the reviewer accepts the result, <see langword="false"/> when it rejects it.
    /// </summary>
    /// <remarks>
    /// When the gate policy sets <c>RequireAllReviewerPasses</c>, a value of
    /// <see langword="false"/> here adds a release-blocking entry to the gate result.
    /// </remarks>
    public required bool Passed { get; init; }
    /// <summary>
    /// The reviewer's confidence score for the case, on a 0.0-to-1.0 scale where higher is better.
    /// </summary>
    /// <remarks>
    /// The gatekeeper blocks the gate whenever this score is below the policy's
    /// <c>MinReviewerScore</c> threshold.
    /// </remarks>
    public required double Score { get; init; }
    /// <summary>
    /// The highest-impact severity the reviewer assigns to the case, expressed on the
    /// shared P0-P3 scale.
    /// </summary>
    /// <remarks>
    /// Severities run from <c>P0</c> (critical blocker: unsafe, data leak, unauthorized action,
    /// severe hallucination, or broken policy), through <c>P1</c> (release blocker: materially
    /// wrong or incomplete behavior) and <c>P2</c> (non-blocking quality issue), to <c>P3</c>
    /// (minor polish issue). The gatekeeper compares this value case-insensitively against the
    /// policy's blocking severities; a match adds a blocker to the gate result.
    /// </remarks>
    public required string Severity { get; init; }
    /// <summary>
    /// The concrete issues the reviewer observed, one per entry; empty when the reviewer found
    /// nothing to report.
    /// </summary>
    /// <remarks>
    /// When a review blocks the gate (for example via a blocking severity or a failed pass),
    /// the gatekeeper joins these findings into the human-readable blocker message.
    /// </remarks>
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();
    /// <summary>
    /// The reviewer's free-text explanation of how it reached its verdict; empty when no
    /// rationale was supplied.
    /// </summary>
    public string Rationale { get; init; } = string.Empty;
    /// <summary>
    /// The reviewer's self-reported sub-scores, keyed by dimension name (for example
    /// <c>grounding</c>, <c>safety</c>, <c>architecture</c>, and <c>toolUse</c>) with values
    /// on the same 0.0-to-1.0 scale as <see cref="Score"/>.
    /// </summary>
    /// <remarks>
    /// Keys are compared case-insensitively (<see cref="StringComparer.OrdinalIgnoreCase"/>),
    /// so callers can look up a dimension regardless of casing. These are the reviewer's own
    /// breakdown and are distinct from the pipeline's deterministic evaluator metric scores.
    /// </remarks>
    public IReadOnlyDictionary<string, double> Metrics { get; init; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
}
