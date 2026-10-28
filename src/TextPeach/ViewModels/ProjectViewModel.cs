using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TextPeach.Services;
using TextPeach.Views;

namespace TextPeach.ViewModels;

public partial class ProjectViewModel : TabViewModel, IDisposable
{
    private const int MaxFilterHits = 300, MaxSearchMatches = 3000, MaxMatchesPerFile = 200;

    public string RootPath { get; }
    /// <summary>
    /// The file of a simple view: one loose file opened from the system (double click, "Abrir con TextPeach"),
    /// shown without tree, search or summary. Null for a project.
    /// </summary>
    public string? FilePath { get; }
    public bool IsSingleFile => FilePath is not null;
    /// <summary>What identifies the tab between sessions: the folder of a project, the file of a simple view.</summary>
    public string TabKey => FilePath ?? RootPath;
    public ViewerSettings Settings { get; }
    private readonly ProjectStore _store;

    public ObservableCollection<FileNode> Tree { get; } = new();
    public ObservableCollection<FileHit> FilterHits { get; } = new();
    public ObservableCollection<TabViewModel> Docs { get; } = new();
    public SummaryViewModel Summary { get; }
    /// <summary>The project's terminals: below the code, as a page of the file strip or in a window.</summary>
    public ConsoleViewModel Console { get; }

    [ObservableProperty] private TabViewModel? _selectedDoc;
    [ObservableProperty] private FileNode? _selectedNode;
    [ObservableProperty] private FileHit? _selectedHit;
    [ObservableProperty] private string _fileFilter = "";
    [ObservableProperty] private bool _isFiltering;
    [ObservableProperty] private bool _isSearchMode;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _matchCase;
    [ObservableProperty] private bool _wholeWord;
    [ObservableProperty] private bool _useRegex;
    [ObservableProperty] private string _includeGlob = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _searchSummary = "";
    [ObservableProperty] private IReadOnlyList<SearchRowVM> _searchRows = Array.Empty<SearchRowVM>();
    [ObservableProperty] private SearchRowVM? _selectedSearchRow;
    [ObservableProperty] private string _notice = "";
    [ObservableProperty] private string _caretText = "";
    [ObservableProperty] private string _filesText = "";
    [ObservableProperty] private bool _hasGit;
    [ObservableProperty] private string _branchText = "";
    [ObservableProperty] private string _branchTip = "";
    [ObservableProperty] private string _changesText = "";
    /// <summary>Shown instead of the branch when there is no git information, with the reason as tooltip.</summary>
    [ObservableProperty] private string _noGitTip = "";
    [ObservableProperty] private bool _showNoGit;
    /// <summary>Sidebar shows the files that changed on disk ("Recent") instead of the tree or the search.</summary>
    [ObservableProperty] private bool _isRecentMode;
    /// <summary>Opens each file as soon as it changes on disk, at the changed line: watching an agent work.</summary>
    [ObservableProperty] private bool _followChanges;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;

    /// <summary>Latest git state of the project (read-only: TextPeach shows it, GitPeach acts on it).</summary>
    public GitDecorations Git { get; } = new();
    /// <summary>Folders left out of the tree, the file list, the search and "Recent" (defaults + this project's lines).</summary>
    public IgnoreRules Rules { get; private set; }
    public ObservableCollection<RecentChangeVM> Recent { get; } = new();

    public OpenFileViewModel? SelectedFile => SelectedDoc as OpenFileViewModel;
    public bool HasFile => SelectedFile is not null;
    public bool IsSummary => SelectedDoc is SummaryViewModel;
    public bool IsConsole => SelectedDoc is ConsoleViewModel;
    public bool IsFilesMode => !IsSearchMode && !IsRecentMode;
    public bool HasRecent => Recent.Count > 0;
    private readonly ProjectPrefs _prefs;
    private HashSet<string> _knownFiles = new(StringComparer.OrdinalIgnoreCase);
    private string? _headId;
    private DispatcherTimer? _agoTimer;

    public event Action? SearchFocusRequested;
    public event Action? FilterFocusRequested;
    public event Action? EditorFocusRequested;
    /// <summary>A simple view hands the window a file it does not show itself (a link to a file next to it).</summary>
    public event Action<string>? OpenRequested;
    /// <summary>A simple view asks to become its folder's project.</summary>
    public event Action? FolderRequested;

    private List<string> _allFiles = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _treeDebounce, _searchCts, _searchDebounce, _scanCts, _gitDebounce, _followDebounce;
    private readonly Dictionary<string, CancellationTokenSource> _fileDebounce = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed, _gitRead;
    private bool _restoring = true; // until the saved session has been reopened

