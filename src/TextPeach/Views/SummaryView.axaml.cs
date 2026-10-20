using Avalonia.Controls;
using Avalonia.VisualTree;
using TextPeach.ViewModels;

namespace TextPeach.Views;

public partial class SummaryView : UserControl
{
    public SummaryView()
    {
        InitializeComponent();
        // Single click opens: these lists are navigation, not selection.
        BigList.SelectionChanged += (_, _) =>
        {
            if (BigList.SelectedItem is BigFileVM f) { Project?.OpenBigFile(f); BigList.SelectedItem = null; }
        };
        MarkerList.SelectionChanged += (_, _) =>
        {
            if (MarkerList.SelectedItem is MarkerRowVM m) { Project?.OpenMarker(m); MarkerList.SelectedItem = null; }
        };
    }

    private ProjectViewModel? Project => this.FindAncestorOfType<ProjectView>()?.DataContext as ProjectViewModel;
}
