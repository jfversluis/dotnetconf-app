using System.Runtime.CompilerServices;
using System.Text.Json;
using DotNetConf.App.Services;
using DotNetConf.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

Require(!RuntimeFeature.IsDynamicCodeSupported, "Run the published NativeAOT executable, not dotnet run.");
Require(JsonSerializer.IsReflectionEnabledByDefault, "AI Attributes preview 14 requires JSON reflection explicitly enabled.");

var modelSelection = new PlannerModelSelection();
Require(!modelSelection.IsSupported &&
    modelSelection.SelectedModelId == PlannerModelSelection.ToolCallingModelId,
    "The Android planner did not keep its fixed function-calling model.");

await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
var conference = await ConferenceJson.ReadAsync(stream);
const string request = "AI sessions before 11pm Etc/GMT+2.";
var daily = PlannerJson.DeserializeResponse(
    "```json\n{\"dailyStart\":\"none\",\"dailyEnd\":\"23:00\",\"zone\":\"local\"}\n```",
    PlannerJsonContext.Default.PlannerDailyAvailability)
    ?? throw new InvalidDataException("Missing daily availability.");
var zone = PlannerInterpretation.RequestedZone(request);
var limits = (daily.Ground(request) with { Zone = zone }).Complete(PlannerWatchingHours.Default);
var windows = limits.ToWindows(TimeZoneInfo.Utc, conference);
Require(limits.Assumption == "Assumed daily start: 07:00 (Etc/GMT+2).", "Requested zone or assumed hours were lost.");
Require(windows.Length > 0 && windows.All(window => window.Zone == zone
    && window.Start.EndsWith("T07:00", StringComparison.Ordinal)
    && window.End.EndsWith("T23:00", StringComparison.Ordinal)), "NativeAOT changed the requested availability.");
Require(PlannerInterpretation.RequestedZone("HTTP/2 before 11pm UTC.") == "UTC", "A protocol version became a time zone.");
foreach (var range in new[] { "9am-5pm", "9am–5pm", "09:00—17:00" })
{
    var compact = new PlannerDailyAvailability("none", "none", "UTC").Ground($"AI from {range} UTC");
    Require(compact.DailyStart == "09:00" && compact.DailyEnd == "17:00", "A compact range lost a clock boundary.");
}
Require(!new PlannerInterpretation("none", "evening", "none", "some")
    .RequiresTimeExtraction("AI any time, including the evening"), "An inclusive daypart restricted unrestricted hours.");
using var services = new ServiceCollection().AddScoped<PlannerTools>().AddScoped<AskTools>().BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true });
using var scope = services.CreateScope();
var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
tools.Initialize(conference, TimeZoneInfo.Utc, []);
tools.CapturePreferences(["Alex"], [], topicFilter: new(["Mobile"], false, false, false));
var functions = tools.CreateFunctions(scope.ServiceProvider).Cast<AIFunction>().ToDictionary(f => f.Name);
Require(functions.Count == 5, "Only the five curated planner functions should be exposed.");
foreach (var function in functions.Values)
{
    Require(function.JsonSchema.GetProperty("type").GetString() == "object", $"Missing schema: {function.Name}.");
    Require(function.ReturnJsonSchema?.GetProperty("type").GetString() == "string", $"Missing return schema: {function.Name}.");
    Require(!string.IsNullOrWhiteSpace(function.Description), $"Missing description: {function.Name}.");
}

var searchArguments = Arguments("""{"query":"Mobile","skip":0}""");
searchArguments.Services = scope.ServiceProvider;
var rawSearch = PlannerToolContext.Default.Tools.Cast<AIFunction>().Single(f => f.Name == "search_sessions");
Require(rawSearch.JsonSchema.GetProperty("properties").GetProperty("query").GetProperty("type").GetString() == "string",
    "The raw generated search schema failed.");
var rawSubmit = PlannerToolContext.Default.Tools.Cast<AIFunction>().Single(f => f.Name == "submit_proposal");
Require(rawSubmit.JsonSchema.GetProperty("properties").GetProperty("sessionIds").GetProperty("type").GetString() == "array",
    "The raw generated array schema failed.");
Require(Text(await rawSearch.InvokeAsync(searchArguments)).Contains("building-with-maui"), "Generated JSON binding failed.");
Require(Text(await functions["search_sessions"].InvokeAsync(Arguments("""{"query":"Mobile","skip":0}""")))
    .Contains("building-with-maui"), "Adapted search failed.");
