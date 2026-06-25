using System.Text.Json;

namespace AiEvalGate.Core;

/// <summary>
/// Provides the single, shared <see cref="JsonSerializerOptions"/> used for all JSON
/// serialization and deserialization across AiEvalGate.Core.
/// </summary>
/// <remarks>
/// Centralizing the options here keeps every artifact on disk and every payload exchanged
/// with reviewers on one consistent JSON shape. The same instance is reused when writing
/// evaluation reports and summaries, when reading gate policies, scenarios, and service
/// boundary contracts, and when round-tripping AI reviewer payloads, so reader and writer
/// always agree on naming and parsing rules.
/// </remarks>
public static class JsonOptions
{
    /// <summary>
    /// The canonical, read-only <see cref="JsonSerializerOptions"/> instance shared by every
    /// JSON read and write in AiEvalGate.Core.
    /// </summary>
    /// <remarks>
    /// Configured to:
    /// <list type="bullet">
    /// <item><description>Emit camelCase property names (<see cref="JsonNamingPolicy.CamelCase"/>).</description></item>
    /// <item><description>Match property names case-insensitively when reading.</description></item>
    /// <item><description>Write indented (human-readable) output for diff-friendly artifacts.</description></item>
    /// <item><description>Skip comments while parsing (<see cref="JsonCommentHandling.Skip"/>), so annotated
    /// policy and scenario files load without error.</description></item>
    /// <item><description>Allow trailing commas when reading.</description></item>
    /// </list>
    /// The instance is immutable in practice and intended to be reused rather than copied or mutated.
    /// </remarks>
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
