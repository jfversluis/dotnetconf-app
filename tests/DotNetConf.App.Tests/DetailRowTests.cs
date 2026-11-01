using DotNetConf.App.Controls;
using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
using DotNetConf.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotNetConf.App.Tests;

public sealed class DetailRowTests
{
    [Fact]
    public void SelectionTracksTheIdAndRecycledBindingContext()
    {
        using var http = new HttpClient();
        var images = new SpeakerImageCache(http, Path.GetTempPath(), NullLogger<SpeakerImageCache>.Instance);
        var owner = new ConferenceViewModel(null!, null!, null!, null!, null!, images,
            NullLogger<ConferenceViewModel>.Instance);
        var session = new SessionViewModel(new Session { Id = "synthetic-session" }, [], false, owner);
        var speaker = new SpeakerViewModel(new Speaker { Id = "synthetic-speaker" }, owner);
        var row = new DetailRow();
        var changes = new List<string?>();
        row.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        row.BindingContext = session;
        Assert.False(row.IsSelected);
        Assert.Equal(string.Empty, row.SelectionSuffix);
        row.SelectedId = session.Id;
        Assert.True(row.IsSelected);
        Assert.Equal(" Selected.", row.SelectionSuffix);
        Assert.Contains(nameof(DetailRow.IsSelected), changes);
        Assert.Contains(nameof(DetailRow.SelectionSuffix), changes);

        row.BindingContext = speaker;
        Assert.False(row.IsSelected);
        row.SelectedId = speaker.Id;
        Assert.True(row.IsSelected);
        row.BindingContext = new object();
        Assert.False(row.IsSelected);
        row.BindingContext = speaker;
        Assert.True(row.IsSelected);
        row.SelectedId = null;
        Assert.False(row.IsSelected);
        Assert.Equal(string.Empty, row.SelectionSuffix);
        Assert.Null(row.Clip);

        row.BindingContext = null;
        row.SelectedId = session.Id;
        Assert.False(row.IsSelected);
        row.BindingContext = session;
        Assert.True(row.IsSelected);
        Assert.Equal(" Selected.", row.SelectionSuffix);
    }
}
