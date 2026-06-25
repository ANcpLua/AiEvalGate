using System.Text.Json;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability;

internal static class ScenarioLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Reads observability evaluation scenarios from a JSONL (JSON Lines) file, parsing one
    /// <see cref="ObservabilityEvaluationRecord"/> per non-blank line into the in-memory
    /// records consumed by the OpenTelemetry-focused evaluators (telemetry evidence, trace
    /// correlation, tool-call accuracy, and cardinality safety).
    /// </summary>
    /// <remarks>
    /// Each line is deserialized with case-insensitive property-name matching, so the
    /// camelCase JSON keys declared via <c>[JsonPropertyName]</c> on
    /// <see cref="ObservabilityEvaluationRecord"/> (such as <c>userInput</c>, <c>toolCalls</c>,
    /// <c>telemetry</c>, and <c>shouldPass</c>) bind regardless of casing. Blank or
    /// whitespace-only lines are skipped; every other line must be a complete JSON object.
    /// Loading is strict and fails fast on a line that cannot be deserialized rather than
    /// skipping it. Records are returned in file order, preserving the spans and trace
    /// fields each scenario captures for the AI-only observability gate.
    /// </remarks>
    /// <param name="path">
    /// Filesystem path to the JSONL scenario file. Each non-blank line must be a complete
    /// JSON object deserializable into an <see cref="ObservabilityEvaluationRecord"/>.
    /// </param>
    /// <returns>
    /// A read-only list of the parsed <see cref="ObservabilityEvaluationRecord"/> records,
    /// in the order they appear in the file.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when a non-blank line deserializes to <c>null</c>; the message includes the
    /// offending path and 1-based line number.
    /// </exception>
    /// <exception cref="System.Text.Json.JsonException">
    /// Thrown when a non-blank line is not valid JSON for an
    /// <see cref="ObservabilityEvaluationRecord"/>.
    /// </exception>
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
