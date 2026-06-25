using Microsoft.Extensions.AI.Evaluation;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability;

internal sealed record ScenarioRunResult(
    string Id,
    bool Passed,
    IReadOnlyList<string> FailedMetrics,
    IReadOnlyList<string> ExpectedFailedMetrics,
    IReadOnlyList<string> Mismatches)
{
    public static ScenarioRunResult Create(ObservabilityEvaluationRecord record, IReadOnlyList<EvaluationMetric> metrics)
    {
        string[] failedMetrics = [.. metrics
            .Where(static metric => metric.Interpretation?.Failed == true)
            .Select(static metric => metric.Name)
            .Order(StringComparer.Ordinal)];

        string[] expectedFailedMetrics = [.. record.ExpectedFailedMetrics.Order(StringComparer.Ordinal)];
        var mismatches = new List<string>();

        if (record.ShouldPass && expectedFailedMetrics.Length > 0)
        {
            mismatches.Add("pass record declares expected failed metrics");
        }

        if (!record.ShouldPass && expectedFailedMetrics.Length == 0)
        {
            mismatches.Add("fail record declares no expected failed metrics");
        }

        if (!failedMetrics.SequenceEqual(expectedFailedMetrics, StringComparer.Ordinal))
        {
            mismatches.Add($"failed metrics [{string.Join(", ", failedMetrics)}] != expected [{string.Join(", ", expectedFailedMetrics)}]");
        }

        return new ScenarioRunResult(
            record.Id,
            mismatches.Count == 0,
            failedMetrics,
            expectedFailedMetrics,
            mismatches);
    }
}
