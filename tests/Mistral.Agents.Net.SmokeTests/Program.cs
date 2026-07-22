// Framework-free smoke tests for Mistral.Agents.Net.
// Runs fully offline: a fake HTTP handler serves canned Agents/Conversations API responses
// and records every request the client sends. No network, no credits.
using System.Net;
using System.Text;
using System.Text.Json;
using Mistral.Agents.Net;
using Mistral.Agents.Net.Models;

var failures = 0;
var passed = 0;
void Ok(string name) { passed++; Console.WriteLine($"  ok  {name}"); }
void Fail(string name, string detail) { failures++; Console.WriteLine($"FAIL  {name}: {detail}"); }
void Assert(bool cond, string name, string detail = "") { if (cond) Ok(name); else Fail(name, detail); }

static MistralAgentsClient ClientFor(RecordingHandler handler) =>
    new(new HttpClient(handler), new MistralAgentsClientOptions { ApiKey = "test-key", BaseUrl = "https://api.mistral.ai" });

// ---------- create agent: request shape + auth header ----------
{
    var handler = new RecordingHandler(_ => Json("""{"id":"ag_1","name":"Analyst","model":"mistral-medium-latest","version":1}"""));
    using var client = ClientFor(handler);

    var req = new CreateAgentRequest { Model = "mistral-medium-latest", Name = "Analyst", Instructions = "be precise" };
    req.Tools.Add(AgentTool.WebSearch());
    req.Tools.Add(AgentTool.CodeInterpreter());
    req.Handoffs = new List<string> { "ag_web" };
    var agent = await client.CreateAgentAsync(req);

    Assert(agent.Id == "ag_1", "agent parsed from response");
    Assert(handler.LastRequestPath == "/v1/agents", "posts to /v1/agents");
    Assert(handler.LastAuthHeader == "Bearer test-key", "bearer auth header sent");
    using var body = JsonDocument.Parse(handler.LastRequestBody!);
    var root = body.RootElement;
    Assert(root.GetProperty("model").GetString() == "mistral-medium-latest", "model in body");
    Assert(root.GetProperty("tools").GetArrayLength() == 2, "tools serialized");
    Assert(root.GetProperty("tools")[0].GetProperty("type").GetString() == "web_search", "web_search tool type");
    Assert(root.GetProperty("tools")[1].GetProperty("type").GetString() == "code_interpreter", "code_interpreter tool type");
    Assert(root.GetProperty("handoffs")[0].GetString() == "ag_web", "handoffs serialized");
}

// ---------- document library + function tools serialize ----------
{
    var handler = new RecordingHandler(_ => Json("""{"id":"ag_2"}"""));
    using var client = ClientFor(handler);
    var req = new CreateAgentRequest { Model = "mistral-small-latest", Name = "Docs" };
    req.Tools.Add(AgentTool.DocumentLibrary("lib_1", "lib_2"));
    req.Tools.Add(AgentTool.FunctionTool(FunctionDefinition.FromJsonSchema(
        "get_order", "gets an order", """{"type":"object","properties":{"id":{"type":"string"}}}""")));
    await client.CreateAgentAsync(req);

    using var body = JsonDocument.Parse(handler.LastRequestBody!);
    var tools = body.RootElement.GetProperty("tools");
    Assert(tools[0].GetProperty("library_ids").GetArrayLength() == 2, "document library ids serialized");
    Assert(tools[1].GetProperty("type").GetString() == "function", "function tool type");
    Assert(tools[1].GetProperty("function").GetProperty("name").GetString() == "get_order", "function name serialized");
    Assert(tools[1].GetProperty("function").GetProperty("parameters").GetProperty("type").GetString() == "object", "function json schema preserved");
}

// ---------- start conversation: outputs + text extraction ----------
{
    var response = """
    {"conversation_id":"conv_1","outputs":[
      {"type":"tool.execution","name":"web_search"},
      {"type":"message.output","content":"The answer is 42."}
    ]}
    """;
    var handler = new RecordingHandler(_ => Json(response));
    using var client = ClientFor(handler);

    using var res = await client.StartConversationAsync(new StartConversationRequest { AgentId = "ag_1", Inputs = "hi" });
    Assert(handler.LastRequestPath == "/v1/conversations", "posts to /v1/conversations");
    Assert(res.ConversationId == "conv_1", "conversation id parsed");
    Assert(res.Outputs.Count == 2, "all outputs surfaced");
    Assert(res.OutputText == "The answer is 42.", "assistant text extracted from message output");
    using var body = JsonDocument.Parse(handler.LastRequestBody!);
    Assert(body.RootElement.GetProperty("agent_id").GetString() == "ag_1", "agent_id in start body");
    Assert(body.RootElement.GetProperty("inputs").GetString() == "hi", "inputs in start body");
}

