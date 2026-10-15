using DotNetConf.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
#if IOS
using Microsoft.Maui.Essentials.AI;
#endif

namespace DotNetConf.App.Services;

public sealed record PlannerResult(ScheduleProposal? Proposal, string Message);

public sealed class OnDevicePlanner(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
{
    public static string? UnsupportedReason =>
#if IOS
        OperatingSystem.IsIOSVersionAtLeast(26) ? null :
        "This spike needs iOS 26 or later with Apple Intelligence enabled and its model downloaded.";
#else
        "On-device planning is currently available only on supported Apple Intelligence iPhones running iOS 26+. Android inference is not available in this package.";
#endif

    public async Task CheckReadinessAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient(null);
        // This package has no public availability API. A real, short local inference
        // checks model readiness, not just the OS version or device name.
        await client.GetResponseAsync("Reply with READY.", new ChatOptions { MaxOutputTokens = 16 }, cancellationToken);
    }

    public async Task<PlannerResult> ProposeAsync(ConferenceDocument conference, string request,
        TimeZoneInfo zone, string[] savedIds, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, zone, savedIds);
        using var client = CreateClient(scope.ServiceProvider).AsBuilder()
            .UseFunctionInvocation(configure: invocation => invocation.MaximumIterationsPerRequest = 10)
            .Build(scope.ServiceProvider);
        var first = TimeZoneInfo.ConvertTime(conference.Sessions.Min(s => s.StartsAt), zone);
        var last = TimeZoneInfo.ConvertTime(conference.Sessions.Max(s => s.EndsAt), zone);
        ChatResponse response;
        try
        {
            var extraction = $"""
                Extract conference preferences from the user's words using capture_preferences once.
                Conference dates: {first:yyyy-MM-dd} through {last:yyyy-MM-dd}. Device zone: {zone.Id}.
                Copy ALL must-see speaker names verbatim, even unknown names.
                Copy ALL availability windows into matching start, end and zone arrays.
                For November 10 from 16:00 to 18:00 UTC, use:
                availabilityStarts=["2026-11-10T16:00"], availabilityEnds=["2026-11-10T18:00"], availabilityZones=["UTC"].
                Use zone "local" for device times. Do not convert time zones. Empty arrays ONLY if unrestricted.
                If unrestricted, ALL THREE availability arrays must be empty, including availabilityZones.
                Never replace availability with session times. Do not relax or drop any constraint.
                If the tool reports invalid arguments, correct them and call it again.
                If a date, time zone or requirement is unclear, ask for clarification and do not call the tool.
                """;
            var extracted = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.System, extraction), new ChatMessage(ChatRole.User, request)],
                new ChatOptions { Tools = [tools.CreatePreferenceFunction()], MaxOutputTokens = 350 },
                cancellationToken);
            if (tools.Preferences is not { } preferences)
                return new(null, $"No preferences were captured. {extracted.Text}");
            cancellationToken.ThrowIfCancellationRequested();
            var instructions = $"""
                Propose a conference schedule using the agenda tools. The agenda is fictional sample content.
                Preferences have already been captured and are immutable during this step.
                Required speakers: {string.Join(", ", preferences.RequiredSpeakers)}.
                Searches return only sessions satisfying availability and not conflicting with existing likes.
                Tool result times are in {zone.Id}. Never invent or modify IDs, speakers or times.
                Search a short topic or look up required speakers. Use at most 8 tool calls.
                Select up to 12 sessions and call submit_proposal with their exact IDs.
                Include at least one session for each required speaker and avoid overlapping proposals.
                If submit_proposal reports errors, correct the selection and resubmit without relaxing constraints.
                If no valid schedule is possible, explain why and submit an empty list so the user sees the constraints.
                Nothing is saved: this is a preview for user confirmation.
                """;
            response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.System, instructions), new ChatMessage(ChatRole.User, request)],
                new ChatOptions { Tools = [.. tools.CreateFunctions()], MaxOutputTokens = 700 },
                cancellationToken);
        }
        finally
        {
            loggerFactory.CreateLogger<OnDevicePlanner>().LogInformation(
                "Local planning used {ToolCalls} app function calls.", tools.CallCount);
        }
        return new(tools.Proposal ?? tools.Preferences, response.Text);
    }

    private IChatClient CreateClient(IServiceProvider? services)
    {
        if (UnsupportedReason is { } reason)
            throw new PlatformNotSupportedException(reason);
#if IOS
        if (OperatingSystem.IsIOSVersionAtLeast(26))
#pragma warning disable MAUIAI0001
            return new AppleIntelligenceChatClient(loggerFactory, services);
#pragma warning restore MAUIAI0001
#endif
        throw new PlatformNotSupportedException("Apple Intelligence is unavailable.");
    }
}
