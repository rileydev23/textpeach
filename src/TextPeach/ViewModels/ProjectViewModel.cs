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
    public ViewerSettings Settings { get; }
    private readonly ProjectStore _store;

    public ObservableCollection<FileNode> Tree { get; } = new();
    public ObservableCollection<FileHit> FilterHits { get; } = new();
    public ObservableCollection<TabViewModel> Docs { get; } = new();
    public SummaryViewModel Summary { get; }

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

    /// <summary>Latest git state of the project (read-only: TextPeach shows it, GitPeach acts on it).</summary>
    public GitDecorations Git { get; } = new();

    public OpenFileViewModel? SelectedFile => SelectedDoc as OpenFileViewModel;
    public bool HasFile => SelectedFile is not null;
    public bool IsSummary => SelectedDoc is SummaryViewModel;

    public event Action? SearchFocusRequested;
    public event Action? FilterFocusRequested;
    public event Action? EditorFocusRequested;

    private List<string> _allFiles = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _treeDebounce, _searchCts, _searchDebounce, _scanCts, _gitDebounce;
    private readonly Dictionary<string, CancellationTokenSource> _fileDebounce = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    private bool _restoring = true; // until the saved session has been reopened

    public ProjectViewModel(string path, ProjectStore store, ViewerSettings settings)
    {
        RootPath = path;
        _store = store;
        Settings = settings;
        Title = Path.GetFileName(path.TrimEnd('\\', '/'));
        ToolTip = path;
        Summary = new SummaryViewModel(path);
        Docs.Add(Summary);
        SelectedDoc = Summary;

        foreach (var n in FileNode.ListChildren(path, path, Git)) Tree.Add(n);
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
        if (value is not OpenFileViewModel) CaretText = "";
        SaveSession();
    }

    // ------------------------------------------------------------ file list / tree

    private async Task RescanAsync()
    {
        _scanCts?.Cancel();
        var cts = _scanCts = new CancellationTokenSource();
        try
        {
            var files = await Task.Run(() => ProjectScanner.ListFiles(RootPath, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            _allFiles = files;
            FilesText = files.Count >= ProjectScanner.MaxFiles ? Loc.F("pv.filesCountMore", files.Count) : Loc.Plural("common.files", files.Count);
            ApplyFilter();
            await Summary.ComputeAsync(files);
        }
        catch (OperationCanceledException) { }
    }

    private void RebuildTree()
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Collect(IEnumerable<FileNode> nodes)
        {
            foreach (var n in nodes)
                if (n.IsDirectory && n.IsExpanded) { expanded.Add(n.RelPath); Collect(n.Children); }
        }
        Collect(Tree);
        var selected = SelectedNode?.RelPath;

        Tree.Clear();
        foreach (var n in FileNode.ListChildren(RootPath, RootPath, Git)) Tree.Add(n);
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
        if (_suppressNodeOpen || value is null || value.IsDirectory || value.IsPlaceholder) return;
        OpenFile(value.RelPath);
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
        if (SelectedHit is { } h) { OpenFile(h.RelPath); EditorFocusRequested?.Invoke(); }
    }

    // ------------------------------------------------------------ documents

    public OpenFileViewModel? OpenFile(string relPath, int line = 0, int col = 1, int len = 0)
    {
        var full = Path.Combine(RootPath, relPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
        {
            Notice = Loc.F("pv.fileMissing", relPath);
            return null;
        }
        var doc = Docs.OfType<OpenFileViewModel>().FirstOrDefault(d => string.Equals(d.RelPath, relPath, StringComparison.OrdinalIgnoreCase));
        if (doc is null)
        {
            doc = new OpenFileViewModel(RootPath, relPath) { Change = Git.For(relPath, false) };
            var captured = doc;
            doc.CloseCommand = new AsyncRelayCommand(() => CloseDocAsync(captured));
            // Open next to the current tab, like most editors.
            int at = SelectedDoc is null ? Docs.Count : Docs.IndexOf(SelectedDoc) + 1;
            Docs.Insert(Math.Clamp(at, 1, Docs.Count), doc);
        }
        SelectedDoc = doc;
        if (line > 0) doc.Reveal(line, col, len);
        return doc;
    }

    public async Task<bool> CloseDocAsync(TabViewModel tab)
    {
        if (!tab.Closable) return false;
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
    private void ShowSummary() => SelectedDoc = Summary;

    private void RestoreSession()
    {
        _restoring = true;
        if (!_store.State.Sessions.TryGetValue(RootPath, out var s)) { _restoring = false; return; }
        try
        {
            foreach (var rel in s.Files) OpenFile(rel);
            SelectedDoc = Docs.OfType<OpenFileViewModel>().FirstOrDefault(d => string.Equals(d.RelPath, s.Active, StringComparison.OrdinalIgnoreCase))
                          ?? (TabViewModel)Summary;
        }
        finally { _restoring = false; }
    }

    private void SaveSession()
    {
        if (_restoring || _disposed) return;
        _store.State.Sessions[RootPath] = new ProjectSession
        {
            Files = Docs.OfType<OpenFileViewModel>().Select(d => d.RelPath).ToList(),
            Active = SelectedFile?.RelPath,
        };
        _store.Save();
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
        f.Reveal(Math.Clamp(line, 1, total), Math.Max(1, col));
        EditorFocusRequested?.Invoke();
    }

    [RelayCommand]
    private void FocusFilter()
    {
        IsSearchMode = false;
        FilterFocusRequested?.Invoke();
    }

    // ------------------------------------------------------------ project-wide search

    [RelayCommand]
    private void FocusSearch()
    {
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
        if (value.IsMatch) OpenFile(value.RelPath, value.Line, value.Column, value.Length);
        else OpenFile(value.RelPath);
    }

    public void OpenMarker(MarkerRowVM m) => OpenFile(m.RelPath, m.Line);

    public void OpenBigFile(BigFileVM f) => OpenFile(f.RelPath);

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
        if (SelectedFile is { } f) { IsSearchMode = false; FileFilter = ""; RevealInTree(f.RelPath); }
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
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Changed += (_, e) => OnFsEvent(e.FullPath, structural: false);
            _watcher.Created += (_, e) => OnFsEvent(e.FullPath, structural: true);
            _watcher.Deleted += (_, e) => OnFsEvent(e.FullPath, structural: true);
            _watcher.Renamed += (_, e) => { OnFsEvent(e.OldFullPath, true); OnFsEvent(e.FullPath, true); };
            _watcher.Error += (_, _) => ScheduleTreeRefresh();
            _watcher.EnableRaisingEvents = true;
        }
        catch { /* no watching; manual refresh still works */ }
    }

    private void OnFsEvent(string fullPath, bool structural)
    {
        if (_disposed || fullPath.Length <= RootPath.Length) return;
        var rel = fullPath[(RootPath.TrimEnd('\\', '/').Length + 1)..].Replace('\\', '/');
        // Inside .git only the state matters (commits, checkouts, staging done elsewhere); lock files come and go.
        if (rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) || rel.Equals(".git", StringComparison.OrdinalIgnoreCase))
        {
            if (!rel.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) Dispatcher.UIThread.Post(() => ScheduleGitRefresh());
            return;
        }
        foreach (var seg in rel.Split('/').SkipLast(1))
            if (ProjectScanner.IsSkipped(seg)) return;
        if (structural) ScheduleTreeRefresh();
        Dispatcher.UIThread.Post(() => { ScheduleFileReload(rel); ScheduleGitRefresh(); });
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
            ApplyGit(snap);
        });
    }

    private void ApplyGit(GitSnapshot? snap)
    {
        Git.Snapshot = snap;
        HasGit = snap is not null;
        foreach (var n in Tree) n.RefreshGit();
        foreach (var d in Docs.OfType<OpenFileViewModel>()) d.Change = Git.For(d.RelPath, false);
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
        _gitDebounce?.Cancel();
        _treeDebounce?.Cancel();
        _searchCts?.Cancel();
        _searchDebounce?.Cancel();
        _scanCts?.Cancel();
        Summary.Cancel();
        _watcher?.Dispose();
        foreach (var d in Docs.OfType<IDisposable>()) d.Dispose();
    }
}
