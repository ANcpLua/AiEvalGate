namespace AiEvalGate.Core.Models;

/// <summary>
/// A single numeric quality metric produced by an LLM-as-judge evaluator for one scenario run.
/// </summary>
/// <remarks>
/// Instances are emitted by <see cref="AiEvalGate.Core.Evaluation.MicrosoftQualityEvaluator"/>
/// (one per numeric metric the judges return, reported as the median of repeated judge samples)
/// and consumed by <see cref="AiEvalGate.Core.Evaluation.AiEvalGatekeeper"/>, which matches a score
/// by <see cref="Name"/> (case-insensitively) against the policy and scenario metric minimums.
/// Consistent with the AI-only policy invariant, the value originates entirely from an LLM judge
/// with no human review or manual override.
/// </remarks>
public sealed record MetricScore
{
    /// <summary>
    /// The metric's name (for example, relevance, coherence, completeness, or groundedness), used to
    /// match this score against the gate's configured per-metric minimums and any scenario threshold
    /// override. Matching is case-insensitive.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// The metric's numeric score, taken as the median across the judge samples (defaulting to
    /// <c>0.0</c> when the judge returns no value). A higher score is better: the gate treats the
    /// metric as failing when this value falls below the required minimum.
    /// </summary>
    public required double Value { get; init; }
    /// <summary>
    /// The judge's natural-language justification for <see cref="Value"/>, carried through from the
    /// selected median sample. It is surfaced in the gate blocker message when the metric falls below
    /// its minimum and in the generated evaluation report. May be <see langword="null"/> when the
    /// judge supplies no reason.
    /// </summary>
    public string? Reason { get; init; }
    /// <summary>
    /// A human-readable interpretation of <see cref="Value"/> from the selected median sample (the
    /// judge's interpretation rendered as text), shown alongside the score in the evaluation report.
    /// May be <see langword="null"/> when the judge supplies no interpretation.
    /// </summary>
    public string? Interpretation { get; init; }
}
