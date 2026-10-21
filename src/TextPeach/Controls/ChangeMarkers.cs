using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using TextPeach.Services;

namespace TextPeach.Controls;

/// <summary>
/// Bars next to the line numbers that say how each line differs from the last commit: green added, amber modified,
/// a red wedge where lines were deleted. Display only, like the rest of git in TextPeach.
/// </summary>
public sealed class GitChangeMargin : AbstractMargin
{
    private const double BarWidth = 3, MarginWidth = 6;
    private LineMark[] _marks = Array.Empty<LineMark>();

    public LineMark[] Marks
    {
        get => _marks;
        set { _marks = value; InvalidateVisual(); }
    }

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    protected override void OnTextViewVisualLinesChanged() => InvalidateVisual();

    public override void Render(DrawingContext dc)
    {
        var view = TextView;
        if (view is null || !view.VisualLinesValid || _marks.Length == 0) return;
        double x = 1;
        foreach (var vl in view.VisualLines)
        {
            int no = vl.FirstDocumentLine.LineNumber;
            if (no < 1 || no > _marks.Length) continue;
            var m = _marks[no - 1];
            if (m == LineMark.None) continue;
            double top = vl.VisualTop - view.ScrollOffset.Y, h = vl.Height;
            if ((m & LineMark.Added) != 0) dc.FillRectangle(Palette.Green, new Rect(x, top, BarWidth, h));
            else if ((m & LineMark.Modified) != 0) dc.FillRectangle(Palette.Amber, new Rect(x, top, BarWidth, h));
            if ((m & LineMark.DeletedAbove) != 0) Wedge(dc, x, top);
            if ((m & LineMark.DeletedBelow) != 0) Wedge(dc, x, top + h);
        }
    }

    private static void Wedge(DrawingContext dc, double x, double y)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x - 1, y - 4), true);
            c.LineTo(new Point(x + 5, y));
            c.LineTo(new Point(x - 1, y + 4));
            c.EndFigure(true);
        }
        dc.DrawGeometry(Palette.Red, null, g);
    }
}

/// <summary>
/// Strip beside the editor's scroll bar with the whole file at scale: git changes on the left half, search matches
/// on the right, the caret as a line across. A click jumps there.
/// </summary>
public sealed class OverviewRuler : Control
{
    private LineMark[] _marks = Array.Empty<LineMark>();
    private IReadOnlyList<int> _matches = Array.Empty<int>();
    private int _lineCount = 1, _caretLine;

    /// <summary>The line (1-based) under a click.</summary>
    public event Action<int>? LineRequested;

    public OverviewRuler()
    {
        Width = 10;
        Cursor = new Cursor(StandardCursorType.Hand);
        ClipToBounds = true;
    }

    public void Update(LineMark[] marks, IReadOnlyList<int> matchLines, int lineCount, int caretLine)
    {
        _marks = marks;
        _matches = matchLines;
        _lineCount = Math.Max(1, lineCount);
        _caretLine = caretLine;
        InvalidateVisual();
    }

    public void SetCaret(int line)
    {
        if (line == _caretLine) return;
        _caretLine = line;
        InvalidateVisual();
    }

    private double Y(int line) => (line - 1) / (double)_lineCount * Bounds.Height;

    public override void Render(DrawingContext dc)
    {
        double h = Bounds.Height, w = Bounds.Width;
        dc.FillRectangle(Palette.Code, new Rect(0, 0, w, h));
        double minH = Math.Max(2, h / _lineCount);

        // Consecutive lines with the same mark become one rectangle.
        for (int i = 0; i < _marks.Length;)
        {
            var m = _marks[i];
            if (m == LineMark.None) { i++; continue; }
            var kind = m & (LineMark.Added | LineMark.Modified);
            int j = i + 1;
            while (kind != 0 && j < _marks.Length && (_marks[j] & (LineMark.Added | LineMark.Modified)) == kind) j++;
            var brush = kind == LineMark.Added ? Palette.Green : kind == LineMark.Modified ? Palette.Amber : Palette.Red;
            double top = Y(i + 1);
            dc.FillRectangle(brush, new Rect(0, top, 4, Math.Max(minH, Y(j + 1) - top)));
            i = j;
        }
        foreach (var line in _matches)
            dc.FillRectangle(Palette.MatchFg, new Rect(5, Y(line), w - 5, Math.Max(2, minH)));
        if (_caretLine > 0)
            dc.FillRectangle(Palette.Text, new Rect(0, Y(_caretLine), w, 1.5));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Bounds.Height <= 0) return;
        int line = (int)(e.GetPosition(this).Y / Bounds.Height * _lineCount) + 1;
        LineRequested?.Invoke(Math.Clamp(line, 1, _lineCount));
        e.Handled = true;
    }
}
