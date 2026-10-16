namespace DotNetConf.App.Controls;

public partial class PlannerView : ContentView
{
    public PlannerView() => InitializeComponent();

    public void SetHeroPadding(Thickness padding)
    {
        PlannerScroll.SafeAreaEdges = SafeAreaEdges.None;
        HeroContent.Padding = padding;
    }

    private void GenerateClicked(object? sender, EventArgs args) => RequestEditor.Unfocus();
}
