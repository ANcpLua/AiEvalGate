using AiEvalGate.Core.Evaluation;
using AiEvalGate.Core.Models;
using AiEvalGate.Core.Reviewers;

namespace AiEvalGate.EvaluationTests;

/// <summary>
/// Deterministic <see cref="IQualityEvaluator"/> that returns canned passing scores for the four
/// quality metrics the gate policy requires, so the gate pipeline runs end-to-end with no model
/// judge and no Anthropic API call.
/// </summary>
internal sealed class StubQualityEvaluator : IQualityEvaluator
{
    public Task<IReadOnlyList<MetricScore>> EvaluateAsync(
        AiScenario scenario,
        AiRunResult runResult,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MetricScore> scores =
        [
            new() { Name = "Relevance", Value = 5.0, Reason = "stub judge", Interpretation = "Good" },
            new() { Name = "Coherence", Value = 5.0, Reason = "stub judge", Interpretation = "Good" },
            new() { Name = "Completeness", Value = 5.0, Reason = "stub judge", Interpretation = "Good" },
            new() { Name = "Groundedness", Value = 5.0, Reason = "stub judge", Interpretation = "Good" },
        ];

        return Task.FromResult(scores);
    }
}

/// <summary>
/// Deterministic <see cref="IReviewerAgent"/> that always returns a passing, non-blocking review for
/// its name. <see cref="DefaultRoster"/> assembles a stub team covering the default reviewer roster,
/// which satisfies the gate policy's required-reviewer and minimum-score rules without any model call.
/// </summary>
internal sealed class StubReviewerAgent(string name) : IReviewerAgent
{
    public string Name => name;

    public Task<AgentReview> ReviewAsync(
        AiScenario scenario,
        AiRunResult runResult,
        IReadOnlyList<MetricScore> evaluatorScores,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AgentReview
        {
            Reviewer = name,
            Passed = true,
            Score = 1.0,
            Severity = "P3",
            Findings = [],
            Rationale = "stub reviewer: deterministic pass (no model judge)",
        });

    public static AiReviewerTeam DefaultRoster() =>
        new([.. ReviewerPromptLibrary.DefaultReviewerFocus.Keys.Select(reviewerName => new StubReviewerAgent(reviewerName))]);
}