var approvalRequired = new ApprovalRequiredAIFunction(functions["search_sessions"]);
var geminiSearch = GeminiToolCompatibility.Adapt(approvalRequired);
Require(!geminiSearch.JsonSchema.TryGetProperty("additionalProperties", out _), "Gemini schema was not adapted.");
Require(geminiSearch.GetService<ApprovalRequiredAIFunction>() == approvalRequired, "Tool approval was lost.");
Require(Text(await geminiSearch.InvokeAsync(Arguments("""{"query":"Mobile","skip":0}""")))
    .Contains("building-with-maui"), "Gemini-compatible search failed.");
var beforeUnknownArgument = tools.CallCount;
try
{
    await geminiSearch.InvokeAsync(Arguments("""{"query":"Mobile","skip":0,"extra":true}"""));
    throw new InvalidOperationException("Unknown tool argument was accepted.");
}
catch (ArgumentException)
{
    Require(tools.CallCount == beforeUnknownArgument, "Unknown tool argument reached the function.");
}
Require(Text(await functions["speaker_sessions"].InvokeAsync(Arguments("""{"name":"Alex"}""")))
    .Contains("Alex Rivera"), "Generated optional speaker offset failed.");
Require(Text(await functions["topic_sessions"].InvokeAsync(new AIFunctionArguments()))
    .Contains("building-with-maui"), "Generated optional topic offset failed.");
Require(Text(await functions["catalog_topics"].InvokeAsync(new AIFunctionArguments()))
    .Contains("<topic-catalog>"), "Generated catalog binding failed.");

var submit = functions["submit_proposal"];
Require(Text(await submit.InvokeAsync(new AIFunctionArguments { ["sessionIds"] = new[] { "building-with-maui" } }))
    .StartsWith("Preview ready."), "Generated typed-array binding failed.");
Require(Text(await submit.InvokeAsync(Arguments("""{"sessionIds":["building-with-maui"]}""")))
    .StartsWith("Preview ready."), "Generated JSON-array binding failed.");
Require(tools.Proposal?.RequiredSpeakers.SequenceEqual(["Alex Rivera"]) == true, "Captured preferences changed.");

foreach (var json in new[] { "{}", """{"sessionIds":[42]}""", """{"sessionIds":null}""" })
{
    tools.SubmitProposal(["building-with-maui"]);
    Require(Text(await submit.InvokeAsync(Arguments(json))).StartsWith("Invalid"), "Invalid arguments looked successful.");
    Require(tools.Proposal is null, "Invalid arguments retained a stale preview.");
}
Require(Text(await functions["search_sessions"].InvokeAsync(Arguments("""{"query":"Mobile","skip":0.5}""")))
    .StartsWith("Invalid tool arguments:"), "Fractional offsets were accepted.");
Require(Text(await functions["search_sessions"].InvokeAsync(
    new AIFunctionArguments { ["query"] = new string('x', 101), ["skip"] = 0 }))
    .StartsWith("Invalid tool arguments:"), "Search argument bounds were lost.");
Require(Text(await submit.InvokeAsync(Arguments("""{"sessionIds":["invented-session"]}""")))
    .Contains("Unknown session"), "Unknown sessions were accepted.");

using var otherScope = services.CreateScope();
var other = otherScope.ServiceProvider.GetRequiredService<PlannerTools>();
other.Initialize(conference, TimeZoneInfo.Utc, []);
other.CapturePreferences(["Ortinau"], []);
Require(Text(await submit.InvokeAsync(new AIFunctionArguments
{
    ["sessionIds"] = new[] { "building-with-maui" }, Services = otherScope.ServiceProvider
})).StartsWith("Preview ready."), "Tool state was redirected to another scope.");
Require(other.Proposal is null && other.CallCount == 0, "Planner state leaked between scopes.");

using var cancellation = new CancellationTokenSource();
cancellation.Cancel();
var beforeCancellation = tools.CallCount;
try
{
    await submit.InvokeAsync(Arguments("""{"sessionIds":["building-with-maui"]}"""), cancellation.Token);
    throw new InvalidOperationException("Cancelled invocation executed.");
}
catch (OperationCanceledException)
{
    Require(tools.CallCount == beforeCancellation, "Cancellation consumed the tool budget.");
}

