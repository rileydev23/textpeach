using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TextPeach.Controls;
using TextPeach.Services;

namespace TextPeach.ViewModels;

/// <summary>A node of the lazy file tree. Directories load their children on first expand.</summary>
public sealed partial class FileNode : ObservableObject
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string RelPath { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsPlaceholder { get; init; }
    /// <summary>The project's git state, shared by every node; null when the project is not a repository.</summary>
    public GitDecorations? Git { get; init; }
    /// <summary>The project's folder rules, shared like <see cref="Git"/>.</summary>
    public IgnoreRules Rules { get; init; } = IgnoreRules.Default;
    public ObservableCollection<FileNode> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Foreground), nameof(StatusText), nameof(StatusBrush), nameof(HasStatus), nameof(StatusTip))]
    private GitChange _change;
    private bool _loaded;

    public IBrush GlyphBrush => IsDirectory ? Palette.Amber : Palette.FromHex(Languages.For(Name).Color);
    public string Glyph => IsDirectory ? "■" : "●";
    public IBrush Foreground => IsPlaceholder ? Palette.Dim : GitLook.NameBrush(Change);
    public string StatusText => GitLook.Letter(Change, IsDirectory);
    public IBrush StatusBrush => GitLook.Brush(Change);
    public bool HasStatus => StatusText.Length > 0;
    public string StatusTip => GitLook.Describe(Change) is { Length: > 0 } d ? $"{RelPath} · {d}" : RelPath;

    public static FileNode Create(string fullPath, string root, GitDecorations? git = null, IgnoreRules? rules = null)
    {
        bool dir = Directory.Exists(fullPath);
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        var node = new FileNode { Name = Path.GetFileName(fullPath), FullPath = fullPath, RelPath = rel, IsDirectory = dir, Git = git, Rules = rules ?? IgnoreRules.Default };
        node.Change = git?.For(rel, dir) ?? GitChange.None;
        if (dir) node.Children.Add(new FileNode { Name = "…", FullPath = "", RelPath = "", IsPlaceholder = true });
        return node;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) EnsureLoaded();
    }

    public void EnsureLoaded()
    {
        if (_loaded || !IsDirectory) return;
        _loaded = true;
        var root = FullPath[..^RelPath.Length].TrimEnd('\\', '/');
        Children.Clear();
        foreach (var n in ListChildren(FullPath, root, Git, Rules)) Children.Add(n);
    }

    /// <summary>Re-reads the git state of this node and of every loaded node below it.</summary>
    public void RefreshGit()
    {
        if (IsPlaceholder) return;
        Change = Git?.For(RelPath, IsDirectory) ?? GitChange.None;
        if (_loaded) foreach (var c in Children) c.RefreshGit();
    }

    public static List<FileNode> ListChildren(string dir, string root, GitDecorations? git = null, IgnoreRules? rules = null)
    {
        var result = new List<FileNode>();
        rules ??= IgnoreRules.Default;
        try
        {
            foreach (var d in Directory.EnumerateDirectories(dir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                if (rules.IsIgnoredDir(Path.GetRelativePath(root, d).Replace('\\', '/'))) continue;
                result.Add(Create(d, root, git, rules));
            }
            foreach (var f in Directory.EnumerateFiles(dir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                result.Add(Create(f, root, git, rules));
        }
        catch { /* inaccessible */ }
        return result;
    }
}

/// <summary>Holder of the latest <see cref="GitSnapshot"/> of a project, read by the tree, the filter list and the tabs.</summary>
public sealed class GitDecorations
{
    public GitSnapshot? Snapshot { get; set; }
    public GitChange For(string relPath, bool isDirectory) => Snapshot?.For(relPath, isDirectory) ?? GitChange.None;
}

/// <summary>Colors and letters of git states, as most editors show them (docs/DISENO.md, "Estado de git").</summary>
public static class GitLook
{
    public static IBrush Brush(GitChange c) => c switch
    {
        GitChange.Modified => Palette.Amber,
        GitChange.Untracked or GitChange.Added => Palette.Green,
        GitChange.Renamed => Palette.Teal,
        GitChange.Deleted => Palette.Red,
        GitChange.Conflict => Palette.Pink,
        GitChange.Ignored => Palette.Dim,
        _ => Palette.Text,
    };

    public static IBrush NameBrush(GitChange c) => c == GitChange.None ? Palette.Text : Brush(c);

    /// <summary>Files get the letter; folders a dot that says "something changed in here".</summary>
    public static string Letter(GitChange c, bool isDirectory) => c switch
    {
        GitChange.None or GitChange.Ignored => "",
        _ when isDirectory => "•",
        GitChange.Modified => "M",
        GitChange.Untracked => "U",
        GitChange.Added => "A",
        GitChange.Renamed => "R",
        GitChange.Deleted => "D",
        GitChange.Conflict => "!",
        _ => "",
    };

    public static string Describe(GitChange c) => c switch
    {
        GitChange.Modified => Loc.T("git.modified"),
        GitChange.Untracked => Loc.T("git.untracked"),
        GitChange.Added => Loc.T("git.added"),
        GitChange.Renamed => Loc.T("git.renamed"),
        GitChange.Deleted => Loc.T("git.deleted"),
        GitChange.Conflict => Loc.T("git.conflict"),
        GitChange.Ignored => Loc.T("git.ignored"),
        _ => "",
    };
}

/// <summary>Flat result row shown while the file filter has text.</summary>
public sealed class FileHit
{
    public required string RelPath { get; init; }
    public GitChange Change { get; init; }
    public IBrush NameBrush => GitLook.NameBrush(Change);
    public string StatusText => GitLook.Letter(Change, false);
    public IBrush StatusBrush => GitLook.Brush(Change);
    public string FileName => RelPath[(RelPath.LastIndexOf('/') + 1)..];
    public string Directory => RelPath.LastIndexOf('/') is var i and > 0 ? RelPath[..(i + 1)] : "";
    public IBrush GlyphBrush => Palette.FromHex(Languages.For(RelPath).Color);
}

public enum DiskChange { Created, Changed, Deleted }

/// <summary>A file that changed on disk while the project was open (newest first in "Recent").</summary>
public sealed partial class RecentChangeVM : ObservableObject
{
    public required string RelPath { get; init; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindText), nameof(KindBrush), nameof(KindTip))]
    private DiskChange _kind;
    [ObservableProperty] private DateTime _when;
    [ObservableProperty] private string _agoText = "";
    [ObservableProperty] private int _count = 1;

    public string FileName => RelPath[(RelPath.LastIndexOf('/') + 1)..];
    public string Directory => RelPath.LastIndexOf('/') is var i and > 0 ? RelPath[..(i + 1)] : "";
    public IBrush GlyphBrush => Palette.FromHex(Languages.For(RelPath).Color);
    public string KindText => Kind switch { DiskChange.Created => "+", DiskChange.Deleted => "−", _ => "~" };
    public IBrush KindBrush => Kind switch { DiskChange.Created => Palette.Green, DiskChange.Deleted => Palette.Red, _ => Palette.Amber };
    public string KindTip => Loc.T(Kind switch { DiskChange.Created => "recent.created", DiskChange.Deleted => "recent.deleted", _ => "recent.changed" });
    public bool IsDeleted => Kind == DiskChange.Deleted;

    partial void OnKindChanged(DiskChange value) => OnPropertyChanged(nameof(IsDeleted));

    public void UpdateAgo(DateTime now) => AgoText = Ago(now - When);

    public static string Ago(TimeSpan t) => t.TotalSeconds switch
    {
        < 10 => Loc.T("recent.now"),
        < 60 => Loc.F("recent.seconds", (int)t.TotalSeconds),
        < 3600 => Loc.F("recent.minutes", (int)t.TotalMinutes),
        _ => Loc.F("recent.hours", (int)t.TotalHours),
    };
}

