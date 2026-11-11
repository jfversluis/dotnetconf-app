using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Controls;

public partial class PlannerView : ContentView
{
    public PlannerView()
    {
        InitializeComponent();
        SizeChanged += UpdateStatusHeight;
        RequestEditor.Focused += UpdateStatusHeight;
        RequestEditor.Unfocused += UpdateStatusHeight;
        StatusLabel.PropertyChanged += async (_, args) =>
        {
            if (args.PropertyName == nameof(Label.Text) && StatusScroll.ScrollY > 0)
                await StatusScroll.ScrollToAsync(0, 0, false);
        };
        StatusLabel.SizeChanged += UpdateStatusScrollHint;
        StatusScroll.SizeChanged += UpdateStatusScrollHint;
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

    private void UpdateStatusHeight(object? sender, EventArgs args)
    {
        if (Height > 0)
            StatusScroll.MaximumHeightRequest = Math.Min(RequestEditor.IsFocused ? 120 : 240, Height / 3);
    }

    private void UpdateStatusScrollHint(object? sender, EventArgs args)
    {
        // The viewport can still have its previous height while layout updates.
        StatusScrollHint.IsVisible = StatusLabel.Height > StatusScroll.MaximumHeightRequest + 1;
    }

    private async void ReviewShortlistClicked(object? sender, EventArgs args)
    {
        await PlannerScroll.ScrollToAsync(ShortlistHeading, ScrollToPosition.Start, false);
        ShortlistHeading.SetSemanticFocus();
    }

    private async void ShowModelSetupClicked(object? sender, EventArgs args)
    {
        await PlannerScroll.ScrollToAsync(ModelSetup, ScrollToPosition.Start, false);
        ModelSetupHeadingLabel.SetSemanticFocus();
    }

    private async void ExploreClicked(object? sender, EventArgs args)
    {
        if (viewModel is null || !await ConfirmReplacingPreviewAsync())
            return;
        await viewModel.ExploreCommand.ExecuteAsync(null);
    }

    private async void UseAiClicked(object? sender, EventArgs args)
    {
        if (viewModel is null || !await ConfirmReplacingPreviewAsync())
            return;
        viewModel.UseAiModeCommand.Execute(null);
    }

    private async Task<bool> ConfirmReplacingPreviewAsync()
    {
        if (viewModel is null || !viewModel.HasProposal || viewModel.SelectedCount == 0)
            return true;
        var page = Navigation.ModalStack.LastOrDefault() ?? Shell.Current?.CurrentPage;
        if (page is null)
            return false;
        return await page.DisplayAlertAsync("Replace these suggestions?",
            "Your checked suggestions haven't been added to My Schedule. Add them first, or replace this preview.",
            "Replace suggestions", "Keep choosing");
    }

    private void ToggleDescriptionClicked(object? sender, EventArgs args)
    {
        if (sender is Button { BindingContext: ProposedSessionViewModel session })
            session.ShowDescription = !session.ShowDescription;
    }

    private async void GenerateClicked(object? sender, EventArgs args)
    {
        if (viewModel is null || !await ConfirmReplacingPreviewAsync())
            return;
        RequestEditor.Unfocus();
        await viewModel.GenerateCommand.ExecuteAsync(null);
    }
}
