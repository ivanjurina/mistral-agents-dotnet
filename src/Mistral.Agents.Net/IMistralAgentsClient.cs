using System.Text.Json;
using Mistral.Agents.Net.Models;

namespace Mistral.Agents.Net;

/// <summary>Abstraction over <see cref="MistralAgentsClient"/> for testing and DI.</summary>
public interface IMistralAgentsClient
{
    /// <summary>Creates a persistent agent.</summary>
    Task<Agent> CreateAgentAsync(CreateAgentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists agents in your workspace (raw JSON).</summary>
    Task<JsonDocument> ListAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one agent by id.</summary>
    Task<Agent> GetAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>Starts a new conversation with an agent or model.</summary>
    Task<ConversationResponse> StartConversationAsync(StartConversationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Appends a new user input to an existing conversation and returns the next turn.</summary>
    Task<ConversationResponse> AppendConversationAsync(string conversationId, AppendConversationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reports the result of a function tool call, continuing the conversation.</summary>
    Task<ConversationResponse> SubmitToolResultAsync(string conversationId, string toolCallId, string result, CancellationToken cancellationToken = default);

    /// <summary>Retrieves a conversation, including its history (raw JSON).</summary>
    Task<JsonDocument> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default);
}
