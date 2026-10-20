using Avalonia;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Folding;
using AvaloniaEdit.TextMate;
using TextPeach.Controls;
using TextPeach.Services;
using TextPeach.ViewModels;

namespace TextPeach.Views;

/// <summary>
/// Hosts a single read-only TextEditor and swaps the document of whichever file tab is selected.
/// Avalonia recycles this view between project tabs, so everything is (re)attached on DataContext change.
/// </summary>
public partial class ProjectView : UserControl
{
    private ProjectViewModel? _vm;
    private OpenFileViewModel? _file;
    private TextMate.Installation? _tm;
    private FoldingManager? _folding;
    private ViewerSettings? _settings;

    public ProjectView()
    {
        InitializeComponent();
        ConfigureEditor();

        FileTree.DoubleTapped += (_, e) =>
        {
            if (e.Source is Control c && c.FindAncestorOfType<TreeViewItem>()?.DataContext is FileNode { IsDirectory: true } n)
                n.IsExpanded = !n.IsExpanded;
        };
        FilterBox.KeyDown += (_, e) =>
        {
            if (_vm is null) return;
            switch (e.Key)
            {
                case Key.Down when _vm.IsFiltering:
                    HitList.Focus();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    _vm.OpenSelectedHit();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    _vm.FileFilter = "";
                    e.Handled = true;
                    break;
            }
        };
        HitList.Tapped += (_, _) => _vm?.OpenSelectedHit();
        HitList.KeyDown += (_, e) => { if (e.Key == Key.Enter) { _vm?.OpenSelectedHit(); e.Handled = true; } };
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { _vm?.SearchCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Key.Down) { SearchList.Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Editor.TextArea.Focus(); e.Handled = true; }
        };
        OutlineList.Tapped += (_, e) =>
        {
            if (Item<OutlineRowVM>(e) is { } o) { _file?.Reveal(o.Line); Editor.TextArea.Focus(); }
        };
        MarkerList.Tapped += (_, e) =>
        {
            if (Item<MarkerRowVM>(e) is { } m) { _file?.Reveal(m.Line); Editor.TextArea.Focus(); }
        };

