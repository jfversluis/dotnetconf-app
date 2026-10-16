using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Controls;

public partial class PlannerView : ContentView
{
    public PlannerView()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachViewModel();
        Unloaded += (_, _) => DetachViewModel();
    }

    private PlannerViewModel? viewModel;
    public bool IsActive { get; set; } = true;

    protected override void OnBindingContextChanged()
    {
        DetachViewModel();
        base.OnBindingContextChanged();
        if (IsLoaded)
            AttachViewModel();
    }

    public void SetHeroAction(View action) => HeroAction.Content = action;

    public void UseOnboardingSafeArea()
    {
        PlannerScroll.SafeAreaEdges = SafeAreaEdges.None;
    }

    private void AttachViewModel()
    {
        DetachViewModel();
        viewModel = BindingContext as PlannerViewModel;
        if (viewModel is not null)
            viewModel.OutcomeAvailable += OnOutcomeAvailable;
    }

    private void DetachViewModel()
    {
        if (viewModel is not null)
            viewModel.OutcomeAvailable -= OnOutcomeAvailable;
        viewModel = null;
    }

    private void OnOutcomeAvailable(object? sender, string message)
    {
        if (IsActive && IsLoaded && Window is not null)
            SemanticScreenReader.Announce(message);
    }

    private void GenerateClicked(object? sender, EventArgs args) => RequestEditor.Unfocus();
}
