namespace AiEvalGate.Core.Models;

public sealed record AgentReview
{
    public required string Reviewer { get; init; }
    public required bool Passed { get; init; }
    public required double Score { get; init; }
    public required string Severity { get; init; }
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();
    public string Rationale { get; init; } = string.Empty;
    public Dictionary<string, double> Metrics { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