public enum SearchRowKind { FileHeader, Match, Note }

public sealed class SearchRowVM
{
    public SearchRowKind Kind { get; init; }
    public string RelPath { get; init; } = "";
    public int Line { get; init; }
    public int Column { get; init; }
    public int Length { get; init; }
    public string Before { get; init; } = "";
    public string Match { get; init; } = "";
    public string After { get; init; } = "";
    public string Text { get; init; } = "";
    public string Detail { get; init; } = "";
    public string LineText => Line.ToString();
    public bool IsFileHeader => Kind == SearchRowKind.FileHeader;
    public bool IsMatch => Kind == SearchRowKind.Match;
    public bool IsNote => Kind == SearchRowKind.Note;
    public IBrush GlyphBrush => Palette.FromHex(Languages.For(RelPath).Color);
}

public sealed class MetricRow
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public IBrush ValueBrush { get; init; } = Palette.Text;
    public string? Hint { get; init; }
}

public sealed class OutlineRowVM
{
    public required OutlineItem Item { get; init; }
    public string Name => Item.Name;
    public int Line => Item.Line;
    public string LineText => Item.Line.ToString();
    public Thickness Pad => new(8 + Math.Min(Item.Depth, 8) * 12, 2, 8, 2);
    public string Letter => Item.Kind switch
    {
        SymbolKind.Namespace => "N",
        SymbolKind.Class => "C",
        SymbolKind.Interface => "I",
        SymbolKind.Struct => "S",
        SymbolKind.Enum => "E",
        SymbolKind.Record => "R",
        SymbolKind.Function => "ƒ",
        SymbolKind.Method => "M",
        SymbolKind.Property => "P",
        SymbolKind.Heading => "#",
        SymbolKind.Key => "K",
        SymbolKind.Selector => "{}",
        SymbolKind.Table => "T",
        SymbolKind.Element => "<>",
        _ => "·",
    };
    public IBrush LetterBrush => Item.Kind switch
    {
        SymbolKind.Namespace => Palette.Dim,
        SymbolKind.Class or SymbolKind.Record => Palette.Amber,
        SymbolKind.Interface or SymbolKind.Struct => Palette.Teal,
        SymbolKind.Enum => Palette.Olive,
        SymbolKind.Function or SymbolKind.Method => Palette.Purple,
        SymbolKind.Property => Palette.Blue,
        SymbolKind.Heading or SymbolKind.Key => Palette.Blue,
        SymbolKind.Selector => Palette.Pink,
        SymbolKind.Table => Palette.Green,
        _ => Palette.Dim,
    };
    public FontWeight Weight => Item.Depth == 0 && Item.Kind is not (SymbolKind.Method or SymbolKind.Property or SymbolKind.Key) ? FontWeight.SemiBold : FontWeight.Normal;
}

