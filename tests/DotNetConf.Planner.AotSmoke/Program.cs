using System.Runtime.CompilerServices;
using System.Text.Json;
using DotNetConf.App.Services;
using DotNetConf.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

Require(!RuntimeFeature.IsDynamicCodeSupported, "Run the published NativeAOT executable, not dotnet run.");
Require(JsonSerializer.IsReflectionEnabledByDefault, "AI Attributes preview 14 requires JSON reflection explicitly enabled.");

await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
var conference = await ConferenceJson.ReadAsync(stream);
using var services = new ServiceCollection().AddScoped<PlannerTools>().BuildServiceProvider(
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

Console.WriteLine("PASS: NativeAOT generated planner bindings, schemas, JSON and typed arguments, defaults, validation, scope isolation, cancellation and call budget. JSON reflection is explicitly enabled; dynamic code is unavailable.");

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
