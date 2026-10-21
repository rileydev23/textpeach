namespace TextPeach.Services;

/// <summary>Walks a project folder, skipping build output and tool folders.</summary>
public static class ProjectScanner
{
    public const int MaxFiles = 60000;

    public static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", ".vs", ".vscode", ".idea", ".gradle", ".next", ".nuxt", ".cache", ".parcel-cache",
        ".venv", "venv", "env", "__pycache__", ".pytest_cache", ".mypy_cache", ".tox",
        "node_modules", "bower_components", "bin", "obj", "dist", "build", "out", "target", "packages",
        "coverage", ".terraform", "$RECYCLE.BIN", "System Volume Information",
    };

    public static bool IsSkipped(string dirName) => SkipDirs.Contains(dirName);

    /// <summary>Relative paths (with '/' separators) of every file worth showing.</summary>
    public static List<string> ListFiles(string root, CancellationToken ct, IgnoreRules? rules = null)
    {
        rules ??= IgnoreRules.Default;
        var result = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);
        int prefix = root.TrimEnd('\\', '/').Length + 1;
        while (stack.Count > 0 && result.Count < MaxFiles)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    result.Add(f[prefix..].Replace('\\', '/'));
                    if (result.Count >= MaxFiles) break;
                }
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (rules.IsIgnoredDir(sub[prefix..].Replace('\\', '/'))) continue;
                    if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
                    stack.Push(sub);
                }
            }
            catch { /* inaccessible */ }
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static readonly (string Marker, string Kind)[] Markers =
    {
        ("*.sln", ".NET"), ("*.slnx", ".NET"), ("*.csproj", ".NET"), ("*.fsproj", ".NET"),
        ("package.json", "Node"), ("Cargo.toml", "Rust"), ("go.mod", "Go"),
        ("pyproject.toml", "Python"), ("requirements.txt", "Python"), ("setup.py", "Python"),
        ("pom.xml", "Java"), ("build.gradle", "Java"), ("build.gradle.kts", "Kotlin"),
        ("composer.json", "PHP"), ("Gemfile", "Ruby"), ("pubspec.yaml", "Flutter"),
        ("CMakeLists.txt", "C/C++"), ("Makefile", "Make"), ("index.html", "Web"),
    };

    /// <summary>A short label for the project type, from well-known marker files.</summary>
    public static string? DetectKind(string dir)
    {
        try
        {
            foreach (var (marker, kind) in Markers)
            {
                bool hit = marker.Contains('*')
                    ? Directory.EnumerateFiles(dir, marker).Any()
                    : File.Exists(Path.Combine(dir, marker));
                if (hit) return kind;
            }
            if (Directory.Exists(Path.Combine(dir, ".git"))) return "Git";
        }
        catch { }
        return null;
    }

    /// <summary>Folders under <paramref name="root"/> that look like projects (does not descend into them).</summary>
    public static List<string> FindProjects(string root, int maxDepth)
    {
        var result = new List<string>();
        void Walk(string dir, int depth)
        {
            if (depth > 0 && DetectKind(dir) is not null)
            {
                result.Add(dir);
                return;
            }
            if (depth >= maxDepth) return;
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (IsSkipped(name) || name.StartsWith('.')) continue;
                    Walk(sub, depth + 1);
                }
            }
            catch { /* inaccessible */ }
        }
        Walk(root, 0);
        return result;
    }
}
