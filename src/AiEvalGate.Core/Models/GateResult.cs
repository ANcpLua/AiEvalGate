using System.Text;

namespace AiEvalGate.Core.Models;

public sealed record GateResult
{
    public required string ScenarioId { get; init; }
    public required bool Passed { get; init; }
    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public string ToFailureMessage()
    {
        if (Passed) return $"Scenario {ScenarioId} passed.";

        var sb = new StringBuilder();
        sb.AppendLine($"Scenario {ScenarioId} failed AI-only gates:");
        foreach (string blocker in Blockers)
        {
            sb.AppendLine($"- {blocker}");
        }

        return sb.ToString();
    }
}
