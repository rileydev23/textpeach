using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TextPeach.Controls;
using TextPeach.Services;
using TextPeach.Views;

namespace TextPeach.ViewModels;

/// <summary>One file open for reading inside a project. Read-only unless the user presses "Editar".</summary>
public sealed partial class OpenFileViewModel : TabViewModel, IDisposable
{
    public string FullPath { get; }
    public string RelPath { get; }
    public LanguageInfo Language { get; }
    public string? Scope { get; }
    public TextDocument Document { get; } = new();

    public string FileName => Path.GetFileName(FullPath);
    public string Directory => RelPath.LastIndexOf('/') is var i and > 0 ? RelPath[..(i + 1)] : "";
    public string LanguageName => Language.Name;
    public Avalonia.Media.IBrush LanguageBrush => Palette.FromHex(Language.Color);

    [ObservableProperty] private FileKind _kind = FileKind.Text;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _encodingName = "";
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string _imageInfo = "";
    [ObservableProperty] private string _noteText = "";
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _changedOnDisk;
    [ObservableProperty] private string _infoText = "";
    [ObservableProperty] private string _lineEnding = "";
    [ObservableProperty] private string _indentation = "";
    [ObservableProperty] private IReadOnlyList<MetricRow> _metrics = Array.Empty<MetricRow>();
    [ObservableProperty] private IReadOnlyList<OutlineRowVM> _outline = Array.Empty<OutlineRowVM>();
    [ObservableProperty] private IReadOnlyList<MarkerRowVM> _markers = Array.Empty<MarkerRowVM>();
    [ObservableProperty] private FileAnalysis _analysis = FileAnalysis.Empty;
    [ObservableProperty] private bool _isRendered;
    /// <summary>Shows the changes against the last commit instead of the file.</summary>
    [ObservableProperty] private bool _isDiff;
    /// <summary>Bumped on every (re)load so the rendered view knows to rebuild.</summary>
    [ObservableProperty] private int _contentVersion;

    /// <summary>How this file can be shown besides plain code.</summary>
    public RenderKind RenderMode { get; }
    private bool _renderDecided;
    private readonly bool _preferCode;

    public bool IsText => Kind == FileKind.Text && !IsLoading;
    public bool CanRender => RenderMode != RenderKind.None && IsText;
    public bool IsCodeVisible => IsText && !IsRendered && !IsDiff;
    public bool IsRenderedVisible => IsText && IsRendered && !IsDiff;
    public bool IsDiffVisible => IsText && IsDiff;
    public bool IsImage => Kind == FileKind.Image;
    public bool IsNote => Kind is FileKind.Binary or FileKind.TooLarge or FileKind.Error;
    public bool CanEdit => Kind == FileKind.Text && !IsEditing && !IsLoading;
    public string OutlineHeader => Loc.F("file.outline", Outline.Count);
    public string MarkersHeader => Loc.F("file.markers", Markers.Count);

    // Where the reader was, so switching tabs comes back to the same spot.
    public int CaretOffset { get; set; }
    public double VerticalOffset { get; set; }
    public double HorizontalOffset { get; set; }
    public bool ViewStateSaved { get; set; }
    public double RenderedOffset { get; set; }
    /// <summary>Line of the caret as the view last saw it, for back/forward navigation.</summary>
    public int CaretLine { get; set; } = 1;

    // ---- changes against the last commit (read-only, like all git here)

    /// <summary>Gutter marks per line of <see cref="Document"/>; empty when unchanged or not under git.</summary>
    public LineMark[] Marks { get; private set; } = Array.Empty<LineMark>();
    /// <summary>The full diff, for the diff view; null when there is nothing to compare with.</summary>
    public IReadOnlyList<DiffRow>? DiffRows { get; private set; }
    [ObservableProperty] private bool _hasChanges;
    [ObservableProperty] private string _diffStats = "";
    public string? HeadText { get; private set; }
    private GitChange _gitChange;
    private string? _repoRoot, _repoPath, _headId;
    private bool _gitKnown;
    private CancellationTokenSource? _diffCts;

    /// <summary>Asks the view to scroll to (line, column) and select <c>length</c> characters.</summary>
    public event Action<int, int, int>? RevealRequested;
    /// <summary>The document was replaced from disk; the view should refresh highlighting/folding.</summary>
    public event Action? Reloaded;
    /// <summary>New gutter marks / diff are ready.</summary>
    public event Action? MarksChanged;
    /// <summary>The reader picked the rendered (true) or code (false) view, to remember it for this file.</summary>
    public event Action<bool>? ViewModeChosen;

