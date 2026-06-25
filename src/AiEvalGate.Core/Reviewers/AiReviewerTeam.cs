using AiEvalGate.Core.Models;
using Microsoft.Extensions.AI;

namespace AiEvalGate.Core.Reviewers;

public sealed class AiReviewerTeam
{
    private readonly IReadOnlyList<IReviewerAgent> _reviewers;

    public AiReviewerTeam(IReadOnlyList<IReviewerAgent> reviewers)
    {
        _reviewers = reviewers;
    }

    public static AiReviewerTeam CreateDefault(IChatClient chatClient)
    {
        var reviewers = ReviewerPromptLibrary.DefaultReviewerFocus
            .Select(kvp => new AiReviewerAgent(kvp.Key, kvp.Value, chatClient))
            .Cast<IReviewerAgent>()
            .ToArray();

        return new AiReviewerTeam(reviewers);
    }

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