tools.Initialize(conference, TimeZoneInfo.Utc, []);
tools.CapturePreferences([], []);
for (var i = 0; i < 32; i++)
    await functions["catalog_topics"].InvokeAsync(new AIFunctionArguments());
try
{
    await functions["catalog_topics"].InvokeAsync(new AIFunctionArguments());
    throw new InvalidOperationException("Tool budget was not enforced.");
}
catch (InvalidOperationException exception) when (exception.Message.StartsWith("Planner tool limit reached."))
{
    Require(tools.CallCount == 33, "The invocation budget was not counted correctly.");
}

tools.Initialize(conference, TimeZoneInfo.Utc, []);
tools.CapturePreferences([], [], topicFilter: new(["Mobile"], false, false, false));
using var chat = new ToolCallingClient();
await PlannerDiscovery.RunAsync(chat, tools, scope.ServiceProvider, "Mobile",
    NullLogger.Instance, CancellationToken.None);
Require(chat.Calls == 2, "The model's tool request and result roundtrip did not execute.");
Require(tools.DiscoveryComplete && tools.CandidateIds.Contains("building-with-maui"),
    "Model-selected discovery lost real candidates.");
Require(tools.Proposal is null, "The model's read-only tools submitted a proposal.");

Console.WriteLine("PASS: NativeAOT planner availability, fixed Android function-calling model, generated bindings, Gemini schemas and approval, JSON and typed arguments, defaults, validation, scope isolation, cancellation, call budget and model-selected tool invocation through Microsoft.Extensions.AI. JSON reflection is explicitly enabled; dynamic code is unavailable.");

var ask = scope.ServiceProvider.GetRequiredService<AskTools>();
ask.Initialize(conference, TimeZoneInfo.Utc, "Save Mobile", []);
var askFunctions = ask.CreateFunctions(scope.ServiceProvider, useGeminiSchema: true).Cast<AIFunction>().ToDictionary(f => f.Name);
Require(askFunctions.Values.All(function => !function.JsonSchema.TryGetProperty("additionalProperties", out _)),
    "Ask must use Gemini-compatible native schemas.");
Require(askFunctions.Count == 2 && AskToolContext.Default.Tools.Count == 6,
    "Ask must offer only the requested capabilities from its curated context.");
Require(Text(await askFunctions["save_sessions"].InvokeAsync(Arguments("""{"query":"Mobile"}""")))
    .StartsWith("Action staged"), "Ask generated optional bindings failed under NativeAOT.");
Require(ask.Complete()?.Intent.Action == AskAction.SaveSession, "Ask lost its grounded pending action.");
ask.Initialize(conference, TimeZoneInfo.Utc, "Add all MAUI sessions to my schedule", []);
Require(Text(await askFunctions["save_sessions"].InvokeAsync(Arguments("""{"speaker":"MAUI"}""")))
    .StartsWith("Action staged"), "Ask couldn't ground a topic mislabeled as a speaker under NativeAOT.");
Require(ask.Complete() is { Intent: { AllMatches: true, Query: "MAUI", Speaker: "" }, Choices.Length: > 1 },
    "Ask must preserve the complete literal bulk topic under NativeAOT.");
ask.Initialize(conference, TimeZoneInfo.Utc, "Remove everything", []);
Require(ask.CreateFunctions(scope.ServiceProvider, useGeminiSchema: true).Single(f => f.Name == "request_removal") is ApprovalRequiredAIFunction,
    "Ask must preserve the curated removal approval marker.");
Console.WriteLine("PASS: NativeAOT Ask schemas, generated bindings, grounded action staging and removal approval metadata.");

static AIFunctionArguments Arguments(string json) =>
    JsonSerializer.Deserialize<AIFunctionArguments>(json, AIJsonUtilities.DefaultOptions)
    ?? throw new InvalidDataException("Missing function arguments.");

static string Text(object? result) =>
    result as string ?? throw new InvalidDataException("Planner function did not return text.");

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class ToolCallingClient : IChatClient
{
    public int Calls { get; private set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++Calls == 1)
        {
            if (options?.Tools?.Any(t => t.Name == "submit_proposal") != false)
                throw new InvalidOperationException("A mutation tool was exposed to the model.");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("lookup", "topic_sessions", new Dictionary<string, object?>())])));
        }
        var result = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        if (result.Result is not string text || !text.Contains("building-with-maui"))
            throw new InvalidOperationException("Generated tool results did not reach the model.");
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done.")));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
