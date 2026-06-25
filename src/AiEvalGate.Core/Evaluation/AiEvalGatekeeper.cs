using AiEvalGate.Core.Models;

namespace AiEvalGate.Core.Evaluation;

/// <summary>
/// Stateless evaluator that enforces the AI-only release gate for a single scenario, combining
/// metric scores, agent reviewer verdicts, and service-boundary checks into a pass/fail decision.
/// </summary>
/// <remarks>
/// The gate is intentionally AI-only: any human-in-the-loop control (human review required, manual
/// override allowed, or a non-zero manual-approval step count) is treated as a broken policy and
/// produces a blocker. A scenario passes only when no blockers are collected; severities follow the
/// P0&#8211;P3 scale (P0 being a critical blocker) emitted by reviewers, and the configured
/// <see cref="AiEvalGate.Core.Models.GatePolicy.BlockSeverities"/> set decides which of those
/// severities are gate-blocking.
/// </remarks>
public static class AiEvalGatekeeper
{
    /// <summary>
    /// Evaluates a single scenario against the gate policy and returns the aggregated gate decision.
    /// </summary>
    /// <remarks>
    /// Blockers are accumulated from five independent checks: the AI-only policy invariant (any human
    /// review, manual override, or non-zero manual-approval step count), missing required reviewers,
    /// per-reviewer verdicts (blocking severity, an optional all-must-pass requirement, and a minimum
    /// reviewer score), per-metric minimums (where a scenario-specific threshold in
    /// <see cref="AiEvalGate.Core.Models.AiScenario.Thresholds"/> overrides the policy minimum and a
    /// higher score is better, so a value below the minimum blocks), and service-boundary failures.
    /// Service-boundary failures block when <see cref="AiEvalGate.Core.Models.GatePolicy.ServiceBoundaryStrict"/>
    /// is set; otherwise they are downgraded to warnings. All string comparisons for reviewer names,
    /// metric names, and severities are case-insensitive.
    /// </remarks>
    /// <param name="scenario">
    /// The scenario under evaluation; supplies the result's <see cref="AiEvalGate.Core.Models.GateResult.ScenarioId"/>
    /// and any per-metric threshold overrides via its thresholds map.
    /// </param>
    /// <param name="runResult">
    /// The captured AI run output for the scenario. It is accepted for context and traceability and is
    /// not itself inspected here; the gate decision is derived from the scores, reviews, and boundary checks.
    /// </param>
    /// <param name="scores">The evaluator metric scores keyed by name, compared against the policy and scenario minimums.</param>
    /// <param name="reviews">The agent reviewer verdicts, checked for required reviewers, blocking severities, pass flags, and minimum scores.</param>
    /// <param name="serviceBoundaryFailures">
    /// Detected service-boundary violations, surfaced as blockers under strict mode and as warnings otherwise.
    /// </param>
    /// <param name="policy">The gate policy defining required reviewers, metric and reviewer thresholds, blocking severities, strictness, and the AI-only invariant.</param>
    /// <returns>
    /// A <see cref="AiEvalGate.Core.Models.GateResult"/> whose <c>Passed</c> flag is <see langword="true"/>
    /// only when no blockers were collected, carrying the accumulated blocker and warning messages.
    /// </returns>
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
