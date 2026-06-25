using AiEvalGate.Core.Models;

namespace AiEvalGate.Core.Evaluation;

/// <summary>
/// Computes quality metric scores for a single scenario run.
/// </summary>
/// <remarks>
/// The production implementation, <see cref="MicrosoftQualityEvaluator"/>, calls a model judge. This
/// seam lets tests and CI substitute a deterministic implementation so the gate pipeline can be
/// exercised end-to-end without any API call — paying for the real judge only when the model
/// behavior itself is being verified.
/// </remarks>
public interface IQualityEvaluator
{
    /// <summary>
    /// Scores the run across this evaluator's quality dimensions (for example relevance, coherence,
    /// completeness, and groundedness).
    /// </summary>
    /// <param name="scenario">The evaluation case being scored.</param>
    /// <param name="runResult">The captured system-under-test output for <paramref name="scenario"/>.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>One <see cref="MetricScore"/> per quality metric produced.</returns>
    Task<IReadOnlyList<MetricScore>> EvaluateAsync(
        AiScenario scenario,
        AiRunResult runResult,
        CancellationToken cancellationToken = default);
}
