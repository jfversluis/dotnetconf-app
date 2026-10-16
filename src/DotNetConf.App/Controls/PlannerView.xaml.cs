namespace DotNetConf.App.Controls;

public partial class PlannerView : ContentView
{
    public PlannerView() => InitializeComponent();

    private void GenerateClicked(object? sender, EventArgs args) => RequestEditor.Unfocus();
}
