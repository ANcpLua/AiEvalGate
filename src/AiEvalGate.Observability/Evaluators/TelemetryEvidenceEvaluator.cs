using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability.Evaluators;

public sealed class TelemetryEvidenceEvaluator(ObservabilityEvaluationRecord record) : IEvaluator
{
    public IReadOnlyCollection<string> EvaluationMetricNames => [ObservabilityMetricNames.TelemetryEvidence];

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
