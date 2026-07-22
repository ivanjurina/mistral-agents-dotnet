using System.Net;

namespace Mistral.Agents.Net;

/// <summary>Thrown when the Mistral API returns an error or a request fails.</summary>
public sealed class MistralAgentsException : Exception
{
    /// <summary>Creates the exception with an error message.</summary>
    public MistralAgentsException(string message) : base(message) { }

    /// <summary>Creates the exception with an error message and the underlying cause.</summary>
    public MistralAgentsException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>HTTP status code returned by Mistral, when available.</summary>
    public HttpStatusCode? StatusCode { get; init; }
}