// ---------- content as array of chunks ----------
{
    var response = """
    {"conversation_id":"c","outputs":[{"type":"message.output","content":[{"type":"text","text":"foo"},{"type":"text","text":"bar"}]}]}
    """;
    using var client = ClientFor(new RecordingHandler(_ => Json(response)));
    using var res = await client.StartConversationAsync(new StartConversationRequest { Model = "mistral-small-latest", Inputs = "x" });
    Assert(res.OutputText == "foobar", "chunked content concatenated");
}

// ---------- function call surfaced + tool result submitted ----------
{
    var withCall = """
    {"conversation_id":"conv_9","outputs":[
      {"type":"function.call","name":"get_order","tool_call_id":"call_7","arguments":{"id":"1234"}}
    ]}
    """;
    var afterResult = """{"conversation_id":"conv_9","outputs":[{"type":"message.output","content":"Order 1234 shipped."}]}""";
    var step = 0;
    var handler = new RecordingHandler(_ => Json(step++ == 0 ? withCall : afterResult));
    using var client = ClientFor(handler);

    using var res = await client.StartConversationAsync(new StartConversationRequest { AgentId = "ag_1", Inputs = "status of 1234?" });
    var calls = res.FunctionCalls;
    Assert(calls.Count == 1, "function call surfaced");
    Assert(calls[0].FunctionName == "get_order", "function name read");
    Assert(calls[0].ToolCallId == "call_7", "tool_call_id read");
    Assert(calls[0].Arguments.GetProperty("id").GetString() == "1234", "arguments read");

    using var res2 = await client.SubmitToolResultAsync("conv_9", "call_7", "Order 1234 shipped.");
    Assert(res2.OutputText == "Order 1234 shipped.", "conversation continues after tool result");
    Assert(handler.LastRequestPath == "/v1/conversations/conv_9", "tool result posts to conversation");
    using var body = JsonDocument.Parse(handler.LastRequestBody!);
    var input0 = body.RootElement.GetProperty("inputs")[0];
    Assert(input0.GetProperty("type").GetString() == "function.result", "function.result input type");
    Assert(input0.GetProperty("tool_call_id").GetString() == "call_7", "tool result carries call id");
    Assert(input0.GetProperty("result").GetString() == "Order 1234 shipped.", "tool result forwarded");
}

// ---------- append conversation ----------
{
    var handler = new RecordingHandler(_ => Json("""{"conversation_id":"conv_1","outputs":[{"type":"message.output","content":"ok"}]}"""));
    using var client = ClientFor(handler);
    using var res = await client.AppendConversationAsync("conv_1", new AppendConversationRequest { Inputs = "more" });
    Assert(handler.LastRequestPath == "/v1/conversations/conv_1", "append posts to conversation id");
    Assert(res.OutputText == "ok", "append returns next turn");
}

// ---------- error surfacing ----------
{
    var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
    {
        Content = new StringContent("""{"message":"Unauthorized: invalid api key"}""", Encoding.UTF8, "application/json"),
    });
    using var client = ClientFor(handler);
    try
    {
        using var _ = await client.StartConversationAsync(new StartConversationRequest { AgentId = "a", Inputs = "x" });
        Fail("error surfaced", "no exception thrown");
    }
    catch (MistralAgentsException ex)
    {
        Assert(ex.Message.Contains("invalid api key"), "error message surfaced");
        Assert(ex.StatusCode == HttpStatusCode.Unauthorized, "status code preserved");
    }
}

// ---------- cancellation ----------
{
    var handler = new RecordingHandler(_ => Json("{}"));
    using var client = ClientFor(handler);
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    try
    {
        using var _ = await client.StartConversationAsync(new StartConversationRequest { AgentId = "a", Inputs = "x" }, cts.Token);
        Fail("cancellation throws", "no exception");
    }
    catch (OperationCanceledException) { Ok("cancellation throws OperationCanceledException"); }
}

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static HttpResponseMessage Json(string body) =>
    new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public string? LastRequestPath { get; private set; }
    public string? LastRequestBody { get; private set; }
    public string? LastAuthHeader { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRequestPath = request.RequestUri!.AbsolutePath;
        LastAuthHeader = request.Headers.Authorization?.ToString();
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return responder(request);
    }
}
