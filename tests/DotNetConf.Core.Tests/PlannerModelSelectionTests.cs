using DotNetConf.App.Services;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;
using NSubstitute;

namespace DotNetConf.Core.Tests;

public sealed class PlannerModelSelectionTests
{
    [Fact]
    public void SelectionPersistsAndCanBeReadByAnotherConsumer()
    {
        var stored = "";
        var preferences = Substitute.For<IPreferences>();
        preferences.Get(PlannerModelSelection.PreferenceKey, "").Returns(_ => stored);
        preferences.When(p => p.Set(PlannerModelSelection.PreferenceKey, Arg.Any<string>()))
            .Do(call => stored = call.ArgAt<string>(1));
        var selection = Create(preferences);

        selection.Select("gemini-nano-preview-fast");

        Assert.Equal("gemini-nano-preview-fast", selection.SelectedModelId);
        Assert.Equal("gemini-nano-preview-fast", Create(preferences).SelectedModelId);
        selection.Select(null);
        Assert.Null(Create(preferences).SelectedModelId);
    }

    [Fact]
    public void InvalidSavedChoiceIsVisibleAndCanBeReplaced()
    {
        var preferences = Substitute.For<IPreferences>();
        preferences.Get(PlannerModelSelection.PreferenceKey, "").Returns("unknown-model");
        var selection = Create(preferences);

        Assert.Null(selection.SelectedModelId);
        Assert.Contains("no longer available", selection.Warning);
        selection.Select(null);
        Assert.Null(selection.Warning);
        preferences.Received(1).Set(PlannerModelSelection.PreferenceKey, "");
    }

    [Fact]
    public void FailedReadIsVisibleAndCanBeRetried()
    {
        var preferences = Substitute.For<IPreferences>();
        preferences.Get(PlannerModelSelection.PreferenceKey, "")
            .Returns(_ => throw new IOException("Synthetic storage failure"));
        var selection = Create(preferences);

        Assert.Null(selection.SelectedModelId);
        Assert.Contains("Couldn't read", selection.Warning);
        selection.Select(null);
        Assert.Null(selection.Warning);
        preferences.Received(1).Set(PlannerModelSelection.PreferenceKey, "");
    }

    [Fact]
    public void FailedSaveLeavesTheInMemoryChoiceUnchanged()
    {
        var preferences = Substitute.For<IPreferences>();
        preferences.Get(PlannerModelSelection.PreferenceKey, "").Returns("");
        preferences.When(p => p.Set(PlannerModelSelection.PreferenceKey, Arg.Any<string>()))
            .Do(_ => throw new IOException("Synthetic storage failure"));
        var selection = Create(preferences);
        var notifications = 0;
        selection.Changed += (_, _) => notifications++;

        Assert.Throws<IOException>(() => selection.Select("gemini-nano-preview-fast"));
        Assert.Null(selection.SelectedModelId);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void NotificationFollowsOnlyAnActualSuccessfulChange()
    {
        var preferences = Substitute.For<IPreferences>();
        preferences.Get(PlannerModelSelection.PreferenceKey, "").Returns("");
        var selection = Create(preferences);
        var notifications = new List<string?>();
        selection.Changed += (_, _) => notifications.Add(selection.SelectedModelId);

        selection.Select(null);
        selection.Select("gemini-nano-preview-fast");
        selection.Select("gemini-nano-preview-fast");

        Assert.Equal(["gemini-nano-preview-fast"], notifications);
    }

    [Fact]
    public void SelectionDoesNotKeepObserversAlive()
    {
        var preferences = Substitute.For<IPreferences>();
        preferences.Get(PlannerModelSelection.PreferenceKey, "").Returns("");
        var selection = Create(preferences);
        var observer = SubscribeObserver(selection);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(observer.IsAlive);
        selection.Select("gemini-nano-preview-fast");
    }

    [Fact]
    public void UnknownChoiceIsRejectedBeforeWritingPreferences()
    {
        var preferences = Substitute.For<IPreferences>();
        preferences.Get(PlannerModelSelection.PreferenceKey, "").Returns("");
        var selection = Create(preferences);

        Assert.Throws<ArgumentOutOfRangeException>(() => selection.Select("unknown-model"));
        preferences.DidNotReceive().Set(PlannerModelSelection.PreferenceKey, Arg.Any<string>());
    }

    private static PlannerModelSelection Create(IPreferences preferences) =>
        new(preferences, NullLogger<PlannerModelSelection>.Instance);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SubscribeObserver(PlannerModelSelection selection)
    {
        var observer = new Observer();
        selection.Changed += observer.OnChanged;
        return new WeakReference(observer);
    }

    private sealed class Observer
    {
        public void OnChanged(object? sender, EventArgs args) { }
    }
}
