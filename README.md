# Mistral.Agents.Net: the Mistral Agents API from .NET

[![NuGet](https://img.shields.io/nuget/v/Mistral.Agents.Net.svg)](https://www.nuget.org/packages/Mistral.Agents.Net/) [![NuGet downloads](https://img.shields.io/nuget/dt/Mistral.Agents.Net.svg)](https://www.nuget.org/packages/Mistral.Agents.Net/)

📦 **NuGet:** [nuget.org/packages/Mistral.Agents.Net](https://www.nuget.org/packages/Mistral.Agents.Net)

unofficial .net client for the [Mistral Agents & Conversations API](https://docs.mistral.ai/studio-api/agents/introduction). the community .net sdks cover chat completions, embeddings and function calling well; this covers the agentic layer they don't: persistent agents, stateful conversations, built-in connectors, and multi-agent handoffs.

not affiliated with Mistral AI. complements [tghamm/Mistral.SDK](https://github.com/tghamm/Mistral.SDK) rather than competing with it.

## what you get

- **agents**: create, list and fetch persistent agents (model, instructions, tools, handoffs)
- **conversations**: stateful, resumable conversations, not just stateless chat completions
- **built-in connectors** as tools: web search, code interpreter, image generation, document library (RAG)
- **handoffs**: multi-agent orchestration, one agent delegating to others
- **function tools**: expose your own c# code, execute it client-side, return the result
- net8.0, zero dependencies, async-first with `CancellationToken`, typed requests with a raw `JsonElement` escape hatch, testable via an injectable `HttpClient`

## quickstart

```
dotnet add package Mistral.Agents.Net
```

```csharp
using Mistral.Agents.Net;
using Mistral.Agents.Net.Models;

using var client = new MistralAgentsClient(apiKey);

var agent = await client.CreateAgentAsync(new CreateAgentRequest
{
    Model = "mistral-medium-latest",
    Name = "Analyst",
    Instructions = "Use the code interpreter for math and web search for current facts.",
    Tools = { AgentTool.CodeInterpreter(), AgentTool.WebSearch() },
});

using var turn = await client.StartConversationAsync(new StartConversationRequest
{
    AgentId = agent.Id,
    Inputs = "What is 15% of last quarter's revenue if it was 12.4M?",
});

Console.WriteLine(turn.OutputText);
```

### your own code as a tool

```csharp
agentRequest.Tools.Add(AgentTool.FunctionTool(FunctionDefinition.FromJsonSchema(
    "get_internal_metric", "Returns a private company metric.",
    """{"type":"object","properties":{"name":{"type":"string"}}}""")));

// when the agent calls it:
foreach (var call in turn.FunctionCalls)
{
    using var args = call.ParseArguments();               // see note below
    var result = LookUp(call.FunctionName!, args.RootElement);
    using var next = await client.SubmitToolResultAsync(turn.ConversationId!, call.ToolCallId!, result);
}
```

> **Wire-format note:** the Agents API returns function-call `arguments` as a JSON *string*,
> not a nested object (a common LLM-API quirk the docs don't call out). `ParseArguments()`
> normalizes both forms so you always get a usable document. This one cost a live debugging
> session, so it's handled in the library.

### multi-agent handoffs

```csharp
var analyst = await client.CreateAgentAsync(new CreateAgentRequest
{
    Model = "mistral-medium-latest",
    Name = "Analyst",
    Handoffs = new List<string> { researchAgent.Id! }, // delegate research to another agent
    Tools = { AgentTool.CodeInterpreter() },
});
```

## project layout

| path | what |
|---|---|
| `src/Mistral.Agents.Net` | the client |
| `tests/Mistral.Agents.Net.SmokeTests` | offline API tests against a fake HTTP handler, no network/credits |
| `samples/EnterpriseMultiAgent` | a financial-analyst agent that computes, researches via handoff, and calls your private data |

## run the tests

```
dotnet run --project tests/Mistral.Agents.Net.SmokeTests
```

## roadmap

- streaming conversations (SSE)
- Connectors / MCP server registration
- Microsoft.Extensions.AI bridge so it drops into the .NET AI ecosystem

## license

MIT
