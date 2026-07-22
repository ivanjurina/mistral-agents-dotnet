using System.Text.Json.Serialization;

namespace Mistral.Agents.Net.Models;

/// <summary>Request body to create or update an agent.</summary>
public sealed class CreateAgentRequest
{
    /// <summary>Model powering the agent, e.g. "mistral-medium-latest".</summary>
    [JsonPropertyName("model")] public required string Model { get; set; }

    /// <summary>Human-readable agent name.</summary>
    [JsonPropertyName("name")] public required string Name { get; set; }

    /// <summary>System instructions that define the agent's behavior.</summary>
    [JsonPropertyName("instructions")] public string? Instructions { get; set; }

    /// <summary>Short description of the agent.</summary>
    [JsonPropertyName("description")] public string? Description { get; set; }

    /// <summary>Tools and connectors available to the agent.</summary>
    [JsonPropertyName("tools")] public IList<AgentTool> Tools { get; } = new List<AgentTool>();

    /// <summary>Ids of agents this agent may hand off to (multi-agent orchestration).</summary>
    [JsonPropertyName("handoffs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IList<string>? Handoffs { get; set; }

    /// <summary>Sampling temperature.</summary>
    [JsonPropertyName("completion_args")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompletionArgs? CompletionArgs { get; set; }
}

/// <summary>Optional generation parameters attached to an agent.</summary>
public sealed class CompletionArgs
{
    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopP { get; set; }

    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }
}

/// <summary>An agent as returned by the API.</summary>
public sealed class Agent
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("model")] public string? Model { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("instructions")] public string? Instructions { get; init; }
    [JsonPropertyName("version")] public int? Version { get; init; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; init; }
}
