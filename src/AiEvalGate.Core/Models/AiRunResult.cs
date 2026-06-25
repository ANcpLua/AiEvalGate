namespace AiEvalGate.Core.Models;

public sealed record AiRunResult
{
    public required string ScenarioId { get; init; }
    public required string FinalAnswer { get; init; }
    public IReadOnlyList<string> RetrievedSources { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RetrievedContext { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ToolCallTrace> ToolCalls { get; init; } = Array.Empty<ToolCallTrace>();
    public IReadOnlyList<ServiceTrace> ServiceTraces { get; init; } = Array.Empty<ServiceTrace>();
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record ToolCallTrace
{
    public required string Name { get; init; }
    public Dictionary<string, string> Arguments { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? ResultSummary { get; init; }
}

public sealed record ServiceTrace
{
    public required string ServiceName { get; init; }
    public required string Operation { get; init; }
    public string? InputSummary { get; init; }
    public string? OutputSummary { get; init; }
    public IReadOnlyList<string> SourceIds { get; init; } = Array.Empty<string>();
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
