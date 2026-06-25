namespace AiEvalGate.Core.Models;

public sealed record MetricScore
{
    public required string Name { get; init; }
    public required double Value { get; init; }
    public string? Reason { get; init; }
    public string? Interpretation { get; init; }
}
