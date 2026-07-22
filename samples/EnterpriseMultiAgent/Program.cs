// An enterprise multi-agent workflow in .NET on the Mistral Agents API.
//
// A "financial analyst" agent answers questions, using the built-in web search and code
// interpreter connectors, and can hand off to a dedicated research agent. Your own C#
// function (get_internal_metric) is exposed as a tool the agent can call for private data.
//
// Requires: MISTRAL_API_KEY (from https://console.mistral.ai)
using System.Text.Json;
using Mistral.Agents.Net;
using Mistral.Agents.Net.Models;

var apiKey = Environment.GetEnvironmentVariable("MISTRAL_API_KEY")
    ?? throw new InvalidOperationException("Set the MISTRAL_API_KEY environment variable.");

using var client = new MistralAgentsClient(apiKey);

// A research agent the analyst can delegate to.
var researcher = await client.CreateAgentAsync(new CreateAgentRequest
{
    Model = "mistral-medium-latest",
    Name = "Research Agent",
    Instructions = "You find current facts on the web and summarize them concisely.",
    Tools = { AgentTool.WebSearch() },
});

// The analyst: computes with the code interpreter, delegates research, and can call our
// private metric function for internal data the model can't know.
var analystRequest = new CreateAgentRequest
{
    Model = "mistral-medium-latest",
    Name = "Financial Analyst",
    Instructions = "You are a financial analyst. Use the code interpreter for calculations, " +
                   "hand off to the research agent for current market facts, and call " +
                   "get_internal_metric for the company's private numbers.",
    Handoffs = new List<string> { researcher.Id! },
};
analystRequest.Tools.Add(AgentTool.CodeInterpreter());
analystRequest.Tools.Add(AgentTool.FunctionTool(FunctionDefinition.FromJsonSchema(
    "get_internal_metric",
    "Returns an internal company metric by name (e.g. 'q3_revenue').",
    """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""")));
var analyst = await client.CreateAgentAsync(analystRequest);

Console.WriteLine($"Analyst agent: {analyst.Id}. Ask a question (empty line to quit).\n");

string? conversationId = null;
while (Console.ReadLine() is { Length: > 0 } question)
{
    using var turn = conversationId is null
        ? await client.StartConversationAsync(new StartConversationRequest { AgentId = analyst.Id, Inputs = question })
        : await client.AppendConversationAsync(conversationId, new AppendConversationRequest { Inputs = question });
    conversationId = turn.ConversationId;

    var result = turn;
    // Resolve any client-side function calls (our private data), looping until the agent is done.
    while (result.FunctionCalls.Count > 0)
    {
        var call = result.FunctionCalls[0];
        var answer = HandleFunction(call.FunctionName!, call.Arguments);
        Console.WriteLine($"[agent called {call.FunctionName} -> {answer}]");
        var next = await client.SubmitToolResultAsync(conversationId!, call.ToolCallId!, answer);
        result.Dispose();
        result = next;
    }

    Console.WriteLine($"analyst: {result.OutputText}\n");
    if (!ReferenceEquals(result, turn)) result.Dispose();
}

static string HandleFunction(string name, JsonElement args) => name switch
{
    "get_internal_metric" => args.TryGetProperty("name", out var n) && n.GetString() == "q3_revenue"
        ? "Q3 revenue was 12.4M EUR, up 18% YoY."
        : "Metric not found.",
    _ => "Unknown function.",
};
