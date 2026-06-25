using Microsoft.Extensions.AI.Evaluation;

namespace AiEvalGate.Observability.Evaluators;

internal static class EvaluationMetricFactory
{
    public static BooleanMetric CreateBoolean(string name, bool passed, string reason)
    {
        return new BooleanMetric(name, passed, reason)
        {
            Interpretation = passed
                ? new EvaluationMetricInterpretation(EvaluationRating.Good, reason: reason)
                : new EvaluationMetricInterpretation(EvaluationRating.Unacceptable, failed: true, reason: reason)
        };
    }
}
