using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Mistral.Agents.Net.Models;

namespace Mistral.Agents.Net;

/// <summary>
/// Async-first client for the Mistral Agents and Conversations API: persistent agents,
/// stateful conversations, built-in connectors, handoffs, and function tools.
/// Thread-safe; register a single instance and reuse it.
/// </summary>
public sealed class MistralAgentsClient : IMistralAgentsClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly MistralAgentsClientOptions _options;

    /// <summary>Creates a client with its own <see cref="HttpClient"/>.</summary>
    public MistralAgentsClient(string apiKey)
        : this(new MistralAgentsClientOptions { ApiKey = apiKey }) { }

    /// <summary>Creates a client with its own <see cref="HttpClient"/> and the given options.</summary>
    public MistralAgentsClient(MistralAgentsClientOptions options)
        : this(new HttpClient { Timeout = options.Timeout }, options)
    {
        _ownsHttpClient = true;
    }

    /// <summary>Creates a client over an externally managed <see cref="HttpClient"/> (e.g. IHttpClientFactory).</summary>
    public MistralAgentsClient(HttpClient httpClient, MistralAgentsClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("An API key is required. Get one at https://console.mistral.ai", nameof(options));

        _http = httpClient;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<Agent> CreateAgentAsync(CreateAgentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var json = await PostAsync("/v1/agents", JsonSerializer.Serialize(request, JsonOptions), cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<Agent>(json, JsonOptions)
               ?? throw new MistralAgentsException("Empty response creating agent.");
    }

    /// <inheritdoc />
    public Task<JsonDocument> ListAgentsAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync("/v1/agents", cancellationToken);

    /// <inheritdoc />
    public async Task<Agent> GetAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        using var doc = await GetJsonAsync($"/v1/agents/{Uri.EscapeDataString(agentId)}", cancellationToken).ConfigureAwait(false);
        return doc.RootElement.Deserialize<Agent>(JsonOptions)
               ?? throw new MistralAgentsException("Empty response getting agent.");
    }

    /// <inheritdoc />
    public async Task<ConversationResponse> StartConversationAsync(StartConversationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var json = await PostAsync("/v1/conversations", JsonSerializer.Serialize(request, JsonOptions), cancellationToken).ConfigureAwait(false);
        return ConversationResponse.Parse(json);
    }

    /// <inheritdoc />
    public async Task<ConversationResponse> AppendConversationAsync(string conversationId, AppendConversationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentNullException.ThrowIfNull(request);
        var json = await PostAsync($"/v1/conversations/{Uri.EscapeDataString(conversationId)}",
            JsonSerializer.Serialize(request, JsonOptions), cancellationToken).ConfigureAwait(false);
        return ConversationResponse.Parse(json);
    }

    /// <inheritdoc />
    public async Task<ConversationResponse> SubmitToolResultAsync(string conversationId, string toolCallId, string result, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);

        // A function result is submitted as an input entry of type function.result.
        using var stream = new MemoryStream();
        await using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteStartArray("inputs");
            w.WriteStartObject();
            w.WriteString("type", "function.result");
            w.WriteString("tool_call_id", toolCallId);
            w.WriteString("result", result);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        }
        var body = Encoding.UTF8.GetString(stream.ToArray());
        var json = await PostAsync($"/v1/conversations/{Uri.EscapeDataString(conversationId)}", body, cancellationToken).ConfigureAwait(false);
        return ConversationResponse.Parse(json);
    }

    /// <inheritdoc />
    public Task<JsonDocument> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        return GetJsonAsync($"/v1/conversations/{Uri.EscapeDataString(conversationId)}", cancellationToken);
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Get, path, null);
        return JsonDocument.Parse(await SendAsync(request, cancellationToken).ConfigureAwait(false));
    }

    private async Task<string> PostAsync(string path, string jsonBody, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Post, path, jsonBody);
        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, string? jsonBody)
    {
        var request = new HttpRequestMessage(method, $"{_options.BaseUrl.TrimEnd('/')}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (jsonBody is not null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            throw new MistralAgentsException($"HTTP request to Mistral failed: {ex.Message}", ex);
        }

        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new MistralAgentsException(TryExtractError(content) ?? $"Mistral returned {(int)response.StatusCode}.")
                {
                    StatusCode = response.StatusCode,
                };
            return content;
        }
    }

    private static string? TryExtractError(string content)
    {
        if (content.Length == 0 || content[0] != '{') return null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            // Mistral errors: { "message": "..."} or { "detail": ... } or { "error": {...} }
            if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) return m.GetString();
            if (root.TryGetProperty("detail", out var d))
                return d.ValueKind == JsonValueKind.String ? d.GetString() : d.GetRawText();
            if (root.TryGetProperty("error", out var e))
                return e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText();
            return null;
        }
        catch (JsonException) { return null; }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
