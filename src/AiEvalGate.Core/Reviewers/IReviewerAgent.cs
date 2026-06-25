using AiEvalGate.Core.Models;

namespace AiEvalGate.Core.Reviewers;

/// <summary>
/// One automated AI reviewer agent in the AI-only evaluation pipeline: an LLM-as-judge that
/// inspects a single scenario run and returns a definitive pass/fail <see cref="AgentReview"/>.
/// </summary>
/// <remarks>
/// Implementations (for example <see cref="AiReviewerAgent"/>) wrap a chat client and a
/// focus-specific system prompt, then emit a strict JSON verdict that is deserialized into an
/// <see cref="AgentReview"/>. A team of reviewers (see <see cref="AiReviewerTeam"/>) typically
/// runs the default roster — <c>ArchitectureReviewer</c>, <c>GroundingReviewer</c>,
/// <c>RetrievalReviewer</c>, <c>ToolUseReviewer</c>, <c>SafetyReviewer</c>, <c>SecurityReviewer</c>,
/// <c>RedTeamReviewer</c>, and <c>RegressionReviewer</c> — concurrently, and the resulting reviews
/// are consumed by <see cref="AiEvalGate.Core.Evaluation.AiEvalGatekeeper"/> to decide the gate.
/// Consistent with the AI-only policy invariant, a reviewer is never allowed to defer to a human:
/// it must always commit to a pass/fail decision rather than abstain, and verdicts originate
/// entirely from the LLM judge with no human review or manual override.
/// </remarks>
public interface IReviewerAgent
{
    /// <summary>
    /// The reviewer's canonical, authoritative name (for example <c>SafetyReviewer</c> or
    /// <c>ArchitectureReviewer</c>).
    /// </summary>
    /// <remarks>
    /// This value is stamped onto <see cref="AgentReview.Reviewer"/> (overwriting whatever name the
    /// model returned) so the reported reviewer stays trustworthy, and the gatekeeper matches it
    /// case-insensitively against the policy's required reviewers.
    /// </remarks>
    string Name { get; }

    /// <summary>
    /// Reviews one scenario run from this agent's focus and produces a definitive pass/fail verdict.
    /// </summary>
    /// <remarks>
    /// The implementation serializes the scenario, run result, evaluator scores, and the AI-only
    /// policy flags into a single untrusted-data payload for the LLM judge, requests a strict JSON
    /// response (temperature 0), parses the returned object into an <see cref="AgentReview"/>, and
    /// rewrites its <see cref="AgentReview.Reviewer"/> to <see cref="Name"/>. The reviewer must
    /// commit to a pass/fail decision and may not defer to a human, and it treats scenario/run text
    /// (including any embedded instructions) as data to evaluate rather than commands to obey.
    /// </remarks>
    /// <param name="scenario">
    /// The evaluation case being judged: its user input, optional system prompt, grounding context,
    /// and the expected/forbidden claims, sources, and tools the run is graded against.
    /// </param>
    /// <param name="runResult">
    /// The captured output of the system under test for <paramref name="scenario"/> — the final
    /// answer, retrieved sources/context, tool calls, and service traces — that constitutes the
    /// evidence under review.
    /// </param>
    /// <param name="evaluatorScores">
    /// The deterministic LLM-as-judge metric scores (for example relevance, coherence, completeness,
    /// groundedness) already computed for the run, supplied to the reviewer as additional context.
    /// </param>
    /// <param name="cancellationToken">A token to observe while awaiting the underlying judge call.</param>
    /// <returns>
    /// A task whose result is the reviewer's <see cref="AgentReview"/> — its pass/fail verdict,
    /// confidence score, highest P0-P3 severity, findings, rationale, and self-reported sub-scores —
    /// with <see cref="AgentReview.Reviewer"/> set to <see cref="Name"/>.
    /// </returns>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when the judge output cannot be parsed into an <see cref="AgentReview"/> — when it
    /// contains no JSON object or deserializes to <see langword="null"/>.
    /// </exception>
    Task<AgentReview> ReviewAsync(
        AiScenario scenario,
        AiRunResult runResult,
        IReadOnlyList<MetricScore> evaluatorScores,
        CancellationToken cancellationToken = default);
}
