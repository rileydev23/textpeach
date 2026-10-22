using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AvaloniaEdit;

namespace TextPeach.Controls;

/// <summary>
/// AvaloniaEdit 11.4's <c>TextEditor.ScrollToVerticalOffset</c>/<c>ScrollToHorizontalOffset</c> leave the view
/// where it was; setting the offset on the editor's own scroll viewer does move it.
/// </summary>
public static class EditorScroll
{
    public static void ScrollVerticallyTo(this TextEditor ed, double y)
    {
        if (Viewer(ed) is { } sv) sv.Offset = new Vector(sv.Offset.X, y);
        else ed.ScrollToVerticalOffset(y);
    }

    public static void ScrollHorizontallyTo(this TextEditor ed, double x)
    {
        if (Viewer(ed) is { } sv) sv.Offset = new Vector(x, sv.Offset.Y);
        else ed.ScrollToHorizontalOffset(x);
    }

    private static ScrollViewer? Viewer(TextEditor ed) => ed.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
}
