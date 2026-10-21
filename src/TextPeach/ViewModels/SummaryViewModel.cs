using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using TextPeach.Controls;
using TextPeach.Services;

namespace TextPeach.ViewModels;

/// <summary>The permanent first tab of a project: languages, sizes and markers across all files.</summary>
public sealed partial class SummaryViewModel : TabViewModel
{
    private const int MaxMarkers = 300;

    public override bool Closable => false;
    public string RootPath { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyText = "";
    [ObservableProperty] private string _kindText = "";
    [ObservableProperty] private IReadOnlyList<StatTile> _tiles = Array.Empty<StatTile>();
    [ObservableProperty] private IReadOnlyList<LangStatVM> _languages = Array.Empty<LangStatVM>();
    [ObservableProperty] private IReadOnlyList<BigFileVM> _largest = Array.Empty<BigFileVM>();
    [ObservableProperty] private IReadOnlyList<MarkerRowVM> _markers = Array.Empty<MarkerRowVM>();
    [ObservableProperty] private string _markersHeader = "";

    private CancellationTokenSource? _cts;

    private static readonly Regex MarkerRegex = new(@"\b(TODO|FIXME|HACK|XXX|BUG)\b\s*(?:\([^)]*\))?\s*[:\-]?\s*(.*)$", RegexOptions.Compiled | RegexOptions.Multiline);

    public SummaryViewModel(string root)
    {
        RootPath = root;
        Title = Loc.T("summary.tab");
        ToolTip = Loc.T("summary.title");
    }

    private sealed record FileStat(string Rel, string Lang, int Lines, int Blank, long Size, List<MarkerRowVM> Markers);

    public async Task ComputeAsync(IReadOnlyList<string> files)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        IsBusy = true;
        BusyText = Loc.F("summary.analyzing", files.Count);
        KindText = ProjectScanner.DetectKind(RootPath) ?? "";
        try
        {
            var root = RootPath;
            var stats = await Task.Run(() =>
            {
                var bag = new System.Collections.Concurrent.ConcurrentBag<FileStat>();
                Parallel.ForEach(files, new ParallelOptions { CancellationToken = cts.Token, MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1) }, rel =>
                {
                    var full = Path.Combine(root, rel);
                    long size = 0;
                    try { size = new FileInfo(full).Length; } catch { }
                    var lang = Services.Languages.For(rel);
                    var text = FileLoader.TryReadText(full, 2 * 1024 * 1024);
                    if (text is null)
                    {
                        bag.Add(new FileStat(rel, "", 0, 0, size, new()));
                        return;
                    }
                    var (lines, blank) = FileAnalyzer.CountLines(text);
                    var markers = new List<MarkerRowVM>();
                    // Cheap pre-check, then the same rules as the per-file analysis (prose, inline code…).
                    if (MarkerRegex.IsMatch(text))
                    {
                        foreach (var m in FileAnalyzer.FindMarkers(text, lang).Where(m => m.Tag is "TODO" or "FIXME" or "HACK" or "XXX" or "BUG").Take(50))
                            markers.Add(new MarkerRowVM { Tag = m.Tag, Text = m.Text, Line = m.Line, RelPath = rel });
                    }
                    bag.Add(new FileStat(rel, lang.Name, lines, blank, size, markers));
                });
                return bag.ToList();
            }, cts.Token);
            if (cts.IsCancellationRequested) return;

            var text = stats.Where(s => s.Lang.Length > 0).ToList();
            int totalLines = text.Sum(s => s.Lines);
            int nonBlank = text.Sum(s => s.Lines - s.Blank);
            long totalSize = stats.Sum(s => s.Size);
            var byLang = text.GroupBy(s => s.Lang)
                .Select(g => (Name: g.Key, Files: g.Count(), Lines: g.Sum(s => s.Lines)))
                .OrderByDescending(x => x.Lines).ToList();
            int maxLines = byLang.Count > 0 ? Math.Max(1, byLang[0].Lines) : 1;
            Languages = byLang.Take(14).Select(x => new LangStatVM
            {
                Name = x.Name,
                Files = x.Files,
                Lines = x.Lines,
                Share = totalLines > 0 ? 100.0 * x.Lines / totalLines : 0,
                BarWidth = Math.Max(2, 220.0 * x.Lines / maxLines),
                Brush = Palette.FromHex(Services.Languages.ColorOf(x.Name)),
            }).ToList();
            Largest = text.OrderByDescending(s => s.Lines).Take(12)
                .Select(s => new BigFileVM { RelPath = s.Rel, Lines = s.Lines, Size = s.Size }).ToList();
            var allMarkers = stats.SelectMany(s => s.Markers).OrderBy(m => m.RelPath, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Line).ToList();
            Markers = allMarkers.Take(MaxMarkers).ToList();
            MarkersHeader = allMarkers.Count > MaxMarkers ? Loc.F("summary.markersOf", MaxMarkers, allMarkers.Count) : Loc.F("summary.markers", allMarkers.Count);
            Tiles = new[]
            {
                new StatTile { Label = Loc.T("summary.files"), Value = $"{stats.Count:N0}" + (files.Count >= ProjectScanner.MaxFiles ? "+" : "") },
                new StatTile { Label = Loc.T("summary.lines"), Value = $"{totalLines:N0}" },
                new StatTile { Label = Loc.T("summary.nonBlank"), Value = $"{nonBlank:N0}" },
                new StatTile { Label = Loc.T("summary.size"), Value = FileLoader.FormatSize(totalSize) },
                new StatTile { Label = Loc.T("summary.languagesCount"), Value = $"{byLang.Count(l => l.Name != Services.Languages.PlainText.Name)}" },
            };
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_cts == cts) IsBusy = false;
        }
    }

    public void Cancel() => _cts?.Cancel();
}
