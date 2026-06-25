using AiEvalGate.Core.Models;

namespace AiEvalGate.Core.Evaluation;

public static class AiEvalGatekeeper
{
    public static GateResult Evaluate(
        AiScenario scenario,
        AiRunResult runResult,
        IReadOnlyList<MetricScore> scores,
        IReadOnlyList<AgentReview> reviews,
        IReadOnlyList<string> serviceBoundaryFailures,
        GatePolicy policy)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();

        if (policy.AiOnlyPolicy.HumanReviewRequired || policy.AiOnlyPolicy.ManualOverrideAllowed || policy.AiOnlyPolicy.ManualApprovalSteps != 0)
        {
            blockers.Add("AI-only policy is broken: human review or manual override is enabled.");
        }

        foreach (string requiredReviewer in policy.RequiredReviewers)
        {
            if (!reviews.Any(r => string.Equals(r.Reviewer, requiredReviewer, StringComparison.OrdinalIgnoreCase)))
            {
                blockers.Add($"Missing required reviewer: {requiredReviewer}");
            }
        }

        foreach (AgentReview review in reviews)
        {
            if (policy.BlockSeverities.Contains(review.Severity, StringComparer.OrdinalIgnoreCase))
            {
                blockers.Add($"{review.Reviewer} emitted blocking severity {review.Severity}: {string.Join(" | ", review.Findings)}");
            }

            if (policy.RequireAllReviewerPasses && !review.Passed)
            {
                blockers.Add($"{review.Reviewer} returned passed=false: {string.Join(" | ", review.Findings)}");
            }

            if (review.Score < policy.MinReviewerScore)
            {
                blockers.Add($"{review.Reviewer} score {review.Score:0.###} < required {policy.MinReviewerScore:0.###}");
            }
        }

        foreach ((string metricName, double min) in policy.MinMetrics)
        {
            double scenarioMin = scenario.Thresholds.TryGetValue(metricName, out double overrideMin) ? overrideMin : min;
            MetricScore? score = scores.FirstOrDefault(s => string.Equals(s.Name, metricName, StringComparison.OrdinalIgnoreCase));
            if (score is null)
            {
                blockers.Add($"Missing evaluator metric: {metricName}");
                continue;
            }

            if (score.Value < scenarioMin)
            {
                blockers.Add($"Metric {metricName} score {score.Value:0.###} < required {scenarioMin:0.###}. Reason: {score.Reason}");
            }
        }

        if (policy.ServiceBoundaryStrict)
        {
            blockers.AddRange(serviceBoundaryFailures.Select(f => $"Service boundary failure: {f}"));
        }
        else
        {
            warnings.AddRange(serviceBoundaryFailures.Select(f => $"Service boundary warning: {f}"));
        }

        return new GateResult
        {
            ScenarioId = scenario.Id,
            Passed = blockers.Count == 0,
            Blockers = blockers,
            Warnings = warnings
        };
    }
}
