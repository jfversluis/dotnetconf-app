using DotNetConf.App.Services;

namespace DotNetConf.Core.Tests;

public sealed class PlannerModelSelectionTests
{
    [Fact]
    public void AndroidPlannerUsesTheFunctionCallingModelWithoutPersistedSelection()
    {
        var selection = new PlannerModelSelection();

        Assert.False(selection.IsSupported);
        Assert.Equal("gemini-nano-preview-fast", selection.SelectedModelId);
        Assert.Null(selection.Warning);
    }

    [Fact]
    public void FixedModelCannotBeChangedToAProfileWithoutFunctionCalling()
    {
        var selection = new PlannerModelSelection();

        Assert.Throws<ArgumentOutOfRangeException>(() => selection.Select(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.Select("gemini-nano-fast"));
        Assert.Equal(PlannerModelSelection.ToolCallingModelId, selection.SelectedModelId);
    }
}
