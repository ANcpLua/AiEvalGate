using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using AiEvalGate.Observability.Evaluators;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability;

internal sealed class EvaluationRunner
{
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
                new CardinalitySafetyEvaluator(record)
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
