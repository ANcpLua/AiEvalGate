using Microsoft.Extensions.AI.Evaluation;

namespace AiEvalGate.Observability.Evaluators;

internal static class EvaluationMetricFactory
{
    /// <summary>
    /// Builds a pass/fail <see cref="BooleanMetric"/> for an observability evaluator and attaches an
    /// <see cref="EvaluationMetricInterpretation"/> so the metric carries a normalized rating, not just a raw boolean.
    /// </summary>
    /// <param name="name">
    /// The metric name, one of the <c>observability.*</c> identifiers defined in <c>ObservabilityMetricNames</c>
    /// (telemetry evidence, tool-call accuracy, trace correlation, or cardinality safety).
    /// </param>
    /// <param name="passed">
    /// Whether the underlying check passed. When <see langword="true"/> the interpretation is rated
    /// <see cref="EvaluationRating.Good"/>; when <see langword="false"/> it is rated
    /// <see cref="EvaluationRating.Unacceptable"/> and flagged as failed, which is what marks the metric as a
    /// gate-blocking failure (consumers treat <c>Interpretation.Failed == true</c> as a failed metric).
    /// </param>
    /// <param name="reason">
    /// The human-readable explanation for the verdict, recorded on both the <see cref="BooleanMetric"/> and its
    /// <see cref="EvaluationMetricInterpretation"/>. For passing checks this describes the satisfied condition; for
    /// failing checks it typically lists the specific gaps (for example missing telemetry, missing citations, or forbidden claims).
    /// </param>
    /// <returns>
    /// A <see cref="BooleanMetric"/> named <paramref name="name"/> with value <paramref name="passed"/> and an
    /// <see cref="EvaluationMetricInterpretation"/> rated <see cref="EvaluationRating.Good"/> (passing) or
    /// <see cref="EvaluationRating.Unacceptable"/> with the failed flag set (failing).
    /// </returns>
    /// <remarks>
    /// Centralizes the mapping from a boolean evaluator outcome to the
    /// <c>Microsoft.Extensions.AI.Evaluation</c> rating scale so every observability evaluator emits metrics with
    /// consistent interpretations. The factory only translates an already-computed outcome; it performs no analysis itself.
    /// </remarks>
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
