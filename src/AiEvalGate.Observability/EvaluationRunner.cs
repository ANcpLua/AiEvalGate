using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using AiEvalGate.Observability.Evaluators;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability;

/// <summary>
/// Executes the deterministic observability evaluation suite over a batch of
/// <see cref="ObservabilityEvaluationRecord"/> instances and produces one
/// <see cref="ScenarioRunResult"/> per record.
/// </summary>
/// <remarks>
/// For each record the runner constructs the four built-in evaluators
/// (<see cref="Evaluators.TelemetryEvidenceEvaluator"/>,
/// <see cref="Evaluators.ToolCallAccuracyEvaluator"/>,
/// <see cref="Evaluators.TraceCorrelationEvaluator"/>, and
/// <see cref="Evaluators.CardinalitySafetyEvaluator"/>), evaluates each one, and
/// aggregates their emitted <see cref="EvaluationMetric"/> values. Every metric is a
/// boolean rated <see cref="EvaluationRating.Good"/> on pass and
/// <see cref="EvaluationRating.Unacceptable"/> (with <c>failed</c> set) on failure.
/// </remarks>
internal sealed class EvaluationRunner
{
    /// <summary>
    /// Runs all four observability evaluators against each supplied record and returns
    /// the aggregated pass/fail outcome for every record.
    /// </summary>
    /// <param name="records">
    /// The observability evaluation records to assess. Each record's
    /// <see cref="ObservabilityEvaluationRecord.FinalResponse"/> is wrapped in an
    /// assistant <see cref="ChatResponse"/> and passed to every evaluator.
    /// </param>
    /// <param name="cancellationToken">
    /// A token forwarded to each evaluator's <c>EvaluateAsync</c> call to observe
    /// cancellation requests.
    /// </param>
    /// <returns>
    /// A read-only list containing one <see cref="ScenarioRunResult"/> per input record,
    /// in the same order as <paramref name="records"/>, where each result aggregates the
    /// metrics produced by all four evaluators for that record.
    /// </returns>
    public static async Task<IReadOnlyList<ScenarioRunResult>> RunAsync(
        IReadOnlyList<ObservabilityEvaluationRecord> records,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ScenarioRunResult>(records.Count);

        foreach (ObservabilityEvaluationRecord record in records)
        {
            List<IEvaluator> evaluators =
            [
                new TelemetryEvidenceEvaluator(record),
                new ToolCallAccuracyEvaluator(record),
                new TraceCorrelationEvaluator(record),
                new CardinalitySafetyEvaluator(record),
                new StructuredOutputConformanceEvaluator(record)
            ];

            ChatResponse response = new(new ChatMessage(ChatRole.Assistant, record.FinalResponse));
            var metrics = new List<EvaluationMetric>();

            foreach (IEvaluator evaluator in evaluators)
            {
                EvaluationResult evaluation = await evaluator.EvaluateAsync(
                    messages: [],
                    modelResponse: response,
                    chatConfiguration: null,
                    additionalContext: null,
                    cancellationToken: cancellationToken);

                metrics.AddRange(evaluation.Metrics.Values);
            }

            results.Add(ScenarioRunResult.Create(record, metrics));
        }

        return results;
    }
}
