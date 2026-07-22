using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mistral.Agents.Net.Models;

/// <summary>
/// A tool an agent can use. Built-in connectors (web search, code interpreter, image
/// generation, document library) plus custom function tools. Construct with the static
/// factory methods.
/// </summary>
public sealed class AgentTool
{
    [JsonPropertyName("type")] public required string Type { get; init; }

    // function tool
    [JsonPropertyName("function")] public FunctionDefinition? Function { get; init; }

    // document library connector
    [JsonPropertyName("library_ids")] public IReadOnlyList<string>? LibraryIds { get; init; }

    // custom connector / MCP
    [JsonPropertyName("connector_id")] public string? ConnectorId { get; init; }

    /// <summary>Built-in web search connector.</summary>
    public static AgentTool WebSearch() => new() { Type = "web_search" };

    /// <summary>Premium web search connector (news/agency sources).</summary>
    public static AgentTool WebSearchPremium() => new() { Type = "web_search_premium" };

    /// <summary>Sandboxed Python execution.</summary>
    public static AgentTool CodeInterpreter() => new() { Type = "code_interpreter" };

    /// <summary>Image generation connector.</summary>
    public static AgentTool ImageGeneration() => new() { Type = "image_generation" };

    /// <summary>Document library (RAG) over the given Mistral Cloud libraries.</summary>
    public static AgentTool DocumentLibrary(params string[] libraryIds) =>
        new() { Type = "document_library", LibraryIds = libraryIds };

    /// <summary>A custom connector / MCP server, referenced by id.</summary>
    public static AgentTool Connector(string connectorId) =>
        new() { Type = "connector", ConnectorId = connectorId };

    /// <summary>A custom function tool the client executes and reports results for.</summary>
    public static AgentTool FunctionTool(FunctionDefinition function) =>
        new() { Type = "function", Function = function };
}

/// <summary>Schema of a custom function tool.</summary>
public sealed class FunctionDefinition
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }

    /// <summary>JSON Schema object describing the parameters.</summary>
    [JsonPropertyName("parameters")] public JsonElement Parameters { get; init; }

    /// <summary>Builds a function definition from a raw JSON Schema string.</summary>
    public static FunctionDefinition FromJsonSchema(string name, string? description, string jsonSchema)
    {
        using var doc = JsonDocument.Parse(jsonSchema);
        return new FunctionDefinition { Name = name, Description = description, Parameters = doc.RootElement.Clone() };
    }
}
