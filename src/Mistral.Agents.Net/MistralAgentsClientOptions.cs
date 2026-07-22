namespace Mistral.Agents.Net;

/// <summary>Configuration for <see cref="MistralAgentsClient"/>.</summary>
public sealed class MistralAgentsClientOptions
{
    /// <summary>Your API key from https://console.mistral.ai.</summary>
    public required string ApiKey { get; set; }

    /// <summary>Base URL of the API. Override for testing.</summary>
    public string BaseUrl { get; set; } = "https://api.mistral.ai";

    /// <summary>HTTP timeout used when the client owns its HttpClient. Default: 100s.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
}
