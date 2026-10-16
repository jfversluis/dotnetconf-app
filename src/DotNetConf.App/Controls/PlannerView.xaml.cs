using System.ComponentModel;
using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Controls;

public partial class PlannerView : ContentView
{
    public PlannerView() => InitializeComponent();

    private PlannerViewModel? viewModel;
    private bool announceOutcome;

    protected override void OnBindingContextChanged()
    {
        if (viewModel is not null)
            viewModel.PropertyChanged -= PlannerPropertyChanged;
        base.OnBindingContextChanged();
        viewModel = BindingContext as PlannerViewModel;
        if (viewModel is not null)
            viewModel.PropertyChanged += PlannerPropertyChanged;
        announceOutcome = false;
    }

    public void SetHeroAction(View action) => HeroAction.Content = action;

    public void SetHeroPadding(Thickness padding)
    {
        PlannerScroll.SafeAreaEdges = SafeAreaEdges.None;
        HeroContent.Padding = padding;
    }

    private void GenerateClicked(object? sender, EventArgs args)
    {
        RequestEditor.Unfocus();
        PrepareOutcomeAnnouncement();
    }

    private void ApplyClicked(object? sender, EventArgs args) => PrepareOutcomeAnnouncement();

    private void PrepareOutcomeAnnouncement()
    {
        announceOutcome = viewModel?.IsBusy == true;
        if (!announceOutcome)
            AnnounceOutcome();
    }

    private void PlannerPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(PlannerViewModel.IsBusy) || viewModel is not { IsBusy: false }
            || !announceOutcome)
            return;
        announceOutcome = false;
        AnnounceOutcome();
    }

    private void AnnounceOutcome()
    {
        if (Handler is null || Window is null || viewModel is null)
            return;
        var message = viewModel.HasValidationErrors
            ? $"The shortlist cannot be saved. {viewModel.Validation}"
            : viewModel.HasStatus ? viewModel.Status
            : $"{viewModel.Sessions.Count} sessions in your shortlist. Review their times and confirm before saving.";
        SemanticScreenReader.Announce(message);
    }
}
