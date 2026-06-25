using System.Text.Json;
using AiEvalGate.Core.Models;
using AiEvalGate.Core;

namespace AiEvalGate.Core.Scenarios;

public static class ScenarioLoader
{
    public static IReadOnlyList<AiScenario> LoadJsonl(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Scenario file not found.", path);
        }

        var scenarios = new List<AiScenario>();
        int lineNo = 0;

        foreach (string rawLine in File.ReadLines(path))
        {
            lineNo++;
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

            AiScenario scenario = JsonSerializer.Deserialize<AiScenario>(line, JsonOptions.Default)
                ?? throw new InvalidOperationException($"Invalid scenario at {path}:{lineNo}");

            if (string.IsNullOrWhiteSpace(scenario.Id))
            {
                throw new InvalidOperationException($"Scenario at {path}:{lineNo} has no id.");
            }

            scenarios.Add(scenario);
        }

        return scenarios;
    }
}