public sealed class MarkerRowVM
{
    public required string Tag { get; init; }
    public required string Text { get; init; }
    public required int Line { get; init; }
    public string RelPath { get; init; } = "";
    public string LineText => Line.ToString();
    public string Location => RelPath.Length > 0 ? $"{RelPath}:{Line}" : Line.ToString();
    public IBrush TagBrush => TagColor(Tag);

    public static IBrush TagColor(string tag) => tag switch
    {
        "FIXME" or "BUG" => Palette.Red,
        "HACK" or "XXX" => Palette.Amber,
        "NOTE" or "REVIEW" => Palette.Green,
        "OPTIMIZE" => Palette.Purple,
        _ => Palette.Blue,
    };
}

public sealed class LangStatVM
{
    public required string Name { get; init; }
    public required int Files { get; init; }
    public required int Lines { get; init; }
    public required double Share { get; init; }
    public required double BarWidth { get; init; }
    public IBrush Brush { get; init; } = Palette.Dim;
    public string FilesText => Loc.Plural("common.files", Files);
    public string LinesText => $"{Lines:N0}";
    public string ShareText => $"{Share:0.#}%";
}

public sealed class BigFileVM
{
    public required string RelPath { get; init; }
    public required int Lines { get; init; }
    public required long Size { get; init; }
    public string LinesText => Loc.Plural("common.lines", Lines);
    public string SizeText => FileLoader.FormatSize(Size);
    public IBrush GlyphBrush => Palette.FromHex(Languages.For(RelPath).Color);
}

public sealed class StatTile
{
    public required string Label { get; init; }
    public required string Value { get; init; }
}
