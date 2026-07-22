# building enterprise multi-agent workflows in .net with mistral

*draft for ivanjurina.com, ivan jurina, july 2026*

most people know [Mistral](https://mistral.ai) for its chat models. the part i find more interesting for enterprise work is the [Agents API](https://docs.mistral.ai/studio-api/agents/introduction): persistent agents with instructions and tools, stateful conversations you can resume, built-in connectors (web search, code interpreter, document library), and handoffs so one agent can delegate to another.

the .net story stops short of this. the community sdks (tghamm's is genuinely good) cover chat completions, embeddings and function calling. they don't cover the agentic layer. so if you're a .net shop that wants to build a multi-agent workflow on mistral, you're writing raw http. i didn't want to, so i built [Mistral.Agents.Net](https://github.com/ivanjurina/mistral-agents-dotnet). here's the design and the one wire-format detail that cost me a debugging session.

## agents, not just completions

a chat completion is stateless: you send messages, you get a reply, you manage all the history yourself. an agent is a stored object with instructions and tools, and a conversation is a stateful thread you can continue by id. that difference matters for enterprise workflows, where a "session" spans many turns and you want the platform to hold the state.

```csharp
var agent = await client.CreateAgentAsync(new CreateAgentRequest
{
    Model = "mistral-medium-latest",
    Name = "Financial Analyst",
    Instructions = "Use the code interpreter for math and web search for current facts.",
    Tools = { AgentTool.CodeInterpreter(), AgentTool.WebSearch() },
});

using var turn = await client.StartConversationAsync(new StartConversationRequest
{
    AgentId = agent.Id,
    Inputs = "what was 15% of last quarter's revenue if it was 12.4M?",
});
Console.WriteLine(turn.OutputText);
```

the response isn't a single message. it's a list of outputs: tool executions, message chunks, function calls, handoffs. the library gives you `OutputText` for the common case and `Outputs` for the raw stream, plus a `Root` JsonElement escape hatch for anything the typed model doesn't cover yet. same philosophy i used for the serpapi and elevenlabs clients: typed where it helps, raw where you need it.

## handoffs: one agent delegating to another

the enterprise-interesting feature is handoffs. you give an agent the ids of other agents it may delegate to, and the platform routes work between them.

```csharp
var researcher = await client.CreateAgentAsync(new CreateAgentRequest
{
    Model = "mistral-medium-latest",
    Name = "Research Agent",
    Tools = { AgentTool.WebSearch() },
});

var analyst = await client.CreateAgentAsync(new CreateAgentRequest
{
    Model = "mistral-medium-latest",
    Name = "Financial Analyst",
    Handoffs = new List<string> { researcher.Id! }, // delegate research
    Tools = { AgentTool.CodeInterpreter() },
});
```

ask the analyst a question that needs current market data and it hands off to the researcher, which uses web search, and the answer comes back through the analyst. you orchestrate multiple specialized agents without writing the routing yourself.

## your own code as a tool

built-in connectors are great, but enterprise value is in your private data. a function tool exposes your c# to the agent: it decides when to call, you run it, you return the result.

```csharp
request.Tools.Add(AgentTool.FunctionTool(FunctionDefinition.FromJsonSchema(
    "get_internal_metric", "Returns a private company metric.",
    """{"type":"object","properties":{"name":{"type":"string"}}}""")));
```

then, when the agent calls it:

```csharp
foreach (var call in turn.FunctionCalls)
{
    using var args = call.ParseArguments();
    var result = LookUp(call.FunctionName!, args.RootElement);
    using var next = await client.SubmitToolResultAsync(turn.ConversationId!, call.ToolCallId!, result);
}
```

i tested this end to end: asked "what is 15% of our q3 revenue?", watched the agent call `get_internal_metric`, feed the private number back, and compute 15% with the code interpreter. the whole loop, from .net.

## the detail the docs don't tell you

here's the debugging session. the docs show function-call arguments as a json object. the live api returns them as a json **string** — a stringified object you have to parse. my first live run threw `element has type 'String'` the instant the agent called a function, because i was calling `GetProperty` on what i thought was an object.

this is a common llm-api quirk (openai does the same), but it's exactly the kind of thing you only learn by running against the real service, not by reading the reference. so the library handles it for you:

```csharp
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
```

`ParseArguments()` normalizes both forms, so your code never sees the difference. every wrinkle like this that the library absorbs is a wrinkle your users don't hit.

## what's real

agents, conversations, connectors, handoffs and function tools are all live-verified against the real api — the revenue demo above actually runs. the library is net8.0, zero dependencies, async-first with cancellation, typed requests with a raw escape hatch, and an offline test suite that replays captured api frames (including the string-arguments case) so ci needs no key or credits.

it's on nuget as `Mistral.Agents.Net` and the source is on my github. .net is a large enterprise audience for agentic ai and right now it has no official path to mistral's agents platform. if you're at mistral and reading this: happy to help close that gap properly.
