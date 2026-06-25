using System.Text.Json.Serialization;

namespace AiEvalGate.Observability.Models;

/// <summary>
/// Describes the AI agent under evaluation — the system under test — captured as part of an
/// <see cref="ObservabilityEvaluationRecord"/> in the observability evaluation pipeline.
/// </summary>
/// <remarks>
/// Consistent with the AI-only policy invariant, this record always identifies an AI agent
/// (its model provider, model, and instructions) rather than a human actor. The record is an
/// immutable, init-only data carrier serialized to and from JSON using the camelCase property
/// names declared by the <see cref="JsonPropertyNameAttribute"/> on each member.
/// </remarks>
public sealed record AgentInfo
{
    /// <summary>
    /// The agent's display name (serialized as <c>"name"</c>). Required.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The provider hosting the agent's underlying language model (serialized as
    /// <c>"modelProvider"</c>), for example the chat-client provider the agent runs on. Required.
    /// </summary>
    [JsonPropertyName("modelProvider")]
    public required string ModelProvider { get; init; }

    /// <summary>
    /// The identifier of the specific model the agent uses (serialized as <c>"modelName"</c>),
    /// for example the model deployment or model name within <see cref="ModelProvider"/>. Required.
    /// </summary>
    [JsonPropertyName("modelName")]
    public required string ModelName { get; init; }

    /// <summary>
    /// The system instructions (system prompt) that configure the agent's behavior, serialized as
    /// <c>"instructions"</c>. Required.
    /// </summary>
    [JsonPropertyName("instructions")]
    public required string Instructions { get; init; }

    /// <summary>
    /// The names of the tools available to the agent (serialized as <c>"tools"</c>). Defaults to an
    /// empty list when the agent exposes no tools.
    /// </summary>
    [JsonPropertyName("tools")]
    public IReadOnlyList<string> Tools { get; init; } = [];
}
