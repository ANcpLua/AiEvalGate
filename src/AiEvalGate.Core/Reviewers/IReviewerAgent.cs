using AiEvalGate.Core.Models;

namespace AiEvalGate.Core.Reviewers;

public interface IReviewerAgent
{
    string Name { get; }

    Task<AgentReview> ReviewAsync(
        AiScenario scenario,
        AiRunResult runResult,
        IReadOnlyList<MetricScore> evaluatorScores,
        CancellationToken cancellationToken = default);
}
