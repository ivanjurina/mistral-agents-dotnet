using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mistral.Agents.Net.Models;

/// <summary>Request to start a new conversation with an agent (or a bare model).</summary>
public sealed class StartConversationRequest
{
    /// <summary>Id of the agent to converse with. Mutually exclusive with <see cref="Model"/>.</summary>
    [JsonPropertyName("agent_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentId { get; set; }

    /// <summary>Model to use when not targeting a stored agent.</summary>
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    /// <summary>The user input that starts the conversation.</summary>
    [JsonPropertyName("inputs")] public required string Inputs { get; set; }

    /// <summary>Persist the conversation so it can be retrieved and continued. Default true server-side.</summary>
    [JsonPropertyName("store")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Store { get; set; }

    /// <summary>Tools to enable for this conversation when not using a stored agent.</summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<AgentTool>? Tools { get; set; }
}

/// <summary>Request to append a new user input to an existing conversation.</summary>
public sealed class AppendConversationRequest
{
    [JsonPropertyName("inputs")] public required string Inputs { get; set; }

    [JsonPropertyName("store")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Store { get; set; }
}

/// <summary>
/// A conversation turn's result. <see cref="OutputText"/> is the concatenated assistant text;
/// <see cref="Outputs"/> exposes every raw output entry (messages, tool executions, function
/// calls, handoffs) for full control.
/// </summary>
public sealed class ConversationResponse : IDisposable
{
    private readonly JsonDocument _document;

    private ConversationResponse(JsonDocument document) => _document = document;

    internal static ConversationResponse Parse(string json) => new(JsonDocument.Parse(json));

    /// <summary>The full JSON payload. Valid until this instance is disposed.</summary>
    public JsonElement Root => _document.RootElement;

    /// <summary>The conversation id, used to continue the conversation later.</summary>
    public string? ConversationId =>
        Root.TryGetProperty("conversation_id", out var v) ? v.GetString() : null;

    /// <summary>All output entries returned for this turn.</summary>
    public IReadOnlyList<ConversationOutput> Outputs
    {
        get
        {
            if (!Root.TryGetProperty("outputs", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];
            var list = new List<ConversationOutput>();
            foreach (var el in arr.EnumerateArray())
                list.Add(new ConversationOutput(el));
            return list;
        }
    }

    /// <summary>The assistant's text for this turn, concatenated across message outputs.</summary>
    public string OutputText
    {
        get
        {
            var parts = new List<string>();
            foreach (var o in Outputs)
                if (o.Type is "message.output" or "message" && o.Text is { } t)
                    parts.Add(t);
            return string.Join("", parts);
        }
    }

    /// <summary>Any function calls the model is requesting the client to execute this turn.</summary>
    public IReadOnlyList<ConversationOutput> FunctionCalls =>
        Outputs.Where(o => o.Type == "function.call").ToList();

    /// <inheritdoc />
    public void Dispose() => _document.Dispose();
}

/// <summary>One output entry within a conversation turn.</summary>
public readonly struct ConversationOutput(JsonElement element)
{
    /// <summary>The entry's type, e.g. message.output, tool.execution, function.call, handoff.</summary>
    public string? Type => element.TryGetProperty("type", out var v) ? v.GetString() : null;

    /// <summary>Text content, when this is a message output.</summary>
    public string? Text
    {
        get
        {
            if (!element.TryGetProperty("content", out var content)) return null;
            if (content.ValueKind == JsonValueKind.String) return content.GetString();
            // content can be an array of chunks with a "text" field
            if (content.ValueKind == JsonValueKind.Array)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var chunk in content.EnumerateArray())
                    if (chunk.TryGetProperty("text", out var t)) sb.Append(t.GetString());
                return sb.ToString();
            }
            return null;
        }
    }

    /// <summary>For function.call entries: the tool name.</summary>
    public string? FunctionName => element.TryGetProperty("name", out var v) ? v.GetString() : null;

    /// <summary>For function.call entries: the call id to reference in the result.</summary>
    public string? ToolCallId => element.TryGetProperty("tool_call_id", out var v) ? v.GetString() : null;

    /// <summary>
    /// For function.call entries: the raw arguments element as sent. Note that Mistral
    /// returns arguments as a JSON <em>string</em>, not an object; prefer
    /// <see cref="ParseArguments"/>, which normalizes both forms.
    /// </summary>
    public JsonElement Arguments => element.TryGetProperty("arguments", out var v) ? v : default;

    /// <summary>
    /// Parses function-call arguments into a document, handling Mistral's string-encoded
    /// form (the API sends arguments as a JSON string) as well as a plain object. The
    /// caller owns the returned document and should dispose it.
    /// </summary>
    public JsonDocument ParseArguments()
    {
        var v = Arguments;
        return v.ValueKind switch
        {
            JsonValueKind.String => JsonDocument.Parse(string.IsNullOrEmpty(v.GetString()) ? "{}" : v.GetString()!),
            JsonValueKind.Object or JsonValueKind.Array => JsonDocument.Parse(v.GetRawText()),
            _ => JsonDocument.Parse("{}"),
        };
    }

    /// <summary>The raw JSON of this output entry.</summary>
    public JsonElement Raw => element;
}
