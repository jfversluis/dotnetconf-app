using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class SessionsPage : ConferencePage
{
    public SessionsPage(ConferenceViewModel conference) : base(conference) => InitializeComponent();

    private void OnDayHeaderSizeChanged(object? sender, EventArgs e)
    {
        if (sender is not Label header || DayTabs is null)
            return;

        // The toolkit requires an explicit bar height; measure the scaled text instead of clipping it.
        DayTabs.TabBarHeight = header.Measure(double.PositiveInfinity, double.PositiveInfinity).Height;
    }
}
