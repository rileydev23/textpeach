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
    public ObservableCollection<FileNode> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    private bool _loaded;

    public IBrush GlyphBrush => IsDirectory ? Palette.Amber : Palette.FromHex(Languages.For(Name).Color);
    public string Glyph => IsDirectory ? "■" : "●";
    public IBrush Foreground => IsPlaceholder ? Palette.Dim : Palette.Text;

    public static FileNode Create(string fullPath, string root)
    {
        bool dir = Directory.Exists(fullPath);
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        var node = new FileNode { Name = Path.GetFileName(fullPath), FullPath = fullPath, RelPath = rel, IsDirectory = dir };
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
        foreach (var n in ListChildren(FullPath, root)) Children.Add(n);
    }

    public static List<FileNode> ListChildren(string dir, string root)
    {
        var result = new List<FileNode>();
        try
        {
            foreach (var d in Directory.EnumerateDirectories(dir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                if (ProjectScanner.IsSkipped(Path.GetFileName(d))) continue;
                result.Add(Create(d, root));
            }
            foreach (var f in Directory.EnumerateFiles(dir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                result.Add(Create(f, root));
        }
        catch { /* inaccessible */ }
        return result;
    }
}

/// <summary>Flat result row shown while the file filter has text.</summary>
public sealed class FileHit
{
    public required string RelPath { get; init; }
    public string FileName => RelPath[(RelPath.LastIndexOf('/') + 1)..];
    public string Directory => RelPath.LastIndexOf('/') is var i and > 0 ? RelPath[..(i + 1)] : "";
    public IBrush GlyphBrush => Palette.FromHex(Languages.For(RelPath).Color);
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
    public string FilesText => Files == 1 ? "1 archivo" : $"{Files:N0} archivos";
    public string LinesText => $"{Lines:N0}";
    public string ShareText => $"{Share:0.#}%";
}

public sealed class BigFileVM
{
    public required string RelPath { get; init; }
    public required int Lines { get; init; }
    public required long Size { get; init; }
    public string LinesText => $"{Lines:N0} líneas";
    public string SizeText => FileLoader.FormatSize(Size);
    public IBrush GlyphBrush => Palette.FromHex(Languages.For(RelPath).Color);
}

public sealed class StatTile
{
    public required string Label { get; init; }
    public required string Value { get; init; }
}
