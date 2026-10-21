using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using TextPeach.Services;

namespace TextPeach.Controls;

/// <summary>
/// The current file against the last commit, read-only: one column with both line numbers (inline) or the two
/// versions side by side with scrolling kept together. Built from <see cref="LineDiff"/> rows.
/// </summary>
public sealed class DiffView : DockPanel
{
    /// <summary>Same cap as GitPeach's diffs (docs/DISENO.md, "Límites").</summary>
    public const int MaxRows = 20000;

    private enum Paint : byte { None, Added, Removed, Filler }

    private readonly Grid _body = new();
    private readonly TextBlock _stats = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly TextBlock _note = new() { Foreground = Palette.Dim, FontStyle = FontStyle.Italic, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly Button _inlineBtn, _sideBtn;
    private readonly List<TextEditor> _editors = new();
    private readonly List<TextMate.Installation> _grammars = new();
    private IReadOnlyList<DiffRow> _rows = Array.Empty<DiffRow>();
    private List<int> _changeRows = new();
    private string? _scope;
    private bool _sideBySide, _syncing;

    /// <summary>The reader switched layout; the choice is remembered for every file.</summary>
    public event Action<bool>? LayoutChanged;

    public double EditorFontSize { get; set; } = 13;
    public FontFamily EditorFont { get; set; } = new("Cascadia Mono, Consolas, Courier New");

    public DiffView()
    {
        Background = Palette.Code;
        _inlineBtn = ModeButton(Loc.T("diff.inline"), "left", () => SetLayout(false));
        _sideBtn = ModeButton(Loc.T("diff.sideBySide"), "right", () => SetLayout(true));
        var prev = SmallButton("↑", Loc.T("diff.prev"), () => Step(-1));
        var next = SmallButton("↓", Loc.T("diff.next"), () => Step(+1));

        var bar = new DockPanel { Margin = new Thickness(10, 4) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        right.Children.Add(prev);
        right.Children.Add(next);
        right.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), Children = { _inlineBtn, _sideBtn } });
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { new TextBlock { Text = Loc.T("diff.title"), Foreground = Palette.Dim, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 }, _stats },
        });
        var barHost = new Border { Background = Palette.Panel, BorderBrush = Palette.Border, BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };
        DockPanel.SetDock(barHost, Dock.Top);
        Children.Add(barHost);
        Children.Add(new Panel { Children = { _body, _note } });
    }

    private static Button ModeButton(string text, string side, Action click)
    {
        var b = new Button { Content = text };
        b.Classes.Add("mode");
        b.Classes.Add(side);
        b.Click += (_, _) => click();
        return b;
    }

    private static Button SmallButton(string text, string tip, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 2) };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>
    /// Shows a diff; <paramref name="scope"/> is the TextMate grammar of the file, if any. Opening it lands on the
    /// first change; a refresh (the file changed on disk) keeps the part of the diff that was in view.
    /// </summary>
    public void Show(IReadOnlyList<DiffRow> rows, string? scope, bool sideBySide, bool toFirstChange)
    {
        // A scroll still waiting for layout counts as the current position (a refresh can arrive right after opening).
        (int Row, bool Center)? keep = toFirstChange || _editors.Count == 0 ? null : _pendingScroll ?? (TopRow(), false);
        _rows = rows;
        _scope = scope;
        _sideBySide = sideBySide;
        int added = rows.Count(r => r.Kind == DiffKind.Added), removed = rows.Count(r => r.Kind == DiffKind.Removed);
        _stats.Inlines = new Avalonia.Controls.Documents.InlineCollection
        {
            new Avalonia.Controls.Documents.Run($"+{added}") { Foreground = Palette.Green },
            new Avalonia.Controls.Documents.Run("  "),
            new Avalonia.Controls.Documents.Run($"−{removed}") { Foreground = Palette.Red },
        };
        Build();
        var (row, center) = keep ?? (_changeRows.FirstOrDefault(-1), true);
        if (row >= 0) ScrollWhenLaidOut(row, center);
    }

    private (int Row, bool Center)? _pendingScroll;

    /// <summary>
    /// A diff that was just built has no size yet, and its scroll viewer learns the document height a little later:
    /// keep trying for a moment until the scroll sticks.
    /// </summary>
    private void ScrollWhenLaidOut(int row, bool center)
    {
        if (_editors.Count == 0) return;
        _pendingScroll = (row, center);
        var view = _editors[0].TextArea.TextView;
        int attempts = 0;
        void Apply()
        {
            // Rebuilt in the meantime: the new build owns the scroll.
            if (_editors.Count == 0 || _editors[0].TextArea.TextView != view) return;
            bool laidOut = view.Bounds.Height > 0 && view.VisualLinesValid;
            if ((laidOut && ScrollToRow(row, center)) || ++attempts >= 40) { _pendingScroll = null; return; }
            DispatcherTimer.RunOnce(Apply, TimeSpan.FromMilliseconds(25));
        }
        Dispatcher.UIThread.Post(Apply, DispatcherPriority.Background);
    }

    public void Clear()
    {
        _rows = Array.Empty<DiffRow>();
        _pendingScroll = null;
        Teardown();
    }

    private void SetLayout(bool sideBySide)
    {
        if (_sideBySide == sideBySide) return;
        _sideBySide = sideBySide;
        LayoutChanged?.Invoke(sideBySide);
        // Keep the same part of the file in view after rebuilding.
        int row = TopRow();
        Build();
        ScrollWhenLaidOut(row, center: false);
    }

    private void Teardown()
    {
        foreach (var g in _grammars) g.Dispose();
        _grammars.Clear();
        _editors.Clear();
        _body.Children.Clear();
        _body.ColumnDefinitions.Clear();
    }

    private void Build()
    {
        Teardown();
        _inlineBtn.Classes.Set("active", !_sideBySide);
        _sideBtn.Classes.Set("active", _sideBySide);
        if (_rows.Count > MaxRows)
        {
            _note.Text = Loc.F("diff.tooLarge", _rows.Count, MaxRows);
            _note.IsVisible = true;
            return;
        }
        _note.IsVisible = false;

        if (!_sideBySide)
        {
            var paint = _rows.Select(r => r.Kind switch { DiffKind.Added => Paint.Added, DiffKind.Removed => Paint.Removed, _ => Paint.None }).ToArray();
            var ed = MakeEditor(_rows.Select(r => r.Text), paint,
                new NumberMargin(_rows.Select(r => r.OldNo).ToArray(), _rows.Select(r => r.NewNo).ToArray(), paint));
            _body.Children.Add(ed);
            _changeRows = ChangeStarts(paint);
        }
        else
        {
            var pairs = LineDiff.SideBySide(_rows);
            Paint Of(DiffRow? r) => r is null ? Paint.Filler : r.Value.Kind switch { DiffKind.Added => Paint.Added, DiffKind.Removed => Paint.Removed, _ => Paint.None };
            var lp = pairs.Select(p => Of(p.Left)).ToArray();
            var rp = pairs.Select(p => Of(p.Right)).ToArray();
            var left = MakeEditor(pairs.Select(p => p.Left?.Text ?? ""), lp, new NumberMargin(pairs.Select(p => p.Left?.OldNo ?? 0).ToArray(), null, lp));
            var right = MakeEditor(pairs.Select(p => p.Right?.Text ?? ""), rp, new NumberMargin(null, pairs.Select(p => p.Right?.NewNo ?? 0).ToArray(), rp));
            _body.ColumnDefinitions = new ColumnDefinitions("*,1,*");
            Grid.SetColumn(right, 2);
            var split = new Border { Background = Palette.Border };
            Grid.SetColumn(split, 1);
            _body.Children.Add(left);
            _body.Children.Add(split);
            _body.Children.Add(right);
            Sync(left, right);
            Sync(right, left);
            _changeRows = ChangeStarts(pairs.Select((p, i) => lp[i] != Paint.None || rp[i] != Paint.None ? Paint.Added : Paint.None).ToArray());
        }
    }

    private static List<int> ChangeStarts(Paint[] paint)
    {
        var starts = new List<int>();
        for (int i = 0; i < paint.Length; i++)
            if (paint[i] != Paint.None && (i == 0 || paint[i - 1] == Paint.None)) starts.Add(i);
        return starts;
    }

    private TextEditor MakeEditor(IEnumerable<string> lines, Paint[] paint, NumberMargin numbers)
    {
        var ed = new TextEditor
        {
            IsReadOnly = true,
            ShowLineNumbers = false,
            FontSize = EditorFontSize,
            FontFamily = EditorFont,
            Background = Palette.Code,
            Foreground = Palette.Text,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        ed.Options.EnableHyperlinks = false;
        ed.Options.EnableEmailHyperlinks = false;
        ed.Options.HighlightCurrentLine = false;
        ed.Document = new AvaloniaEdit.Document.TextDocument(string.Join("\n", lines));
        var area = ed.TextArea;
        area.SelectionBrush = Palette.Selection;
        area.SelectionBorder = null;
        area.SelectionForeground = null;
        area.Caret.CaretBrush = Palette.Accent;
        area.LeftMargins.Insert(0, numbers);
        area.TextView.BackgroundRenderers.Add(new LinePainter(paint));
        try
        {
            _grammars.Add(SyntaxColoring.Install(ed, _scope));
        }
        catch { /* plain text is fine */ }
        _editors.Add(ed);
        return ed;
    }

    /// <summary>Side by side: scrolling either pane (wheel, scroll bar, keys) moves the other one too.</summary>
    private void Sync(TextEditor from, TextEditor to)
    {
        void Follow()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                to.ScrollToVerticalOffset(from.VerticalOffset);
                to.ScrollToHorizontalOffset(from.HorizontalOffset);
            }
            finally { _syncing = false; }
        }
        // The editor's own scroll viewer reports every scroll; the text view's event misses some of them.
        ScrollViewer? hooked = null;
        from.Loaded += (_, _) =>
        {
            var sv = from.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (sv is null || sv == hooked) return;
            hooked = sv;
            sv.ScrollChanged += (_, _) => Follow();
        };
        from.TextArea.TextView.ScrollOffsetChanged += (_, _) => Follow();
    }

    private int TopRow()
    {
        if (_editors.Count == 0) return 0;
        var view = _editors[0].TextArea.TextView;
        return view.GetDocumentLineByVisualTop(view.ScrollOffset.Y)?.LineNumber - 1 ?? 0;
    }

    private void Step(int dir)
    {
        if (_changeRows.Count == 0) return;
        int top = TopRow();
        var view = _editors.Count > 0 ? _editors[0].TextArea.TextView : null;
        // "Current" change = the one about a third of the way down, where ScrollToRow puts it.
        int anchor = view is null ? top : top + (int)(view.Bounds.Height / 3 / Math.Max(1, view.DefaultLineHeight));
        int target = dir > 0 ? _changeRows.FirstOrDefault(r => r > anchor, -1) : _changeRows.LastOrDefault(r => r < anchor - 1, -1);
        if (target >= 0) ScrollToRow(target, center: true);
    }

    /// <summary>Scrolls every pane to <paramref name="row"/>; false while the scroll viewer cannot reach it yet.</summary>
    private bool ScrollToRow(int row, bool center)
    {
        bool reached = true;
        foreach (var ed in _editors)
        {
            var view = ed.TextArea.TextView;
            var doc = ed.Document;
            if (doc.LineCount == 0) continue;
            int line = Math.Clamp(row + 1, 1, doc.LineCount);
            double y = view.GetVisualTopByDocumentLine(line);
            if (center) y -= view.Bounds.Height / 3;
            y = Math.Max(0, y);
            _syncing = true;
            try { ed.ScrollToVerticalOffset(y); }
            finally { _syncing = false; }
            // A fresh editor's scroll viewer learns the document height a moment after its first layout.
            if (Math.Abs(ed.VerticalOffset - y) > 1 && ed.VerticalOffset < y) reached = false;
        }
        return reached;
    }

    /// <summary>Full-width backgrounds: added, removed, and hatch-free filler where the other side has lines.</summary>
    private sealed class LinePainter(Paint[] paint) : IBackgroundRenderer
    {
        private static readonly IBrush Filler = new SolidColorBrush(Color.Parse("#202124"));
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView view, DrawingContext dc)
        {
            if (!view.VisualLinesValid) return;
            foreach (var vl in view.VisualLines)
            {
                int i = vl.FirstDocumentLine.LineNumber - 1;
                if (i < 0 || i >= paint.Length || paint[i] == Paint.None) continue;
                var brush = paint[i] switch { Paint.Added => Palette.AddedBg, Paint.Removed => Palette.RemovedBg, _ => Filler };
                dc.FillRectangle(brush, new Rect(0, vl.VisualTop - view.ScrollOffset.Y, view.Bounds.Width, vl.Height));
            }
        }
    }

    /// <summary>Old and/or new line numbers plus the +/− sign; blank where a side has no line.</summary>
    private sealed class NumberMargin(int[]? oldNos, int[]? newNos, Paint[] paint) : AbstractMargin
    {
        private static readonly Typeface Face = new("Cascadia Mono, Consolas, Courier New");
        private readonly int _digits = Math.Max(3, Math.Max(oldNos?.DefaultIfEmpty().Max() ?? 0, newNos?.DefaultIfEmpty().Max() ?? 0).ToString(CultureInfo.InvariantCulture).Length);
        private int Digits => _digits;
        private double TextSize => TextArea?.FontSize ?? 13;
        private double CharWidth => new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, TextSize, Brushes.White).Width;
        private int Columns => (oldNos is null ? 0 : 1) + (newNos is null ? 0 : 1);

        protected override Size MeasureOverride(Size availableSize) =>
            new(Columns * (Digits + 1) * CharWidth + 2 * CharWidth + 6, 0);

        protected override void OnTextViewVisualLinesChanged() => InvalidateVisual();

        public override void Render(DrawingContext dc)
        {
            var view = TextView;
            if (view is null || !view.VisualLinesValid) return;
            double em = TextSize, cw = CharWidth;
            double colW = (Digits + 1) * cw;
            foreach (var vl in view.VisualLines)
            {
                int i = vl.FirstDocumentLine.LineNumber - 1;
                if (i < 0 || i >= paint.Length) continue;
                double y = vl.VisualTop - view.ScrollOffset.Y;
                if (paint[i] is Paint.Added or Paint.Removed)
                    dc.FillRectangle(paint[i] == Paint.Added ? Palette.AddedBg : Palette.RemovedBg, new Rect(0, y, Bounds.Width, vl.Height));
                double x = 4;
                foreach (var nos in new[] { oldNos, newNos })
                {
                    if (nos is null) continue;
                    if (nos[i] > 0) DrawRight(dc, nos[i].ToString(CultureInfo.InvariantCulture), x + colW - cw, y, em, Palette.Gutter);
                    x += colW;
                }
                var sign = paint[i] switch { Paint.Added => "+", Paint.Removed => "−", _ => "" };
                if (sign.Length > 0)
                    DrawRight(dc, sign, x + cw, y, em, paint[i] == Paint.Added ? Palette.AddedFg : Palette.RemovedFg);
            }
        }

        private static void DrawRight(DrawingContext dc, string text, double right, double y, double size, IBrush brush)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush);
            dc.DrawText(ft, new Point(right - ft.Width, y + 1));
        }
    }
}
