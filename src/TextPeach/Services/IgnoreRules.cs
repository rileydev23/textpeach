namespace TextPeach.Services;

/// <summary>
/// Which folders a project leaves out of the tree, the file list, the search and the change feed: the built-in
/// <see cref="ProjectScanner.SkipDirs"/> plus the project's own lines. A line is a folder name ("vendor", any depth),
/// a project-relative path ("docs/generated", only that one) or "!name" to show a folder that is skipped by default.
/// </summary>
public sealed class IgnoreRules
{
    public static readonly IgnoreRules Default = new(Array.Empty<string>());

    private readonly HashSet<string> _names = new(ProjectScanner.SkipDirs, StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _paths = new();

    /// <summary>The project's lines as given, for the settings dialog.</summary>
    public IReadOnlyList<string> Lines { get; }

    public IgnoreRules(IEnumerable<string> lines)
    {
        Lines = Normalize(lines);
        foreach (var line in Lines)
        {
            if (line.StartsWith('!')) _names.Remove(line[1..].Trim('/'));
            else if (line.Contains('/')) _paths.Add(line.Trim('/') + "/");
            else _names.Add(line);
        }
    }

    /// <summary>Trimmed, '/'-separated, without blanks, comments or duplicates.</summary>
    public static List<string> Normalize(IEnumerable<string> lines)
    {
        var result = new List<string>();
        foreach (var raw in lines)
        {
            var l = raw.Trim().Replace('\\', '/');
            if (l.Length == 0 || l.StartsWith('#')) continue;
            bool show = l.StartsWith('!');
            l = l.TrimStart('!').Trim().Trim('/');
            if (l.StartsWith("./")) l = l[2..];
            if (l.Length == 0) continue;
            if (show) l = "!" + l;
            if (!result.Contains(l, StringComparer.OrdinalIgnoreCase)) result.Add(l);
        }
        return result;
    }

    /// <summary>Whether the folder at <paramref name="relDir"/> (project-relative, '/'-separated) is left out.</summary>
    public bool IsIgnoredDir(string relDir)
    {
        var name = relDir[(relDir.LastIndexOf('/') + 1)..];
        if (_names.Contains(name)) return true;
        var withSlash = relDir + "/";
        foreach (var p in _paths)
            if (withSlash.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Whether a file or folder lies inside an ignored folder (any of its parents).</summary>
    public bool IsInsideIgnored(string relPath)
    {
        int i = -1;
        while ((i = relPath.IndexOf('/', i + 1)) >= 0)
            if (IsIgnoredDir(relPath[..i])) return true;
        return false;
    }
}
