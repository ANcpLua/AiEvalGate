using System.Text.Json;
using AiEvalGate.Core;

namespace AiEvalGate.Core.Models;

public sealed record GatePolicy
{
    public required IReadOnlyList<string> RequiredReviewers { get; init; }
    public required Dictionary<string, double> MinMetrics { get; init; }
    public required double MinReviewerScore { get; init; }
    public required IReadOnlyList<string> BlockSeverities { get; init; }
    public required bool RequireAllReviewerPasses { get; init; }
    public required bool ServiceBoundaryStrict { get; init; }
    public required AiOnlyPolicy AiOnlyPolicy { get; init; }

    public static GatePolicy Load(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GatePolicy>(json, JsonOptions.Default)
               ?? throw new InvalidOperationException($"Unable to load gate policy: {path}");
    }
}

public sealed record AiOnlyPolicy
{
    public required bool HumanReviewRequired { get; init; }
    public required bool ManualOverrideAllowed { get; init; }
    public required int ManualApprovalSteps { get; init; }
}
