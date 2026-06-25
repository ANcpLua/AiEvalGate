using System.Text.Json;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability;

internal static class ScenarioLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<ObservabilityEvaluationRecord> LoadJsonl(string path)
    {
        var records = new List<ObservabilityEvaluationRecord>();
        int lineNumber = 0;

        foreach (string line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ObservabilityEvaluationRecord record = JsonSerializer.Deserialize<ObservabilityEvaluationRecord>(line, JsonOptions)
                ?? throw new InvalidDataException($"Could not deserialize {path} line {lineNumber}.");

            records.Add(record);
        }

        return records;
    }
}