        DataContextChanged += (_, _) => Attach();
    }

    private static T? Item<T>(Avalonia.Interactivity.RoutedEventArgs e) where T : class =>
        (e.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.DataContext as T;

    // ------------------------------------------------------------ editor setup

    private void ConfigureEditor()
    {
        var o = Editor.Options;
        o.HighlightCurrentLine = true;
        o.EnableHyperlinks = false;
        o.EnableEmailHyperlinks = false;
        o.AllowScrollBelowDocument = true;
        o.EnableRectangularSelection = true;
        o.ConvertTabsToSpaces = false;
        o.CutCopyWholeLine = true;
        var area = Editor.TextArea;
        area.TextView.CurrentLineBackground = new SolidColorBrush(Color.Parse("#232428"));
        area.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        area.SelectionBrush = new SolidColorBrush(Color.Parse("#33507a"));
        area.SelectionBorder = null;
        area.SelectionCornerRadius = 0;
        area.SelectionForeground = null;
        area.Caret.CaretBrush = Palette.Accent;
        area.Caret.PositionChanged += (_, _) => UpdateCaret();
        area.SelectionChanged += (_, _) => UpdateCaret();
        // Block typing/paste in read-only mode even for the clipboard shortcuts AvaloniaEdit maps itself.
        Editor.IsReadOnly = true;
    }

    private void ApplySettings()
    {
        if (_settings is null) return;
        Editor.FontSize = _settings.FontSize;
        Editor.WordWrap = _settings.WordWrap;
        Editor.Options.ShowSpaces = _settings.ShowWhitespace;
        Editor.Options.ShowTabs = _settings.ShowWhitespace;
        Editor.Options.ShowEndOfLine = _settings.ShowWhitespace;
    }

    // ------------------------------------------------------------ view-model wiring

    private void Attach()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.SearchFocusRequested -= OnSearchFocus;
            _vm.FilterFocusRequested -= OnFilterFocus;
            _vm.EditorFocusRequested -= OnEditorFocus;
        }
        if (_settings is not null) _settings.PropertyChanged -= OnSettingsChanged;

        _vm = DataContext as ProjectViewModel;
        _settings = _vm?.Settings;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.SearchFocusRequested += OnSearchFocus;
            _vm.FilterFocusRequested += OnFilterFocus;
            _vm.EditorFocusRequested += OnEditorFocus;
        }
        if (_settings is not null) _settings.PropertyChanged += OnSettingsChanged;
        ApplySettings();
        SwitchTo(_vm?.SelectedFile);
        // Project shortcuts live on this view, so something inside it must own the focus.
        if (_vm is not null)
        {
            if (_vm.HasFile) OnEditorFocus();
            else OnFilterFocus();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectViewModel.SelectedDoc)) SwitchTo(_vm?.SelectedFile);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => ApplySettings();

    private void OnSearchFocus() => Dispatcher.UIThread.Post(() =>
    {
        var sel = _file is not null ? Editor.SelectedText : "";
        if (_vm is not null && sel.Length is > 0 and < 200 && !sel.Contains('\n')) _vm.SearchText = sel;
        SearchBox.Focus();
        SearchBox.SelectAll();
    });

    private void OnFilterFocus() => Dispatcher.UIThread.Post(() => { FilterBox.Focus(); FilterBox.SelectAll(); });

    private void OnEditorFocus() => Dispatcher.UIThread.Post(() => Editor.TextArea.Focus(), DispatcherPriority.Background);

    // ------------------------------------------------------------ document switching

    private void SwitchTo(OpenFileViewModel? f)
    {
        if (ReferenceEquals(f, _file)) return;
        if (_file is not null)
        {
            SaveViewState(_file);
            if (_file.IsRendered) _file.RenderedOffset = RenderedHost.Offset.Y;
            _file.RevealRequested -= OnReveal;
            _file.Reloaded -= OnReloaded;
            _file.PropertyChanged -= OnFilePropertyChanged;
        }
        _file = f;
        UninstallFolding();
        if (f is null) return;

        f.RevealRequested += OnReveal;
        f.Reloaded += OnReloaded;
        f.PropertyChanged += OnFilePropertyChanged;

        Editor.Document = f.Document;
        Editor.IsReadOnly = !f.IsEditing;
        ApplyGrammar(f);
        InstallFolding(f);
        RestoreViewState(f);
        UpdateRendered(restoreScroll: true);
        UpdateCaret();
    }

    // ------------------------------------------------------------ rendered view (Markdown / text)

    private sealed class RenderCache
    {
        public int Version;
        public required RenderedDocument Doc;
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<OpenFileViewModel, RenderCache> _rendered = new();

    /// <summary>Shows the rendered document of the current file, rebuilding it only when the text changed.</summary>
    private void UpdateRendered(bool restoreScroll)
    {
        var f = _file;
        if (f is null || !f.IsRenderedVisible) return;
        if (!_rendered.TryGetValue(f, out var cache) || cache.Version != f.ContentVersion)
        {
            var doc = DocumentRenderer.Render(f.RenderMode, f.Document.Text, Path.GetDirectoryName(f.FullPath) ?? "", url => OnDocumentLink(f, url));
            cache = new RenderCache { Version = f.ContentVersion, Doc = doc };
            _rendered.AddOrUpdate(f, cache);
        }
        if (!ReferenceEquals(RenderedHost.Content, cache.Doc.Root))
        {
            RenderedHost.Content = cache.Doc.Root;
            restoreScroll = true;
        }
        if (restoreScroll)
        {
            double y = f.RenderedOffset;
            Dispatcher.UIThread.Post(() => RenderedHost.Offset = new Vector(0, y), DispatcherPriority.Background);
        }
    }

    private void OnDocumentLink(OpenFileViewModel f, string url)
    {
        if (url.StartsWith('#'))
        {
            if (_rendered.TryGetValue(f, out var cache) && cache.Doc.Anchors.TryGetValue(url[1..], out var target))
                ScrollRenderedTo(target);
            return;
        }
        _vm?.OpenLink(f.Directory, url);
    }

    private void ScrollRenderedTo(Control target)
    {
        if (RenderedHost.Content is not Visual root) return;
        var p = target.TranslatePoint(new Point(0, 0), root);
        if (p is { } pt) RenderedHost.Offset = new Vector(0, Math.Max(0, pt.Y - 12));
    }

    private void ApplyGrammar(OpenFileViewModel f)
    {
        // A fresh installation per document is the simplest way to keep TextMate's model in sync.
        _tm?.Dispose();
        _tm = null;
        try
        {
            _tm = Editor.InstallTextMate(Highlighting.Instance);
            _tm.SetTheme(Highlighting.Instance.Theme);
            if (f.Scope is not null) _tm.SetGrammar(f.Scope);
        }
        catch { /* plain text is fine */ }
    }

    private void InstallFolding(OpenFileViewModel f)
    {
        if (!f.IsText) return;
        _folding = FoldingManager.Install(Editor.TextArea);
        foreach (var m in Editor.TextArea.LeftMargins.OfType<FoldingMargin>())
        {
            m.FoldingMarkerBrush = Palette.Dim;
            m.FoldingMarkerBackgroundBrush = Palette.Code;
            m.SelectedFoldingMarkerBrush = Palette.Text;
            m.SelectedFoldingMarkerBackgroundBrush = Palette.Panel2;
        }
        UpdateFoldings();
    }

    private void UpdateFoldings()
    {
        if (_folding is null || _file is null) return;
        _folding.UpdateFoldings(Controls.Folding.Compute(_file.Document, _file.Language), -1);
    }

    private void UninstallFolding()
    {
        if (_folding is null) return;
        FoldingManager.Uninstall(_folding);
        _folding = null;
    }

    private void OnFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_file is null) return;
        switch (e.PropertyName)
        {
            case nameof(OpenFileViewModel.IsEditing):
                Editor.IsReadOnly = !_file.IsEditing;
                if (_file.IsEditing) OnEditorFocus();
                break;
            case nameof(OpenFileViewModel.IsLoading) when !_file.IsLoading && _folding is null:
                InstallFolding(_file);
                break;
            case nameof(OpenFileViewModel.Analysis) when _file.IsEditing:
                UpdateFoldings();
                break;
            case nameof(OpenFileViewModel.IsRendered):
                if (!_file.IsRendered) _file.RenderedOffset = RenderedHost.Offset.Y;
                UpdateRendered(restoreScroll: true);
                UpdateCaret();
                break;
            case nameof(OpenFileViewModel.ContentVersion):
                UpdateRendered(restoreScroll: false);
                break;
        }
    }

    private void OnReloaded()
    {
        if (_file is null) return;
        if (_folding is null) InstallFolding(_file);
        else UpdateFoldings();
        UpdateRendered(restoreScroll: false);
        UpdateCaret();
        // First load fills an empty document, which leaves the caret at the end: start at the top instead.
        if (!_file.ViewStateSaved && ReferenceEquals(Editor.Document, _file.Document))
        {
            _file.ViewStateSaved = true;
            Editor.CaretOffset = 0;
            Editor.ScrollToVerticalOffset(0);
        }
    }

    private void SaveViewState(OpenFileViewModel f)
    {
        if (!ReferenceEquals(Editor.Document, f.Document)) return;
        f.CaretOffset = Editor.CaretOffset;
        f.VerticalOffset = Editor.VerticalOffset;
        f.HorizontalOffset = Editor.HorizontalOffset;
        f.ViewStateSaved = true;
    }

    private void RestoreViewState(OpenFileViewModel f)
    {
        var doc = f.Document;
        Editor.CaretOffset = f.ViewStateSaved ? Math.Clamp(f.CaretOffset, 0, doc.TextLength) : 0;
        double v = f.ViewStateSaved ? f.VerticalOffset : 0, h = f.ViewStateSaved ? f.HorizontalOffset : 0;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(Editor.Document, doc)) return;
            Editor.ScrollToVerticalOffset(v);
            Editor.ScrollToHorizontalOffset(h);
        }, DispatcherPriority.Background);
    }

    private void OnReveal(int line, int col, int len)
    {
        var f = _file;
        if (f is { IsRendered: true })
        {
            // Outline/marker jumps stay in the rendered view; a search hit needs the exact text, so show the code.
            if (len == 0)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(_file, f) && _rendered.TryGetValue(f, out var cache) && cache.Doc.BlockAt(line) is { } block)
                        ScrollRenderedTo(block);
                }, DispatcherPriority.Background);
                return;
            }
            f.IsRendered = false;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (f is null || !ReferenceEquals(Editor.Document, f.Document)) return;
            var doc = f.Document;
            if (doc.LineCount == 0) return;
            var dl = doc.GetLineByNumber(Math.Clamp(line, 1, doc.LineCount));
            int offset = dl.Offset + Math.Clamp(col - 1, 0, dl.Length);
            int length = Math.Clamp(len, 0, doc.TextLength - offset);
            if (_folding is not null)
                foreach (var fs in _folding.GetFoldingsContaining(offset)) fs.IsFolded = false;
            Editor.CaretOffset = offset;
            if (length > 0) Editor.Select(offset, length);
            else Editor.TextArea.ClearSelection();
            // Put the target roughly a third of the way down instead of at the very edge.
            var view = Editor.TextArea.TextView;
            double lineTop = view.GetVisualTopByDocumentLine(dl.LineNumber);
            double target = Math.Max(0, lineTop - view.Bounds.Height / 3);
            if (lineTop < Editor.VerticalOffset || lineTop > Editor.VerticalOffset + view.Bounds.Height - 2 * view.DefaultLineHeight)
                Editor.ScrollToVerticalOffset(target);
            Editor.ScrollToHorizontalOffset(0);
            Editor.TextArea.Caret.BringCaretToView();
        }, DispatcherPriority.Background);
    }

    private void UpdateCaret()
    {
        if (_vm is null || _file is null || !ReferenceEquals(Editor.Document, _file.Document)) return;
        if (_file.IsRendered) { _vm.CaretText = "Vista renderizada"; return; }
        var caret = Editor.TextArea.Caret;
        var sel = Editor.SelectionLength;
        _vm.CaretText = sel > 0 ? $"Ln {caret.Line}, Col {caret.Column} ({sel} sel.)" : $"Ln {caret.Line}, Col {caret.Column}";
    }
}
