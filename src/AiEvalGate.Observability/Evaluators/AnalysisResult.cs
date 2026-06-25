namespace AiEvalGate.Observability.Evaluators;

/// <summary>
/// Immutable verdict returned by each observability evaluator's <c>Analyze</c> method, pairing a
/// boolean outcome with the human-readable explanation behind it.
/// </summary>
/// <remarks>
/// Consumers (for example <c>TraceCorrelationEvaluator</c>, <c>CardinalitySafetyEvaluator</c> and
/// <c>TelemetryEvidenceEvaluator</c>) hand this result to <c>EvaluationMetricFactory.CreateBoolean</c>,
/// which maps <see cref="Passed"/> onto the metric interpretation: a passing result is rated
/// <c>Good</c>, while a failing result is rated <c>Unacceptable</c> and flagged as failed. The
/// <see cref="Reason"/> text is surfaced verbatim as that interpretation's reason.
/// </remarks>
/// <param name="Passed">
/// <see langword="true"/> when the evaluator's invariant held; otherwise <see langword="false"/>.
/// </param>
/// <param name="Reason">
/// Human-readable explanation for the verdict: a single pass message when <paramref name="Passed"/>
/// is <see langword="true"/>, or a semicolon-joined summary of the detected violations when it is
/// <see langword="false"/>.
/// </param>
public sealed record AnalysisResult(bool Passed, string Reason)
{
    /// <summary>
    /// Creates a passing <see cref="AnalysisResult"/> whose <see cref="Passed"/> is
    /// <see langword="true"/>.
    /// </summary>
    /// <param name="reason">The explanation describing why the evaluator's invariant held.</param>
    /// <returns>An <see cref="AnalysisResult"/> with <see cref="Passed"/> set to <see langword="true"/> and the supplied <paramref name="reason"/>.</returns>
    public static AnalysisResult Pass(string reason) => new(true, reason);

    /// <summary>
    /// Creates a failing <see cref="AnalysisResult"/> whose <see cref="Passed"/> is
    /// <see langword="false"/>.
    /// </summary>
    /// <param name="reason">The explanation describing the violations that caused the evaluator's invariant to fail.</param>
    /// <returns>An <see cref="AnalysisResult"/> with <see cref="Passed"/> set to <see langword="false"/> and the supplied <paramref name="reason"/>.</returns>
    public static AnalysisResult Fail(string reason) => new(false, reason);
}