    private Encoding _encoding = new UTF8Encoding(false);
    private DateTime _modified;
    private long _size;
    private bool _loadingText;
    private DateTime _ignoreDiskUntil;
    private CancellationTokenSource? _analysisCts;
    private (int Line, int Col, int Len)? _pendingReveal;

    public OpenFileViewModel(string root, string relPath, bool preferCode = false)
    {
        _preferCode = preferCode;
        RelPath = relPath;
        FullPath = Path.Combine(root, relPath.Replace('/', Path.DirectorySeparatorChar));
        Language = Languages.For(FullPath);
        Scope = Highlighting.Instance.ScopeFor(FullPath);
        RenderMode = Path.GetExtension(FullPath).ToLowerInvariant() switch
        {
            ".md" or ".markdown" or ".mdx" => RenderKind.Markdown,
            ".txt" or ".text" => RenderKind.PlainText,
            _ => RenderKind.None,
        };
        Title = FileName;
        ToolTip = relPath;
        Document.TextChanged += (_, _) =>
        {
            if (_loadingText || !IsEditing) return;
            IsDirty = true;
            ScheduleAnalysis(TimeSpan.FromMilliseconds(800));
        };
        _ = LoadAsync(keepView: false);
    }

    partial void OnKindChanged(FileKind value) => NotifyKind();
    partial void OnIsLoadingChanged(bool value) => NotifyKind();
    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        UpdateTitle();
        // A file being edited must not be replaced by the next single-click open.
        if (value) IsPreview = false;
    }
    partial void OnIsRenderedChanged(bool value) => NotifyViews();
    partial void OnIsDiffChanged(bool value) => NotifyViews();

    private void NotifyViews()
    {
        OnPropertyChanged(nameof(IsCodeVisible));
        OnPropertyChanged(nameof(IsRenderedVisible));
        OnPropertyChanged(nameof(IsDiffVisible));
    }
    partial void OnIsDirtyChanged(bool value) => UpdateTitle();
    partial void OnOutlineChanged(IReadOnlyList<OutlineRowVM> value) => OnPropertyChanged(nameof(OutlineHeader));
    partial void OnMarkersChanged(IReadOnlyList<MarkerRowVM> value) => OnPropertyChanged(nameof(MarkersHeader));

    private void NotifyKind()
    {
        OnPropertyChanged(nameof(IsText));
        OnPropertyChanged(nameof(IsImage));
        OnPropertyChanged(nameof(IsNote));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanRender));
        NotifyViews();
    }

    private void UpdateTitle() => Title = (IsDirty ? "● " : IsEditing ? "✎ " : "") + FileName;

    // ------------------------------------------------------------ loading

    public async Task LoadAsync(bool keepView)
    {
        IsLoading = Document.TextLength == 0;
        var loaded = await Task.Run(() => FileLoader.Load(FullPath));
        _modified = loaded.Modified;
        _size = loaded.Size;
        Kind = loaded.Kind;
        ChangedOnDisk = false;
        switch (loaded.Kind)
        {
            case FileKind.Text:
                _encoding = loaded.Encoding;
                EncodingName = loaded.EncodingName;
                _loadingText = true;
                try
                {
                    LastChangeLine = 0;
                    if (keepView && Document.TextLength > 0)
                    {
                        // Replace in place so the caret and scroll position survive the reload.
                        var old = Document.Text;
                        if (old != loaded.Text)
                        {
                            LastChangeLine = FirstDifferentLine(old, loaded.Text);
                            Document.Replace(0, Document.TextLength, loaded.Text);
                        }
                    }
                    else Document.Text = loaded.Text;
                    Document.UndoStack.ClearAll();
                }
                finally { _loadingText = false; }
                IsDirty = false;
                if (!_renderDecided)
                {
                    // Documents open "compiled" unless the reader chose code for this one; huge ones stay as code
                    // so the first paint is instant.
                    _renderDecided = true;
                    IsRendered = RenderMode != RenderKind.None && !_preferCode && _size <= 2 * 1024 * 1024;
                }
                ContentVersion++;
                break;
            case FileKind.Image:
                try
                {
                    Image = await Task.Run(() => new Bitmap(FullPath));
                    ImageInfo = $"{Image.PixelSize.Width} × {Image.PixelSize.Height} px · {FileLoader.FormatSize(_size)}";
                }
                catch (Exception ex)
                {
                    Kind = FileKind.Error;
                    NoteText = Loc.F("file.imageError", ex.Message);
                }
                break;
            case FileKind.Binary:
                NoteText = Loc.F("file.binary", FileLoader.FormatSize(_size));
                break;
            case FileKind.TooLarge:
                NoteText = Loc.F("file.tooLarge", FileLoader.FormatSize(_size), FileLoader.FormatSize(FileLoader.MaxTextBytes));
                break;
            case FileKind.Error:
                NoteText = Loc.F("file.readError", loaded.Error);
                break;
        }
        IsLoading = false;
        InfoText = Loc.F("file.info", FileLoader.FormatSize(_size), _modified);
        if (Kind == FileKind.Text)
        {
            _ = UpdateDiffAsync(rereadHead: false);
            await AnalyzeNowAsync();
            Reloaded?.Invoke();
            if (_pendingReveal is { } r) { _pendingReveal = null; RevealRequested?.Invoke(r.Line, r.Col, r.Len); }
        }
        else
        {
            Metrics = new[]
            {
                new MetricRow { Label = Loc.T("metric.type"), Value = Kind switch { FileKind.Image => Loc.T("metric.type.image"), FileKind.Binary => Loc.T("metric.type.binary"), FileKind.TooLarge => Loc.T("metric.type.tooLarge"), _ => Loc.T("metric.type.error") } },
                new MetricRow { Label = Loc.T("metric.size"), Value = FileLoader.FormatSize(_size) },
                new MetricRow { Label = Loc.T("metric.modified"), Value = _modified == default ? "—" : _modified.ToString("yyyy-MM-dd HH:mm") },
            };
        }
    }

    /// <summary>Line where the last reload from disk started to differ (0 = first load or no change).</summary>
    public int LastChangeLine { get; private set; }

    private static int FirstDifferentLine(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length), i = 0, line = 1;
        for (; i < n && a[i] == b[i]; i++)
            if (a[i] == '\n') line++;
        return line;
    }

    /// <summary>Called by the project watcher when this file changed on disk.</summary>
    public async Task OnDiskChangedAsync()
    {
        if (DateTime.UtcNow < _ignoreDiskUntil) return;
        if (!File.Exists(FullPath))
        {
            NoteText = Loc.T("file.gone");
            ChangedOnDisk = true;
            return;
        }
        if (IsEditing) { ChangedOnDisk = true; return; }
        await LoadAsync(keepView: true);
    }

    public void Reveal(int line, int col = 1, int len = 0)
    {
        if (IsLoading) _pendingReveal = (line, col, len);
        else RevealRequested?.Invoke(line, col, len);
    }

    // ------------------------------------------------------------ analysis

    private void ScheduleAnalysis(TimeSpan delay)
    {
        _analysisCts?.Cancel();
        var cts = _analysisCts = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Task.Delay(delay, cts.Token); } catch (OperationCanceledException) { return; }
            await AnalyzeNowAsync();
        });
    }

    private async Task AnalyzeNowAsync()
    {
        var text = Document.Text;
        var lang = Language;
        var a = await Task.Run(() => FileAnalyzer.Analyze(text, lang));
        Analysis = a;
        LineEnding = a.LineEnding;
        Indentation = a.Indent;
        Outline = a.Outline.Select(o => new OutlineRowVM { Item = o }).ToList();
        Markers = a.Markers.Select(m => new MarkerRowVM { Tag = m.Tag, Text = m.Text, Line = m.Line }).ToList();
        Metrics = BuildMetrics(a);
        if (IsEditing) _ = UpdateDiffAsync(rereadHead: false);
    }

    // ------------------------------------------------------------ changes against the last commit

    /// <summary>
    /// Called by the project after every git refresh. The committed text is read again only when HEAD moved or the
    /// file changed state; files git sees as unchanged need no process at all.
    /// </summary>
    public void SetGitBase(string? repoRoot, string? repoPath, GitChange change, string? headId)
    {
        bool reread = !_gitKnown || repoRoot != _repoRoot || repoPath != _repoPath || headId != _headId
                      || (change != _gitChange && HeadText is null);
        _gitKnown = true;
        _repoRoot = repoRoot;
        _repoPath = repoPath;
        _headId = headId;
        var old = _gitChange;
        _gitChange = change;
        if (reread || old != change) _ = UpdateDiffAsync(reread);
    }

    private async Task UpdateDiffAsync(bool rereadHead)
    {
        if (!_gitKnown || Kind != FileKind.Text || IsLoading) return;
        _diffCts?.Cancel();
        var cts = _diffCts = new CancellationTokenSource();
        try
        {
            var change = _gitChange;
            if (change is GitChange.Ignored || _repoRoot is null || _repoPath is null)
            {
                HeadText = null;
                SetDiff(null, Array.Empty<LineMark>());
                return;
            }
            if (change is GitChange.Untracked or GitChange.Added)
            {
                // New to git: every line is new.
                HeadText = null;
                SetDiff(null, LineDiff.AllAdded(Document.LineCount));
                return;
            }
            if (change == GitChange.None && !IsEditing)
            {
                // git says the file matches HEAD: nothing to mark, and the committed text is the file itself.
                HeadText = Document.Text;
                SetDiff(null, Array.Empty<LineMark>());
                return;
            }
            if (rereadHead || HeadText is null)
            {
                var path = _repoPath;
                HeadText = await GitStatus.ReadHeadTextAsync(_repoRoot, path, cts.Token);
                if (cts.IsCancellationRequested) return;
            }
            if (HeadText is null) { SetDiff(null, Array.Empty<LineMark>()); return; }
            var head = HeadText;
            var now = Document.Text;
            int lines = Document.LineCount;
            var (rows, marks) = await Task.Run(() =>
            {
                var r = LineDiff.Compute(head, now);
                return (r, LineDiff.Marks(r, lines));
            }, cts.Token);
            if (cts.IsCancellationRequested) return;
            SetDiff(rows, marks);
        }
        catch (OperationCanceledException) { }
    }

    private void SetDiff(IReadOnlyList<DiffRow>? rows, LineMark[] marks)
    {
        DiffRows = rows;
        Marks = marks;
        int added = rows?.Count(r => r.Kind == DiffKind.Added) ?? marks.Count(m => m != LineMark.None);
        int removed = rows?.Count(r => r.Kind == DiffKind.Removed) ?? 0;
        HasChanges = rows is not null && (added > 0 || removed > 0);
        DiffStats = added + removed > 0 ? $"+{added} −{removed}" : "";
        if (!HasChanges && IsDiff) IsDiff = false;
        MarksChanged?.Invoke();
    }

    /// <summary>The first changed line, so following an agent lands where it just wrote.</summary>
    public int FirstChangedLine()
    {
        for (int i = 0; i < Marks.Length; i++)
            if (Marks[i] != LineMark.None) return i + 1;
        return 0;
    }

    private List<MetricRow> BuildMetrics(FileAnalysis a)
    {
        var warn = Palette.Amber;
        var rows = new List<MetricRow>
        {
            new() { Label = Loc.T("metric.lines"), Value = $"{a.Lines:N0}" },
        };
        if (!Language.IsProse)
        {
            double ratio = a.Code + a.Comment > 0 ? 100.0 * a.Comment / (a.Code + a.Comment) : 0;
            rows.Add(new() { Label = Loc.T("metric.code"), Value = $"{a.Code:N0}" });
            rows.Add(new() { Label = Loc.T("metric.comments"), Value = $"{a.Comment:N0}  ({ratio:0}%)", Hint = Loc.T("metric.comments.hint") });
        }
        rows.Add(new() { Label = Loc.T("metric.blank"), Value = $"{a.Blank:N0}" });
        if (!Language.IsProse && a.Branches > 0)
            rows.Add(new() { Label = Loc.T("metric.branches"), Value = $"{a.Branches:N0}", Hint = Loc.T("metric.branches.hint"), ValueBrush = a.Branches > 150 ? warn : Palette.Text });
        if (a.MaxDepth > 0)
            rows.Add(new() { Label = Loc.T("metric.depth"), Value = a.MaxDepth.ToString(), Hint = Loc.T("metric.depth.hint"), ValueBrush = a.MaxDepth > 6 ? warn : Palette.Text });
        if (a.Outline.Count > 0)
            rows.Add(new() { Label = Loc.T("metric.symbols"), Value = $"{a.Outline.Count:N0}" });
        rows.Add(new() { Label = Loc.T("metric.longest"), Value = Loc.F("metric.longest.value", a.LongestLine, a.LongestLineNo), ValueBrush = a.LongestLine > 160 ? warn : Palette.Text });
        rows.Add(new() { Label = Loc.T("metric.chars"), Value = $"{a.Chars:N0}" });
        rows.Add(new() { Label = Loc.T("metric.size"), Value = FileLoader.FormatSize(_size) });
        rows.Add(new() { Label = Loc.T("metric.indent"), Value = a.Indent, ValueBrush = a.IndentMixed ? warn : Palette.Text });
        rows.Add(new() { Label = Loc.T("metric.eol"), Value = a.LineEnding, ValueBrush = a.LineEndingMixed ? warn : Palette.Text });
        rows.Add(new() { Label = Loc.T("metric.encoding"), Value = EncodingName });
        rows.Add(new() { Label = Loc.T("metric.trailing"), Value = a.TrailingWhitespace ? Loc.T("common.yes") : Loc.T("common.no"), ValueBrush = a.TrailingWhitespace ? warn : Palette.Text });
        rows.Add(new() { Label = Loc.T("metric.modified"), Value = _modified.ToString("yyyy-MM-dd HH:mm") });
        return rows;
    }

    // ------------------------------------------------------------ editing

    [RelayCommand]
    private void Edit()
    {
        if (!CanEdit) return;
        IsRendered = false;
        IsDiff = false;
        IsEditing = true;
    }

    [RelayCommand]
    private void ShowRendered()
    {
        if (!CanRender || IsEditing) return;
        IsDiff = false;
        IsRendered = true;
        ViewModeChosen?.Invoke(true);
    }

    [RelayCommand]
    private void ShowCode()
    {
        IsDiff = false;
        IsRendered = false;
        if (CanRender) ViewModeChosen?.Invoke(false);
    }

    /// <summary>Toggles the diff against the last commit (it can be opened while editing, to compare).</summary>
    [RelayCommand]
    private void ToggleDiff()
    {
        if (!HasChanges && !IsDiff) return;
        IsDiff = !IsDiff;
    }

    [RelayCommand] private Task Save() => SaveAsync();

    public async Task<bool> SaveAsync()
    {
        if (!IsEditing) return true;
        try
        {
            _ignoreDiskUntil = DateTime.UtcNow.AddSeconds(1.5);
            var text = Document.Text;
            await Task.Run(() => File.WriteAllText(FullPath, text, _encoding));
            var info = new FileInfo(FullPath);
            _modified = info.LastWriteTime;
            _size = info.Length;
            IsDirty = false;
            ChangedOnDisk = false;
            InfoText = Loc.F("file.saved", FileLoader.FormatSize(_size), _modified);
            await AnalyzeNowAsync();
            return true;
        }
        catch (Exception ex)
        {
            await Dialogs.Message(Loc.T("file.saveError"), ex.Message);
            return false;
        }
    }

    /// <summary>Leaves edit mode. Unsaved changes are confirmed and then discarded (reloaded from disk).</summary>
    [RelayCommand] private Task FinishEditing() => FinishEditingAsync();

    public async Task<bool> FinishEditingAsync()
    {
        if (!IsEditing) return true;
        if (IsDirty)
        {
            var choice = await Dialogs.Choose(Loc.T("common.unsaved"),
                Loc.F("file.unsaved", FileName), Loc.T("common.save"), Loc.T("common.discard"));
            if (choice == 0) { if (!await SaveAsync()) return false; }
            else if (choice == 1) await LoadAsync(keepView: true);
            else return false;
        }
        else if (ChangedOnDisk) await LoadAsync(keepView: true);
        IsEditing = false;
        IsDirty = false;
        return true;
    }

    [RelayCommand]
    private async Task ReloadFromDiskAsync()
    {
        if (IsDirty && !await Dialogs.Confirm(Loc.T("common.reload"), Loc.T("file.reload.text"), Loc.T("common.reload")))
            return;
        await LoadAsync(keepView: true);
    }

    public void Dispose()
    {
        _analysisCts?.Cancel();
        _diffCts?.Cancel();
        Image?.Dispose();
    }
}
