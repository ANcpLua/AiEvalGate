using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability.Evaluators;

/// <summary>
/// Deterministic, non-LLM <see cref="IEvaluator"/> that verifies an agent's observability evidence:
/// every required piece of telemetry was actually captured, every required evidence id is cited in the
/// agent's final response, and no forbidden claim was emitted. It reports a single boolean metric,
/// <c>observability.telemetry.evidence</c>.
/// </summary>
/// <remarks>
/// The verdict is computed purely from the constructor-injected <see cref="ObservabilityEvaluationRecord"/>;
/// the evaluator makes no model calls, keeping it on the deterministic side of the AI-only gate. The actual
/// rule logic lives in the static <see cref="Analyze"/> method so it can be unit-tested without any chat
/// plumbing, and <see cref="EvaluateAsync"/> is a thin adapter that maps the result onto a
/// <see cref="BooleanMetric"/> (a pass becomes <c>EvaluationRating.Good</c>, a failure becomes
/// <c>EvaluationRating.Unacceptable</c>). Citation and forbidden-claim checks are substring matches performed
/// with <see cref="StringComparison.OrdinalIgnoreCase"/>.
/// </remarks>
/// <param name="record">
/// The captured record under evaluation, supplying the telemetry evidence, the set of required evidence ids,
/// the forbidden-claim strings, and the agent's final response text that the analysis inspects.
/// </param>
public sealed class TelemetryEvidenceEvaluator(ObservabilityEvaluationRecord record) : IEvaluator
{
    /// <summary>
    /// The names of the metrics this evaluator produces, as required by <see cref="IEvaluator"/>.
    /// </summary>
    /// <value>
    /// A single-element collection containing the telemetry-evidence metric name
    /// (<c>observability.telemetry.evidence</c>).
    /// </value>
    public IReadOnlyCollection<string> EvaluationMetricNames => [ObservabilityMetricNames.TelemetryEvidence];

    /// <summary>
    /// Evaluates the telemetry-evidence rules against the injected record and returns the result as the
    /// single boolean metric <c>observability.telemetry.evidence</c>.
    /// </summary>
    /// <remarks>
    /// Delegates to the static <see cref="Analyze"/> method and wraps its outcome in a
    /// <see cref="BooleanMetric"/> via <see cref="EvaluationMetricFactory"/>. The work is fully synchronous
    /// and operates only on the constructor-supplied record, so the returned <see cref="ValueTask{TResult}"/>
    /// is already completed. The <see cref="IEvaluator"/>-contract parameters below are accepted to satisfy the
    /// interface but are not consulted by this implementation.
    /// </remarks>
    /// <param name="messages">The conversation history supplied by the <see cref="IEvaluator"/> contract; not used here.</param>
    /// <param name="modelResponse">The model response supplied by the <see cref="IEvaluator"/> contract; not used here.</param>
    /// <param name="chatConfiguration">Optional chat configuration supplied by the <see cref="IEvaluator"/> contract; not used here.</param>
    /// <param name="additionalContext">Optional additional evaluation context supplied by the <see cref="IEvaluator"/> contract; not used here.</param>
    /// <param name="cancellationToken">A token to observe cancellation requests; not observed here because the work is synchronous.</param>
    /// <returns>
    /// A completed <see cref="ValueTask{TResult}"/> whose <see cref="EvaluationResult"/> carries the
    /// telemetry-evidence <see cref="BooleanMetric"/>, with the pass/fail outcome and a human-readable reason.
    /// </returns>
    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        AnalysisResult analysis = Analyze(record);
        BooleanMetric metric = EvaluationMetricFactory.CreateBoolean(ObservabilityMetricNames.TelemetryEvidence, analysis.Passed, analysis.Reason);
        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }

    /// <summary>
    /// Applies the telemetry-evidence rules to a record and returns whether it passes along with a reason.
    /// </summary>
    /// <remarks>
    /// Runs three independent checks over the record:
    /// <list type="bullet">
    /// <item><description>
    /// <b>Missing telemetry</b> — any <see cref="ObservabilityEvaluationRecord.RequiredEvidenceIds"/> entry
    /// whose id is absent from the set of <see cref="TelemetryEvidenceRecord.Id"/> values actually captured in
    /// <see cref="ObservabilityEvaluationRecord.Telemetry"/>.
    /// </description></item>
    /// <item><description>
    /// <b>Missing citations</b> — any required evidence id not present as a substring of
    /// <see cref="ObservabilityEvaluationRecord.FinalResponse"/> (matched with
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>).
    /// </description></item>
    /// <item><description>
    /// <b>Forbidden claims</b> — any <see cref="ObservabilityEvaluationRecord.ForbiddenClaims"/> string found
    /// as a substring of the final response (same ordinal, case-insensitive match).
    /// </description></item>
    /// </list>
    /// The record passes only when all three sets are empty; otherwise the failing categories are joined into a
    /// single semicolon-separated reason string.
    /// </remarks>
    /// <param name="record">
    /// The record to analyze, supplying the captured telemetry, required evidence ids, forbidden claims, and the
    /// final response text.
    /// </param>
    /// <returns>
    /// A passing <see cref="AnalysisResult"/> when all required evidence exists and is cited and no forbidden
    /// claim was emitted; otherwise a failing result whose reason names the missing telemetry, missing citations,
    /// and forbidden claims that were found.
    /// </returns>
    public static AnalysisResult Analyze(ObservabilityEvaluationRecord record)
    {
        HashSet<string> availableEvidence = [.. record.Telemetry.Select(static telemetry => telemetry.Id)];
        List<string> missingTelemetry = [.. record.RequiredEvidenceIds.Where(required => !availableEvidence.Contains(required))];
        List<string> missingCitations = [.. record.RequiredEvidenceIds.Where(required => !ContainsOrdinalIgnoreCase(record.FinalResponse, required))];
        List<string> forbiddenClaims = [.. record.ForbiddenClaims.Where(claim => ContainsOrdinalIgnoreCase(record.FinalResponse, claim))];

        if (missingTelemetry.Count == 0 && missingCitations.Count == 0 && forbiddenClaims.Count == 0)
        {
            return AnalysisResult.Pass("All required telemetry evidence exists, is cited, and no forbidden claims were emitted.");
        }

        List<string> reasons = [];
        if (missingTelemetry.Count > 0)
        {
            reasons.Add($"missing telemetry: {string.Join(", ", missingTelemetry)}");
        }

        if (missingCitations.Count > 0)
        {
            reasons.Add($"missing citations: {string.Join(", ", missingCitations)}");
        }

        if (forbiddenClaims.Count > 0)
        {
            reasons.Add($"forbidden claims: {string.Join(", ", forbiddenClaims)}");
        }

        return AnalysisResult.Fail(string.Join("; ", reasons));
    }

    private static bool ContainsOrdinalIgnoreCase(string value, string expected)
        => value.Contains(expected, StringComparison.OrdinalIgnoreCase);
}
