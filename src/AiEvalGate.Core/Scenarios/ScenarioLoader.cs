using System.Text.Json;
using AiEvalGate.Core.Models;
using AiEvalGate.Core;

namespace AiEvalGate.Core.Scenarios;

/// <summary>
/// Reads AI evaluation scenarios from a JSONL (JSON Lines) file, parsing one
/// <see cref="AiScenario"/> per non-blank, non-comment line into the in-memory test
/// definitions consumed by the quality evaluators, the AI reviewer team, the
/// service-boundary validator, and the AI-only gatekeeper.
/// </summary>
/// <remarks>
/// Deserialization uses the shared <see cref="JsonOptions.Default"/> options, so property
/// names are matched camelCase and case-insensitively, comments are skipped, and trailing
/// commas are tolerated. Blank lines and lines whose first non-whitespace character is
/// <c>#</c> are treated as comments and ignored, allowing scenario files to be annotated.
/// Every scenario is validated to have a non-blank <see cref="AiScenario.Id"/>; loading is
/// strict and fails fast rather than skipping malformed entries.
/// </remarks>
public static class ScenarioLoader
{
    /// <summary>
    /// Reads the JSONL file at <paramref name="path"/> and returns its scenarios in file
    /// order, ignoring blank lines and <c>#</c>-prefixed comment lines.
    /// </summary>
    /// <param name="path">
    /// Filesystem path to the JSONL scenario file. Each significant line must be a complete
    /// JSON object deserializable into an <see cref="AiScenario"/>.
    /// </param>
    /// <returns>
    /// A read-only list of the parsed <see cref="AiScenario"/> records, in the order they
    /// appear in the file.
    /// </returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when no file exists at <paramref name="path"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a line deserializes to <c>null</c> (an invalid scenario) or when a parsed
    /// scenario has a blank <see cref="AiScenario.Id"/>; the message includes the offending
    /// path and 1-based line number.
    /// </exception>
    /// <exception cref="System.Text.Json.JsonException">
    /// Thrown when a significant line is not valid JSON for an <see cref="AiScenario"/>.
    /// </exception>
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
