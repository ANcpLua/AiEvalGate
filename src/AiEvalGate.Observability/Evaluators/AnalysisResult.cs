namespace AiEvalGate.Observability.Evaluators;

public sealed record AnalysisResult(bool Passed, string Reason)
{
    public static AnalysisResult Pass(string reason) => new(true, reason);

    public static AnalysisResult Fail(string reason) => new(false, reason);
}