    public ProjectViewModel(string path, ProjectStore store, ViewerSettings settings, string? file = null)
    {
        RootPath = path;
        FilePath = file;
        _store = store;
        Settings = settings;
        // A simple view leaves no trace among the saved projects.
        _prefs = file is null ? store.PrefsFor(path) : new ProjectPrefs();
        Rules = new IgnoreRules(_prefs.Ignore);
        Title = Path.GetFileName(file ?? path.TrimEnd('\\', '/'));
        ToolTip = file ?? path;
        Summary = new SummaryViewModel(path);
        Recent.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecent));
        Console = new ConsoleViewModel(path, Title, settings, allowsPage: file is null);
        Console.CloseCommand = new RelayCommand(() => Console.IsOpen = false);
        Console.Notice += m => Notice = m;
        Console.ToggleRequested += ToggleConsole;
        Console.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ConsoleViewModel.IsOpen) or nameof(ConsoleViewModel.Place)) SyncConsoleTab();
        };

        if (file is not null)
        {
            _restoring = false;
            OpenFile(Path.GetFileName(file));
            StartWatcher();
            ScheduleGitRefresh(0);
            return;
        }
        Docs.Add(Summary);
        SelectedDoc = Summary;

        foreach (var n in FileNode.ListChildren(path, path, Git, Rules)) Tree.Add(n);
        RestoreSession();
        StartWatcher();
        _ = RescanAsync();
        ScheduleGitRefresh(0);
    }

    partial void OnSelectedDocChanged(TabViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedFile));
        OnPropertyChanged(nameof(HasFile));
        OnPropertyChanged(nameof(IsSummary));
        OnPropertyChanged(nameof(IsConsole));
        if (value is not OpenFileViewModel) CaretText = "";
        SaveSession();
    }

    partial void OnSelectedDocChanged(TabViewModel? oldValue, TabViewModel? newValue)
    {
        // Switching files is a jump Alt+← can undo, like in most editors.
        if (oldValue is not null && newValue is not null) PushBack(LocationOf(oldValue));
        if (newValue is ConsoleViewModel && oldValue is not null and not ConsoleViewModel) _beforeConsole = oldValue;
    }

    partial void OnIsSearchModeChanged(bool value) => OnPropertyChanged(nameof(IsFilesMode));
    partial void OnIsRecentModeChanged(bool value) => OnPropertyChanged(nameof(IsFilesMode));

    // ------------------------------------------------------------ file list / tree

    private async Task RescanAsync()
    {
        if (IsSingleFile) return;
        _scanCts?.Cancel();
        var cts = _scanCts = new CancellationTokenSource();
        try
        {
            var rules = Rules;
            var files = await Task.Run(() => ProjectScanner.ListFiles(RootPath, cts.Token, rules), cts.Token);
            if (cts.IsCancellationRequested) return;
            _allFiles = files;
            _knownFiles = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            FilesText = files.Count >= ProjectScanner.MaxFiles ? Loc.F("pv.filesCountMore", files.Count) : Loc.Plural("common.files", files.Count);
            ApplyFilter();
            await Summary.ComputeAsync(files);
        }
        catch (OperationCanceledException) { }
    }

    private void RebuildTree()
    {
        if (IsSingleFile) return;
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Collect(IEnumerable<FileNode> nodes)
        {
            foreach (var n in nodes)
                if (n.IsDirectory && n.IsExpanded) { expanded.Add(n.RelPath); Collect(n.Children); }
        }
        Collect(Tree);
        var selected = SelectedNode?.RelPath;

        Tree.Clear();
        foreach (var n in FileNode.ListChildren(RootPath, RootPath, Git, Rules)) Tree.Add(n);
        void Expand(IEnumerable<FileNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (selected is not null && string.Equals(n.RelPath, selected, StringComparison.OrdinalIgnoreCase)) SelectedNode = n;
                if (n.IsDirectory && expanded.Contains(n.RelPath)) { n.IsExpanded = true; Expand(n.Children); }
            }
        }
        Expand(Tree);
    }

    /// <summary>Expands the tree down to a file and selects it.</summary>
    public void RevealInTree(string relPath)
    {
        var parts = relPath.Split('/');
        IEnumerable<FileNode> level = Tree;
        FileNode? node = null;
        for (int i = 0; i < parts.Length; i++)
        {
            node = level.FirstOrDefault(n => string.Equals(n.Name, parts[i], StringComparison.OrdinalIgnoreCase));
            if (node is null) return;
            if (i < parts.Length - 1) { node.IsExpanded = true; level = node.Children; }
        }
        _suppressNodeOpen = true;
        SelectedNode = node;
        _suppressNodeOpen = false;
    }

    private bool _suppressNodeOpen;

    partial void OnSelectedNodeChanged(FileNode? value)
    {
        // One click (or arrowing through the tree) previews; a double click keeps the tab (PinNode).
        if (_suppressNodeOpen || value is null || value.IsDirectory || value.IsPlaceholder) return;
        OpenFile(value.RelPath, preview: true);
    }

    /// <summary>Double click on a file of the tree: open it in a tab of its own.</summary>
    public void PinNode(FileNode node)
    {
        if (!node.IsDirectory && !node.IsPlaceholder) OpenFile(node.RelPath);
    }

    /// <summary>Double click on a tab, or any edit: the preview tab becomes a normal one.</summary>
    public void Pin(TabViewModel tab)
    {
        if (!tab.IsPreview) return;
        tab.IsPreview = false;
        SaveSession();
    }

    partial void OnFileFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = FileFilter.Trim();
        IsFiltering = q.Length > 0;
        FilterHits.Clear();
        if (!IsFiltering) return;
        foreach (var rel in Dialogs.RankPaths(_allFiles, q, MaxFilterHits))
            FilterHits.Add(new FileHit { RelPath = rel, Change = Git.For(rel, false) });
        SelectedHit = FilterHits.FirstOrDefault();
    }

    public void OpenSelectedHit()
    {
        if (SelectedHit is { } h) { OpenFile(h.RelPath, preview: true); EditorFocusRequested?.Invoke(); }
    }

    // ------------------------------------------------------------ documents

    /// <summary>
    /// Opens (or selects) a file. With <paramref name="preview"/> it takes the place of the current preview tab
    /// instead of adding one, so exploring does not fill the strip; without it the tab stays.
    /// </summary>
    public OpenFileViewModel? OpenFile(string relPath, int line = 0, int col = 1, int len = 0, bool preview = false)
    {
        var full = Path.Combine(RootPath, relPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
        {
            Notice = Loc.F("pv.fileMissing", relPath);
            return null;
        }
        // A simple view shows its own file only; any other opens in a tab of its own.
        if (IsSingleFile && !string.Equals(full, FilePath, StringComparison.OrdinalIgnoreCase))
        {
            OpenRequested?.Invoke(full);
            return null;
        }
        var doc = Docs.OfType<OpenFileViewModel>().FirstOrDefault(d => string.Equals(d.RelPath, relPath, StringComparison.OrdinalIgnoreCase));
        OpenFileViewModel? replaced = null;
        if (doc is null)
        {
            bool preferCode = _prefs.CodeView.Contains(relPath, StringComparer.OrdinalIgnoreCase);
            doc = new OpenFileViewModel(RootPath, relPath, preferCode) { Change = Git.For(relPath, false), IsPreview = preview };
            var captured = doc;
            doc.CloseCommand = new AsyncRelayCommand(() => CloseDocAsync(captured));
            doc.ViewModeChosen += rendered => RememberViewMode(captured.RelPath, rendered);
            SetGitBase(doc);
            // Open next to the current tab, like most editors; a preview takes the old preview's place.
            replaced = preview ? Docs.OfType<OpenFileViewModel>().FirstOrDefault(d => d.IsPreview && !d.IsDirty && !d.IsEditing) : null;
            int at = replaced is not null ? Docs.IndexOf(replaced) : SelectedDoc is null ? Docs.Count : Docs.IndexOf(SelectedDoc) + 1;
            Docs.Insert(Math.Clamp(at, IsSingleFile ? 0 : 1, Docs.Count), doc);
        }
        else if (!preview) doc.IsPreview = false;
        // A jump inside the file already shown is recorded here; switching files is recorded by the selection.
        if (line > 0 && SelectedDoc == doc) PushBack(LocationOf(doc));
        SelectedDoc = doc;
        if (replaced is not null)
        {
            Docs.Remove(replaced);
            replaced.Dispose();
        }
        if (line > 0) doc.Reveal(line, col, len);
        SaveSession();
        return doc;
    }

    private void RememberViewMode(string relPath, bool rendered)
    {
        _prefs.CodeView.RemoveAll(p => string.Equals(p, relPath, StringComparison.OrdinalIgnoreCase));
        if (!rendered) _prefs.CodeView.Add(relPath);
        _store.Save();
    }

    public async Task<bool> CloseDocAsync(TabViewModel tab)
    {
        if (!tab.Closable) return false;
        // Closing the console's page only hides it: its shells keep running until closed one by one.
        if (tab is ConsoleViewModel c) { c.IsOpen = false; return true; }
        if (tab is OpenFileViewModel f && f.IsEditing && !await f.FinishEditingAsync()) return false;
        int idx = Docs.IndexOf(tab);
        if (idx < 0) return false;
        bool wasSelected = SelectedDoc == tab;
        Docs.Remove(tab);
        (tab as IDisposable)?.Dispose();
        if (wasSelected || SelectedDoc is null)
            SelectedDoc = Docs[Math.Clamp(idx - 1, 0, Docs.Count - 1)];
        SaveSession();
        return true;
    }

    /// <summary>Ctrl+W: closes the current file. Returns false when there was no file to close.</summary>
    public bool CloseCurrentDoc()
    {
        // A simple view is its file: closing the file closes the tab.
        if (IsSingleFile) return false;
        if (SelectedDoc is not { Closable: true } t) return false;
        _ = CloseDocAsync(t);
        return true;
    }

    [RelayCommand]
    private async Task CloseOthersAsync()
    {
        foreach (var d in Docs.Where(d => d.Closable && d != SelectedDoc).ToList())
            if (!await CloseDocAsync(d)) return;
    }

    [RelayCommand]
    private async Task CloseAllAsync()
    {
        foreach (var d in Docs.Where(d => d.Closable).ToList())
            if (!await CloseDocAsync(d)) return;
    }

    [RelayCommand]
    private void NextDoc()
    {
        if (Docs.Count < 2 || SelectedDoc is null) return;
        SelectedDoc = Docs[(Docs.IndexOf(SelectedDoc) + 1) % Docs.Count];
    }

    [RelayCommand]
    private void PreviousDoc()
    {
        if (Docs.Count < 2 || SelectedDoc is null) return;
        SelectedDoc = Docs[(Docs.IndexOf(SelectedDoc) - 1 + Docs.Count) % Docs.Count];
    }

    [RelayCommand]
    private void ShowSummary()
    {
        if (!IsSingleFile) SelectedDoc = Summary;
    }

    /// <summary>Simple view: its folder as a whole project, with the file already open.</summary>
    [RelayCommand]
    private void OpenFolder() => FolderRequested?.Invoke();

    private void RestoreSession()
    {
        _restoring = true;
        if (!_store.State.Sessions.TryGetValue(RootPath, out var s)) { _restoring = false; return; }
        try
        {
            foreach (var rel in s.Files) OpenFile(rel, preview: string.Equals(rel, s.Preview, StringComparison.OrdinalIgnoreCase));
            SelectedDoc = Docs.OfType<OpenFileViewModel>().FirstOrDefault(d => string.Equals(d.RelPath, s.Active, StringComparison.OrdinalIgnoreCase))
                          ?? (TabViewModel)Summary;
        }
        finally
        {
            _restoring = false;
            _back.Clear();
            UpdateNav();
        }
    }

    private void SaveSession()
    {
        if (_restoring || _disposed || IsSingleFile) return;
        _store.State.Sessions[RootPath] = new ProjectSession
        {
            Files = Docs.OfType<OpenFileViewModel>().Select(d => d.RelPath).ToList(),
            Active = SelectedFile?.RelPath,
            Preview = Docs.OfType<OpenFileViewModel>().FirstOrDefault(d => d.IsPreview)?.RelPath,
        };
        _store.Save();
    }

    // ------------------------------------------------------------ back / forward (Alt+← / Alt+→)

    /// <summary>A place to come back to: a file and line, or the summary (empty path).</summary>
    private readonly record struct NavLocation(string RelPath, int Line);

    private const int MaxHistory = 100;
    private readonly List<NavLocation> _back = new(), _forward = new();
    private bool _navigating;

    private static NavLocation LocationOf(TabViewModel tab) =>
        tab is OpenFileViewModel f ? new(f.RelPath, f.CaretLine) : new("", 0);

    private void PushBack(NavLocation loc)
    {
        if (_navigating || _restoring) return;
        if (_back.Count > 0 && _back[^1] == loc) return;
        _back.Add(loc);
        if (_back.Count > MaxHistory) _back.RemoveAt(0);
        _forward.Clear();
        UpdateNav();
    }

    private void UpdateNav()
    {
        CanGoBack = _back.Count > 0;
        CanGoForward = _forward.Count > 0;
    }

    /// <summary>A jump inside the current file (go to line, structure, markers) that Alt+← can undo.</summary>
    public void RevealInCurrent(int line)
    {
        if (SelectedFile is not { } f) return;
        PushBack(LocationOf(f));
        f.Reveal(line);
    }

    [RelayCommand]
    private void GoBack() => Travel(_back, _forward);

    [RelayCommand]
    private void GoForward() => Travel(_forward, _back);

    private void Travel(List<NavLocation> from, List<NavLocation> to)
    {
        var here = SelectedDoc is null ? new NavLocation("", 0) : LocationOf(SelectedDoc);
        while (from.Count > 0 && from[^1] == here) from.RemoveAt(from.Count - 1);
        if (from.Count == 0) { UpdateNav(); return; }
        var target = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(here);
        _navigating = true;
        try
        {
            if (target.RelPath.Length == 0) SelectedDoc = Summary;
            else if (OpenFile(target.RelPath, preview: true) is { } doc && target.Line > 0) doc.Reveal(target.Line);
        }
        finally { _navigating = false; }
        UpdateNav();
        EditorFocusRequested?.Invoke();
    }

    public bool HasUnsavedChanges => Docs.OfType<OpenFileViewModel>().Any(d => d.IsDirty);

    // ------------------------------------------------------------ quick open / go to line

    [RelayCommand]
    public async Task QuickOpenFileAsync()
    {
        var picked = await Dialogs.PickPath(Loc.T("pv.goToFile"), _allFiles, Loc.T("pv.goToFile.watermark"));
        if (picked is not null) { OpenFile(picked); EditorFocusRequested?.Invoke(); }
    }

    [RelayCommand]
    private async Task GoToLineAsync()
    {
        if (SelectedFile is not { IsText: true } f) return;
        var total = f.Document.LineCount;
        var input = await Dialogs.Prompt(Loc.T("pv.goToLine"), Loc.F("pv.goToLine.label", total));
        if (string.IsNullOrWhiteSpace(input)) return;
        var parts = input.Split(':', ',');
        if (!int.TryParse(parts[0].Trim(), out var line)) return;
        int col = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var c) ? c : 1;
        PushBack(LocationOf(f));
        f.Reveal(Math.Clamp(line, 1, total), Math.Max(1, col));
        EditorFocusRequested?.Invoke();
    }

    [RelayCommand]
    private void FocusFilter()
    {
        if (IsSingleFile) return;
        IsSearchMode = false;
        IsRecentMode = false;
        FilterFocusRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowRecent()
    {
        if (IsSingleFile) return;
        IsSearchMode = false;
        IsRecentMode = true;
        UpdateAgo();
    }

    // ------------------------------------------------------------ project-wide search

    [RelayCommand]
    private void FocusSearch()
    {
        if (IsSingleFile) return;
        IsRecentMode = false;
        IsSearchMode = true;
        SearchFocusRequested?.Invoke();
    }

    partial void OnSearchTextChanged(string value) => ScheduleSearch();
    partial void OnMatchCaseChanged(bool value) => ScheduleSearch();
    partial void OnWholeWordChanged(bool value) => ScheduleSearch();
    partial void OnUseRegexChanged(bool value) => ScheduleSearch();
    partial void OnIncludeGlobChanged(string value) => ScheduleSearch();

    private void ScheduleSearch()
    {
        _searchDebounce?.Cancel();
        var cts = _searchDebounce = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Task.Delay(350, cts.Token); } catch (OperationCanceledException) { return; }
            await RunSearchAsync();
        });
    }

    [RelayCommand]
    private Task Search() => RunSearchAsync();

    private async Task RunSearchAsync()
    {
        _searchCts?.Cancel();
        var query = SearchText;
        if (string.IsNullOrEmpty(query) || (!UseRegex && query.Trim().Length == 0))
        {
            SearchRows = Array.Empty<SearchRowVM>();
            SearchSummary = "";
            IsSearching = false;
            return;
        }
        Regex rx;
        try
        {
            var pattern = UseRegex ? query : Regex.Escape(query);
            if (WholeWord) pattern = $@"\b(?:{pattern})\b";
            rx = new Regex(pattern, (MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException ex)
        {
            SearchRows = new[] { new SearchRowVM { Kind = SearchRowKind.Note, Text = Loc.F("pv.badRegex", ex.Message) } };
            SearchSummary = "";
            return;
        }

        var cts = _searchCts = new CancellationTokenSource();
        var files = FilterByGlob(_allFiles, IncludeGlob);
        var root = RootPath;
        IsSearching = true;
        SearchSummary = Loc.T("pv.searching");
        var sw = Stopwatch.StartNew();
        try
        {
            var (rows, matches, fileCount, truncated) = await Task.Run(() => SearchFiles(root, files, rx, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            SearchRows = rows.Count > 0 ? rows : new[] { new SearchRowVM { Kind = SearchRowKind.Note, Text = Loc.T("pv.noResults") } };
            SearchSummary = matches == 0 ? Loc.F("pv.noResultsIn", files.Count)
                : Loc.F("pv.results", matches, fileCount, truncated ? Loc.T("pv.results.limited") : "", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_searchCts == cts) IsSearching = false;
        }
    }

    private static (List<SearchRowVM> Rows, int Matches, int Files, bool Truncated) SearchFiles(string root, List<string> files, Regex rx, CancellationToken ct)
    {
        var perFile = new System.Collections.Concurrent.ConcurrentDictionary<string, List<SearchRowVM>>();
        int total = 0;
        bool truncated = false;
        Parallel.ForEach(files, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1) }, (rel, state) =>
        {
            if (Volatile.Read(ref total) >= MaxSearchMatches) { truncated = true; state.Stop(); return; }
            var text = FileLoader.TryReadText(Path.Combine(root, rel));
            if (text is null) return;
            List<SearchRowVM>? hits = null;
            int lineNo = 1, lineStart = 0, scanned = 0;
            try
            {
                foreach (Match m in rx.Matches(text))
                {
                    if (m.Length == 0) continue;
                    for (; scanned < m.Index; scanned++)
                        if (text[scanned] == '\n') { lineNo++; lineStart = scanned + 1; }
                    int lineEnd = text.IndexOf('\n', m.Index);
                    if (lineEnd < 0) lineEnd = text.Length;
                    var line = text[lineStart..lineEnd].TrimEnd('\r');
                    int col = m.Index - lineStart;
                    int len = Math.Min(m.Length, Math.Max(0, line.Length - col));
                    // Keep the snippet short and the match visible.
                    int from = Math.Max(0, col - 40);
                    var before = line[from..col].TrimStart();
                    if (from > 0) before = "…" + before;
                    var after = line[(col + len)..];
                    if (after.Length > 120) after = after[..120] + "…";
                    (hits ??= new()).Add(new SearchRowVM
                    {
                        Kind = SearchRowKind.Match, RelPath = rel, Line = lineNo, Column = col + 1, Length = m.Length,
                        Before = before, Match = line.Substring(col, len), After = after,
                    });
                    if (hits.Count >= MaxMatchesPerFile) break;
                }
            }
            catch (RegexMatchTimeoutException) { }
            if (hits is null) return;
            perFile[rel] = hits;
            Interlocked.Add(ref total, hits.Count);
        });

        var rows = new List<SearchRowVM>();
        int count = 0;
        foreach (var (rel, hits) in perFile.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new SearchRowVM { Kind = SearchRowKind.FileHeader, RelPath = rel, Text = rel, Detail = hits.Count.ToString() });
            rows.AddRange(hits);
            count += hits.Count;
        }
        return (rows, count, perFile.Count, truncated);
    }

    private static List<string> FilterByGlob(List<string> files, string globs)
    {
        var parts = globs.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return files;
        var rxs = parts.Select(g => new Regex("^" + Regex.Escape(g.Contains('/') ? g : "**/" + g)
                .Replace(@"\*\*/", "(?:.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$",
            RegexOptions.IgnoreCase)).ToList();
        return files.Where(f => rxs.Any(r => r.IsMatch(f))).ToList();
    }

    partial void OnSelectedSearchRowChanged(SearchRowVM? value)
    {
        if (value is null || value.IsNote) return;
        if (value.IsMatch) OpenFile(value.RelPath, value.Line, value.Column, value.Length, preview: true);
        else OpenFile(value.RelPath, preview: true);
    }

    public void OpenMarker(MarkerRowVM m) => OpenFile(m.RelPath, m.Line, preview: true);

    public void OpenBigFile(BigFileVM f) => OpenFile(f.RelPath, preview: true);

    // ------------------------------------------------------------ toolbar actions

    [RelayCommand]
    private async Task RefreshAsync()
    {
        RebuildTree();
        foreach (var d in Docs.OfType<OpenFileViewModel>().Where(d => !d.IsEditing))
            await d.LoadAsync(keepView: true);
        await RescanAsync();
        ScheduleGitRefresh(0);
        Notice = Loc.T("project.refreshed");
    }

    /// <summary>The only hand-off to the outside: whatever app the system associates with the file.</summary>
    [RelayCommand]
    private void OpenWithDefault()
    {
        if (SelectedFile is not { } f) return;
        OpenExternal(f.FullPath);
    }

    // ------------------------------------------------------------ console

    private TabViewModel? _beforeConsole;

    /// <summary>
    /// "Consola" in the toolbar (Ctrl+J): opens the console where it lives, with a terminal if it has none. A second
    /// press hides the panel, or leaves the page for the tab that was shown before it.
    /// </summary>
    [RelayCommand]
    private void ToggleConsole()
    {
        var c = Console;
        if (c is { IsOpen: true, IsPanel: true }) { c.IsOpen = false; return; }
        if (c is { IsOpen: true, IsPage: true } && IsConsole)
        {
            SelectedDoc = _beforeConsole is not null && Docs.Contains(_beforeConsole) ? _beforeConsole : Docs.FirstOrDefault(d => d != c);
            EditorFocusRequested?.Invoke();
            return;
        }
        c.Show();
        if (c.IsPage && Docs.Contains(c)) SelectedDoc = c;
    }

    /// <summary>Tree menu: a new terminal already in that folder.</summary>
    public void OpenConsoleAt(string directory)
    {
        if (Console.Add(directory) is null) return;
        if (Console.IsPage && Docs.Contains(Console)) SelectedDoc = Console;
    }

    /// <summary>The console's page is a tab of the file strip (right after the summary) only while it is open there.</summary>
    private void SyncConsoleTab()
    {
        var c = Console;
        bool want = c is { IsOpen: true, IsPage: true };
        bool has = Docs.Contains(c);
        if (want && !has)
        {
            Docs.Insert(IsSingleFile ? 0 : 1, c);
            SelectedDoc = c;
        }
        else if (!want && has)
        {
            bool wasSelected = SelectedDoc == c;
            int idx = Docs.IndexOf(c);
            Docs.Remove(c);
            if (wasSelected)
                SelectedDoc = _beforeConsole is not null && Docs.Contains(_beforeConsole) ? _beforeConsole : Docs[Math.Clamp(idx - 1, 0, Docs.Count - 1)];
        }
        // Hiding the console must not leave the keyboard in a shell nobody sees.
        if (!c.IsOpen) EditorFocusRequested?.Invoke();
    }

    /// <summary>Links in rendered documents: project files open here, anything else goes to the system.</summary>
    public void OpenLink(string baseRelDir, string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            if (uri.Scheme is "http" or "https" or "mailto") OpenExternal(target);
            return;
        }
        var path = Uri.UnescapeDataString(target.Split('#')[0]);
        if (path.Length == 0) return;
        var full = Path.GetFullPath(Path.Combine(RootPath, baseRelDir.Replace('/', Path.DirectorySeparatorChar), path.Replace('/', Path.DirectorySeparatorChar)));
        var root = RootPath.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        if (File.Exists(full) && full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            OpenFile(Path.GetRelativePath(RootPath, full).Replace('\\', '/'));
        else if (File.Exists(full) || Directory.Exists(full))
            OpenExternal(full);
        else
            Notice = Loc.F("pv.notFound", path);
    }

    private void OpenExternal(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Notice = ex.Message; }
    }

    [RelayCommand]
    private async Task CopyPathAsync(string? which)
    {
        if (SelectedFile is not { } f || Dialogs.Owner?.Clipboard is not { } cb) return;
        var text = which == "rel" ? f.RelPath : f.FullPath;
        await cb.SetTextAsync(text);
        Notice = Loc.F("pv.copied", text);
    }

    [RelayCommand]
    private void RevealCurrent()
    {
        if (SelectedFile is { } f && !IsSingleFile) { IsSearchMode = false; IsRecentMode = false; FileFilter = ""; RevealInTree(f.RelPath); }
    }

    [RelayCommand] private void ZoomIn() => Settings.Zoom(+1);
    [RelayCommand] private void ZoomOut() => Settings.Zoom(-1);
    [RelayCommand] private void ZoomReset() => Settings.Zoom(0);
    [RelayCommand] private void ToggleWrap() => Settings.WordWrap = !Settings.WordWrap;
    [RelayCommand] private void ToggleWhitespace() => Settings.ShowWhitespace = !Settings.ShowWhitespace;

    [RelayCommand]
    private void ToggleEdit()
    {
        if (SelectedFile is not { } f) return;
        if (f.IsEditing) _ = f.FinishEditingAsync();
        else f.EditCommand.Execute(null);
    }

    /// <summary>Alt+D: the current file against the last commit, and back.</summary>
    [RelayCommand]
    private void ToggleDiff()
    {
        if (SelectedFile is { } f) f.ToggleDiffCommand.Execute(null);
    }

    [RelayCommand]
    private async Task SaveCurrentAsync()
    {
        if (SelectedFile is { IsEditing: true } f) await f.SaveAsync();
    }

    // ------------------------------------------------------------ watcher

    private void StartWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(RootPath)
            {
                // A simple view watches its file only; a project, everything under it.
                IncludeSubdirectories = !IsSingleFile,
                Filter = IsSingleFile ? Path.GetFileName(FilePath)! : "*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Changed += (_, e) => OnFsEvent(e.FullPath, structural: false, DiskChange.Changed);
            _watcher.Created += (_, e) => OnFsEvent(e.FullPath, structural: true, DiskChange.Created);
            _watcher.Deleted += (_, e) => OnFsEvent(e.FullPath, structural: true, DiskChange.Deleted);
            // Editors and agents often save by writing a temp file and renaming it over the original.
            _watcher.Renamed += (_, e) => { OnFsEvent(e.OldFullPath, true, DiskChange.Deleted); OnFsEvent(e.FullPath, true, DiskChange.Changed); };
            _watcher.Error += (_, _) => ScheduleTreeRefresh();
            _watcher.EnableRaisingEvents = true;
        }
        catch { /* no watching; manual refresh still works */ }
    }

    private void OnFsEvent(string fullPath, bool structural, DiskChange kind)
    {
        if (_disposed || fullPath.Length <= RootPath.Length) return;
        var rel = fullPath[(RootPath.TrimEnd('\\', '/').Length + 1)..].Replace('\\', '/');
        // Inside .git only the state matters (commits, checkouts, staging done elsewhere); lock files come and go.
        if (rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) || rel.Equals(".git", StringComparison.OrdinalIgnoreCase))
        {
            if (!rel.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) Dispatcher.UIThread.Post(() => ScheduleGitRefresh());
            return;
        }
        if (Rules.IsInsideIgnored(rel)) return;
        bool isFile = File.Exists(fullPath);
        if (structural) ScheduleTreeRefresh();
        Dispatcher.UIThread.Post(() =>
        {
            ScheduleFileReload(rel);
            ScheduleGitRefresh();
            if (isFile || (kind == DiskChange.Deleted && _knownFiles.Contains(rel))) RecordChange(rel, isFile ? kind : DiskChange.Deleted);
        });
    }

    // ------------------------------------------------------------ recent changes / follow mode

    private const int MaxRecent = 60;

    /// <summary>Puts a changed file at the top of "Recent" and, in follow mode, shows it.</summary>
    private void RecordChange(string rel, DiskChange kind)
    {
        if (_disposed || Git.For(rel, false) == GitChange.Ignored) return;
        var now = DateTime.Now;
        var row = Recent.FirstOrDefault(r => string.Equals(r.RelPath, rel, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            if (kind == DiskChange.Changed && !_knownFiles.Contains(rel)) kind = DiskChange.Created;
            row = new RecentChangeVM { RelPath = rel, Kind = kind, When = now };
            Recent.Insert(0, row);
            if (Recent.Count > MaxRecent) Recent.RemoveAt(Recent.Count - 1);
        }
        else
        {
            // A file created and then written is still "created"; one that comes back after a delete, "changed".
            if (!(row.Kind == DiskChange.Created && kind == DiskChange.Changed)) row.Kind = kind;
            row.When = now;
            row.Count++;
            int i = Recent.IndexOf(row);
            if (i > 0) Recent.Move(i, 0);
        }
        if (kind != DiskChange.Deleted) _knownFiles.Add(rel);
        row.UpdateAgo(now);
        EnsureAgoTimer();
        if (FollowChanges && kind != DiskChange.Deleted) ScheduleFollow(rel);
    }

    private void EnsureAgoTimer()
    {
        if (_agoTimer is not null) return;
        _agoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _agoTimer.Tick += (_, _) => UpdateAgo();
        _agoTimer.Start();
    }

    private void UpdateAgo()
    {
        var now = DateTime.Now;
        foreach (var r in Recent) r.UpdateAgo(now);
    }

    public void OpenRecent(RecentChangeVM r)
    {
        if (r.IsDeleted) { Notice = Loc.F("pv.fileMissing", r.RelPath); return; }
        var doc = OpenFile(r.RelPath, preview: true);
        if (doc is not null) RevealChangeWhenReady(doc, fresh: doc.Document.TextLength == 0);
    }

    [RelayCommand]
    private void ClearRecent() => Recent.Clear();

    partial void OnFollowChangesChanged(bool value)
    {
        Notice = Loc.T(value ? "recent.follow.on" : "recent.follow.off");
        if (value && Recent.FirstOrDefault(r => !r.IsDeleted) is { } latest) ScheduleFollow(latest.RelPath);
    }

    /// <summary>Follow mode: a short pause lets a burst of writes settle on the last file touched.</summary>
    private void ScheduleFollow(string rel)
    {
        _followDebounce?.Cancel();
        var cts = _followDebounce = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Task.Delay(150, cts.Token); } catch (OperationCanceledException) { return; }
            if (_disposed || !FollowChanges) return;
            // Never pull the reader away from a file being edited.
            if (SelectedFile is { IsEditing: true }) return;
            var existing = Docs.OfType<OpenFileViewModel>().FirstOrDefault(d => string.Equals(d.RelPath, rel, StringComparison.OrdinalIgnoreCase));
            _navigating = true;
            OpenFileViewModel? doc;
            try { doc = OpenFile(rel, preview: existing is null || existing.IsPreview); }
            finally { _navigating = false; }
            if (doc is not null) RevealChangeWhenReady(doc, fresh: existing is null);
        });
    }

    /// <summary>
    /// Scrolls to where the file changed: the first line that differs from the previous version on screen when it was
    /// already open, or from the last commit when it was just opened.
    /// </summary>
    private void RevealChangeWhenReady(OpenFileViewModel doc, bool fresh)
    {
        if (fresh)
        {
            var until = DateTime.UtcNow.AddSeconds(4);
            void OnMarks()
            {
                if (DateTime.UtcNow > until || SelectedDoc != doc) { doc.MarksChanged -= OnMarks; return; }
                if (doc.FirstChangedLine() is > 0 and var line) { doc.MarksChanged -= OnMarks; doc.Reveal(line); }
            }
            doc.MarksChanged += OnMarks;
            OnMarks();
        }
        else
        {
            void OnReloaded()
            {
                doc.Reloaded -= OnReloaded;
                if (SelectedDoc == doc && doc.LastChangeLine > 0) doc.Reveal(doc.LastChangeLine);
            }
            doc.Reloaded += OnReloaded;
        }
    }

    // ------------------------------------------------------------ ignored folders

    /// <summary>Tree context menu: leave this folder out of the project from now on.</summary>
    [RelayCommand]
    private void IgnoreFolder(FileNode? node)
    {
        if (node is not { IsDirectory: true, IsPlaceholder: false }) return;
        ApplyRules(_prefs.Ignore.Append(node.RelPath));
        Notice = Loc.F("ignore.added", node.RelPath);
    }

    [RelayCommand]
    private async Task EditIgnoredAsync()
    {
        var lines = await Dialogs.EditLines(Loc.T("ignore.title"), Loc.T("ignore.text"),
            Loc.F("ignore.defaults", string.Join(", ", ProjectScanner.SkipDirs.Order(StringComparer.OrdinalIgnoreCase))), Rules.Lines);
        if (lines is null) return;
        ApplyRules(lines);
        Notice = Loc.T("ignore.saved");
    }

    private void ApplyRules(IEnumerable<string> lines)
    {
        Rules = new IgnoreRules(lines);
        _prefs.Ignore = Rules.Lines.ToList();
        _store.Save();
        RebuildTree();
        _ = RescanAsync();
    }

    private void ScheduleFileReload(string rel)
    {
        var doc = Docs.OfType<OpenFileViewModel>().FirstOrDefault(d => string.Equals(d.RelPath, rel, StringComparison.OrdinalIgnoreCase));
        if (doc is null) return;
        if (_fileDebounce.TryGetValue(rel, out var old)) old.Cancel();
        var cts = _fileDebounce[rel] = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Task.Delay(300, cts.Token); } catch (OperationCanceledException) { return; }
            await doc.OnDiskChangedAsync();
        });
    }

    private void ScheduleTreeRefresh()
    {
        if (IsSingleFile) return;
        _treeDebounce?.Cancel();
        var cts = _treeDebounce = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Task.Delay(700, cts.Token); } catch (OperationCanceledException) { return; }
            if (_disposed) return;
            RebuildTree();
            await RescanAsync();
        });
    }

    // ------------------------------------------------------------ git (read-only)

    /// <summary>Re-reads branch and file states after a short pause, so a burst of changes costs one <c>git status</c>.</summary>
    public void ScheduleGitRefresh(int delayMs = 600)
    {
        if (_disposed) return;
        _gitDebounce?.Cancel();
        var cts = _gitDebounce = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { if (delayMs > 0) await Task.Delay(delayMs, cts.Token); } catch (OperationCanceledException) { return; }
            var snap = await GitStatus.ReadAsync(RootPath, cts.Token);
            if (cts.IsCancellationRequested || _disposed) return;
            _gitRead = true;
            ApplyGit(snap);
        });
    }

    private void ApplyGit(GitSnapshot? snap)
    {
        Git.Snapshot = snap;
        HasGit = snap is not null;
        _headId = snap is null ? null : GitStatus.ReadHeadId(snap.RepoRoot);
        foreach (var n in Tree) n.RefreshGit();
        foreach (var d in Docs.OfType<OpenFileViewModel>())
        {
            d.Change = Git.For(d.RelPath, false);
            SetGitBase(d);
        }
        if (snap is null)
        {
            BranchText = BranchTip = ChangesText = "";
            // Any folder can be a project; without git only the git extras are missing, and the summary says why.
            NoGitTip = Loc.T(GitStatus.FindRepoRoot(RootPath) is null ? "git.none.notRepo"
                : !GitStatus.IsAvailable ? "git.none.noGit" : "git.none.failed");
            ShowNoGit = true;
            Summary.GitWarning = NoGitTip;
            return;
        }
        ShowNoGit = false;
        NoGitTip = "";
        Summary.GitWarning = "";

        var name = snap.Detached ? GitStatus.ReadDetachedCommit(snap.RepoRoot) ?? "HEAD" : snap.Branch;
        var sync = (snap.Ahead > 0 ? $" ↑{snap.Ahead}" : "") + (snap.Behind > 0 ? $" ↓{snap.Behind}" : "");
        BranchText = name + sync;
        int changes = snap.ChangedCount;
        ChangesText = changes > 0 ? Loc.Plural("git.changes", changes) : "";
        var lines = new List<string> { Loc.F(snap.Detached ? "git.tip.detached" : "git.tip.branch", name) };
        if (snap.Ahead > 0) lines.Add(Loc.Plural("git.tip.ahead", snap.Ahead));
        if (snap.Behind > 0) lines.Add(Loc.Plural("git.tip.behind", snap.Behind));
        lines.Add(changes > 0 ? Loc.Plural("git.tip.changes", changes) : Loc.T("git.tip.clean"));
        lines.Add(Loc.T("git.tip.open"));
        BranchTip = string.Join(Environment.NewLine, lines);
    }

    /// <summary>Tells a file what to compare with: its path in HEAD (the old one if renamed) and HEAD's commit.</summary>
    private void SetGitBase(OpenFileViewModel d)
    {
        // Before the first git read nothing is known yet; the refresh calls this again.
        if (Git.Snapshot is not { } snap)
        {
            if (_gitRead) d.SetGitBase(null, null, GitChange.None, null);
            return;
        }
        var repoPath = snap.RenamedFrom.TryGetValue(d.RelPath, out var from) ? from : GitStatus.RepoPath(snap.RepoRoot, RootPath, d.RelPath);
        d.SetGitBase(snap.RepoRoot, repoPath, Git.For(d.RelPath, false), _headId);
    }

    /// <summary>The branch in the status bar: everything git happens in GitPeach; TextPeach only shows the state.</summary>
    [RelayCommand]
    private async Task OpenInGitPeachAsync()
    {
        var repo = Git.Snapshot?.RepoRoot ?? GitStatus.FindRepoRoot(RootPath) ?? RootPath;
        if (GitPeachLocator.Open(repo)) return;
        if (await Dialogs.Confirm(Loc.T("gitpeach.missing.title"), Loc.T("gitpeach.missing.text"),
                Loc.T("gitpeach.missing.go"), Loc.T("gitpeach.missing.later")))
            OpenExternal(GitPeachLocator.RepoUrl);
    }

    public void Dispose()
    {
        _disposed = true;
        _agoTimer?.Stop();
        _followDebounce?.Cancel();
        _gitDebounce?.Cancel();
        _treeDebounce?.Cancel();
        _searchCts?.Cancel();
        _searchDebounce?.Cancel();
        _scanCts?.Cancel();
        Summary.Cancel();
        Console.Dispose();
        _watcher?.Dispose();
        foreach (var d in Docs.OfType<IDisposable>().Where(d => d != Console)) d.Dispose();
    }
}
