namespace DotNetConf.App.Services;

internal static class CommunityPaneLayout
{
    internal static void Apply(Grid panes, View list, View map, DetailLayout layout)
    {
        Grid.SetColumn(map, layout.IsSplit && !layout.IsStacked ? 2 : 0);
        Grid.SetRow(map, layout.IsStacked ? 2 : 0);
        Grid.SetRowSpan(list, layout.IsSplit && !layout.IsStacked ? 4 : 1);
        panes.ColumnDefinitions[0].Width = layout.IsStacked ? GridLength.Star : layout.ListLength;
        panes.ColumnDefinitions[1].Width = layout.IsStacked ? new(0) : layout.HingeLength;
        panes.ColumnDefinitions[2].Width = layout.IsStacked ? new(0) : layout.DetailLength;
        panes.RowDefinitions[0].Height = layout.IsStacked ? layout.ListLength : GridLength.Star;
        panes.RowDefinitions[1].Height = layout.IsStacked ? layout.HingeLength : new(0);
        panes.RowDefinitions[2].Height = layout.IsStacked ? GridLength.Star : new(0);
        panes.InvalidateMeasure();
    }
}
