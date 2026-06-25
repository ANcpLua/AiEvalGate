using AiEvalGate.Core.Models;
using Microsoft.Extensions.AI;

namespace AiEvalGate.Core.Reviewers;

/// <summary>
/// Orchestrates a panel of AI reviewer agents, fanning a single evaluation case out to every
/// reviewer concurrently and collecting their individual <see cref="AgentReview"/> verdicts.
/// </summary>
/// <remarks>
/// Each member of the team is an <see cref="IReviewerAgent"/> with its own review focus (for
/// example architecture, grounding, retrieval, tool use, safety, security, red-teaming, or
/// regression). Consistent with the pipeline's AI-only policy invariant, every reviewer is an
/// automated LLM judge that must return a definitive pass/fail decision and is never allowed to
/// defer to a human or trigger a manual override. The collected reviews are consumed downstream
/// by the gatekeeper, which combines them with the deterministic evaluator scores to decide
/// whether the gate passes.
/// </remarks>
public sealed class AiReviewerTeam
{
    private readonly IReadOnlyList<IReviewerAgent> _reviewers;

    /// <summary>
    /// Initializes a new team from an explicit, ordered collection of reviewer agents.
    /// </summary>
    /// <param name="reviewers">
    /// The reviewer agents that make up the panel. The collection's order is preserved: the
    /// reviews returned by <see cref="ReviewAsync"/> are produced in this same order, one per
    /// reviewer.
    /// </param>
    public AiReviewerTeam(IReadOnlyList<IReviewerAgent> reviewers)
    {
        _reviewers = reviewers;
    }

    /// <summary>
    /// Builds the default reviewer panel, creating one <see cref="AiReviewerAgent"/> for every
    /// entry in <see cref="ReviewerPromptLibrary.DefaultReviewerFocus"/> (the standard roster of
    /// architecture, grounding, retrieval, tool-use, safety, security, red-team, and regression
    /// reviewers), each named and focused from that library.
    /// </summary>
    /// <param name="chatClient">
    /// The chat client every default reviewer agent shares to obtain its LLM judgment.
    /// </param>
    /// <returns>
    /// A team containing one reviewer agent per default focus, in the library's enumeration order.
    /// </returns>
    public static AiReviewerTeam CreateDefault(IChatClient chatClient)
    {
        IReviewerAgent[] reviewers =
        [
            .. ReviewerPromptLibrary.DefaultReviewerFocus
                .Select(kvp => new AiReviewerAgent(kvp.Key, kvp.Value, chatClient))
        ];

        return new AiReviewerTeam(reviewers);
    }

    /// <summary>
    /// Runs every reviewer in the team against the same evaluation case concurrently and gathers
    /// their verdicts.
    /// </summary>
    /// <param name="scenario">
    /// The scenario specification under evaluation (user input, optional system prompt, grounding
    /// context, and the expected/forbidden behavior the run is graded against).
    /// </param>
    /// <param name="runResult">
    /// The captured output of the system under test for this scenario (its final answer, retrieved
    /// sources and context, tool calls, and service traces) that each reviewer inspects.
    /// </param>
    /// <param name="evaluatorScores">
    /// The deterministic LLM-as-judge metric scores already computed for the run, supplied to each
    /// reviewer as additional context for its decision.
    /// </param>
    /// <param name="cancellationToken">
    /// A token forwarded to every reviewer that cancels the in-flight LLM review calls.
    /// </param>
    /// <returns>
    /// A task that completes with one <see cref="AgentReview"/> per reviewer, in the same order as
    /// the team's reviewers; each review carries a definitive pass/fail verdict, score, severity,
    /// findings, rationale, and per-dimension metrics.
    /// </returns>
    /// <exception cref="System.InvalidOperationException">
    /// Propagated when an individual reviewer agent cannot parse a usable review from its model's
    /// output (for example, missing or invalid review JSON).
    /// </exception>
    public async Task<IReadOnlyList<AgentReview>> ReviewAsync(
        AiScenario scenario,
        AiRunResult runResult,
        IReadOnlyList<MetricScore> evaluatorScores,
        CancellationToken cancellationToken = default)
    {
        var tasks = _reviewers.Select(r => r.ReviewAsync(scenario, runResult, evaluatorScores, cancellationToken));
        return await Task.WhenAll(tasks);
    }
}
