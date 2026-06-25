using System.Text.Json.Serialization;

namespace AiEvalGate.Core.Models;

public sealed record AiScenario
{
    public required string Id { get; init; }
    public required string Area { get; init; }
    public required string Architecture { get; init; }
    public required string UserInput { get; init; }
    public string? SystemPrompt { get; init; }
    public IReadOnlyList<string> Context { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RequiredSources { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RequiredClaims { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ForbiddenClaims { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExpectedTools { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ForbiddenTools { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public Dictionary<string, double> Thresholds { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string ContextBlock => string.Join("\n", Context);
}
