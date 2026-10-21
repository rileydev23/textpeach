namespace TextPeach.Services;

/// <summary>Which outline extractor understands a language.</summary>
public enum OutlineFamily { None, CSharp, Java, JavaScript, Python, Go, Rust, C, Php, Ruby, Kotlin, Swift, Shell, PowerShell, Lua, Markdown, Css, Sql, Json, Yaml, Xml }

public sealed record LanguageInfo(
    string Name,
    string Color,
    string[] LineComments,
    (string Open, string Close)[] BlockComments,
    OutlineFamily Family = OutlineFamily.None)
{
    public bool IsProse => Family is OutlineFamily.Markdown || Name == Languages.PlainText.Name;
}

public static class Languages
{
    private static readonly string[] Slash = { "//" };
    private static readonly string[] Hash = { "#" };
    private static readonly (string, string)[] CBlock = { ("/*", "*/") };
    private static readonly (string, string)[] XmlBlock = { ("<!--", "-->") };
    private static readonly (string, string)[] None = Array.Empty<(string, string)>();

    public static readonly LanguageInfo PlainText = new(Loc.T("lang.text"), "#8a8d93", Array.Empty<string>(), None);

    private static readonly Dictionary<string, LanguageInfo> ByExt = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, LanguageInfo> ByName = new(StringComparer.OrdinalIgnoreCase);

    static Languages()
    {
        void Add(LanguageInfo l, params string[] exts)
        {
            foreach (var e in exts) ByExt[e] = l;
        }
        Add(new("C#", "#9a7ae0", Slash, CBlock, OutlineFamily.CSharp), ".cs", ".csx");
        Add(new("F#", "#4ac7c0", Slash, new[] { ("(*", "*)") }), ".fs", ".fsx", ".fsi");
        Add(new("VB.NET", "#4a90d9", new[] { "'" }, None), ".vb");
        Add(new("Java", "#e0a24a", Slash, CBlock, OutlineFamily.Java), ".java");
        Add(new("Kotlin", "#9a7ae0", Slash, CBlock, OutlineFamily.Kotlin), ".kt", ".kts");
        Add(new("Scala", "#e0645a", Slash, CBlock, OutlineFamily.Java), ".scala");
        Add(new("Groovy", "#4ac7c0", Slash, CBlock, OutlineFamily.Java), ".groovy", ".gradle");
        Add(new("JavaScript", "#c8c84a", Slash, CBlock, OutlineFamily.JavaScript), ".js", ".mjs", ".cjs");
        Add(new("JSX", "#c8c84a", Slash, CBlock, OutlineFamily.JavaScript), ".jsx");
        Add(new("TypeScript", "#4a90d9", Slash, CBlock, OutlineFamily.JavaScript), ".ts", ".mts", ".cts");
        Add(new("TSX", "#4a90d9", Slash, CBlock, OutlineFamily.JavaScript), ".tsx");
        Add(new("Vue", "#6cc070", Slash, new[] { ("/*", "*/"), ("<!--", "-->") }, OutlineFamily.JavaScript), ".vue");
        Add(new("Svelte", "#e0645a", Slash, new[] { ("/*", "*/"), ("<!--", "-->") }, OutlineFamily.JavaScript), ".svelte");
        Add(new("Python", "#4a90d9", Hash, new[] { ("\"\"\"", "\"\"\"") }, OutlineFamily.Python), ".py", ".pyw", ".pyi");
        Add(new("Go", "#4ac7c0", Slash, CBlock, OutlineFamily.Go), ".go");
        Add(new("Rust", "#e0a24a", Slash, CBlock, OutlineFamily.Rust), ".rs");
        Add(new("C", "#8a8d93", Slash, CBlock, OutlineFamily.C), ".c", ".h");
        Add(new("C++", "#d96a9b", Slash, CBlock, OutlineFamily.C), ".cpp", ".cc", ".cxx", ".hpp", ".hh", ".hxx", ".ino");
        Add(new("Objective-C", "#4a90d9", Slash, CBlock, OutlineFamily.C), ".m", ".mm");
        Add(new("Swift", "#e0645a", Slash, CBlock, OutlineFamily.Swift), ".swift");
        Add(new("Dart", "#4ac7c0", Slash, CBlock, OutlineFamily.Java), ".dart");
        Add(new("PHP", "#9a7ae0", new[] { "//", "#" }, CBlock, OutlineFamily.Php), ".php");
        Add(new("Ruby", "#e0645a", Hash, new[] { ("=begin", "=end") }, OutlineFamily.Ruby), ".rb", ".rake", ".gemspec");
        Add(new("Lua", "#4a90d9", new[] { "--" }, new[] { ("--[[", "]]") }, OutlineFamily.Lua), ".lua");
        Add(new("Perl", "#4ac7c0", Hash, None), ".pl", ".pm");
        Add(new("R", "#4a90d9", Hash, None), ".r");
        Add(new("Julia", "#9a7ae0", Hash, new[] { ("#=", "=#") }), ".jl");
        Add(new("Elixir", "#9a7ae0", Hash, None), ".ex", ".exs");
        Add(new("Erlang", "#e0645a", new[] { "%" }, None), ".erl");
        Add(new("Haskell", "#9a7ae0", new[] { "--" }, new[] { ("{-", "-}") }), ".hs");
        Add(new("Clojure", "#6cc070", new[] { ";" }, None), ".clj", ".cljs", ".edn");
        Add(new("Shell", "#6cc070", Hash, None, OutlineFamily.Shell), ".sh", ".bash", ".zsh", ".fish");
        Add(new("PowerShell", "#4a90d9", Hash, new[] { ("<#", "#>") }, OutlineFamily.PowerShell), ".ps1", ".psm1", ".psd1");
        Add(new("Batch", "#c8c84a", new[] { "rem ", "REM ", "::", "@rem " }, None), ".bat", ".cmd");
        Add(new("SQL", "#e0a24a", new[] { "--" }, CBlock, OutlineFamily.Sql), ".sql");
        Add(new("HTML", "#e0645a", Array.Empty<string>(), XmlBlock, OutlineFamily.Xml), ".html", ".htm", ".xhtml", ".cshtml", ".razor");
        Add(new("XML", "#e0a24a", Array.Empty<string>(), XmlBlock, OutlineFamily.Xml), ".xml", ".xaml", ".axaml", ".csproj", ".fsproj", ".vbproj", ".props", ".targets", ".config", ".svg", ".plist", ".resx", ".nuspec", ".manifest", ".xsd", ".xsl");
        Add(new("CSS", "#9a7ae0", Array.Empty<string>(), CBlock, OutlineFamily.Css), ".css");
        Add(new("SCSS", "#d96a9b", Slash, CBlock, OutlineFamily.Css), ".scss", ".sass");
        Add(new("Less", "#4a90d9", Slash, CBlock, OutlineFamily.Css), ".less");
        Add(new("JSON", "#c8c84a", Slash, CBlock, OutlineFamily.Json), ".json", ".jsonc", ".json5", ".code-workspace");
        Add(new("YAML", "#e0645a", Hash, None, OutlineFamily.Yaml), ".yml", ".yaml");
        Add(new("TOML", "#8a8d93", Hash, None, OutlineFamily.Yaml), ".toml");
        Add(new("INI", "#8a8d93", new[] { ";", "#" }, None, OutlineFamily.Yaml), ".ini", ".cfg", ".conf", ".editorconfig", ".properties", ".env");
        Add(new("Markdown", "#4a90d9", Array.Empty<string>(), XmlBlock, OutlineFamily.Markdown), ".md", ".markdown", ".mdx");
        Add(new("reStructuredText", "#8a8d93", Array.Empty<string>(), None), ".rst");
        Add(new("Dockerfile", "#4a90d9", Hash, None), ".dockerfile");
        Add(new("Makefile", "#8a8d93", Hash, None), ".mk");
        Add(new("CMake", "#6cc070", Hash, None), ".cmake");
        Add(new("GraphQL", "#d96a9b", Hash, None), ".graphql", ".gql");
        Add(new("Protobuf", "#8a8d93", Slash, CBlock), ".proto");
        Add(new("Diff", "#6cc070", Array.Empty<string>(), None), ".diff", ".patch");
        Add(new("LaTeX", "#6cc070", new[] { "%" }, None), ".tex", ".sty", ".cls");
        Add(PlainText, ".txt", ".log", ".csv", ".tsv", ".text");

        foreach (var l in ByExt.Values) ByName.TryAdd(l.Name, l);
    }

    public static LanguageInfo For(string path)
    {
        var name = Path.GetFileName(path);
        switch (name.ToLowerInvariant())
        {
            case "dockerfile": return ByExt[".dockerfile"];
            case "makefile" or "gnumakefile": return ByExt[".mk"];
            case "cmakelists.txt": return ByExt[".cmake"];
            case ".gitignore" or ".gitattributes" or ".dockerignore" or ".npmignore": return ByExt[".ini"] with { Name = "Ignore" };
        }
        return ByExt.TryGetValue(Path.GetExtension(path), out var l) ? l : PlainText;
    }

    public static string ColorOf(string languageName) => ByName.TryGetValue(languageName, out var l) ? l.Color : PlainText.Color;

    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp" };

    private static readonly HashSet<string> BinaryExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".pdb", ".so", ".dylib", ".bin", ".obj", ".o", ".a", ".lib", ".class", ".jar", ".war",
        ".zip", ".7z", ".rar", ".gz", ".tar", ".tgz", ".bz2", ".xz", ".nupkg", ".snupkg",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt",
        ".mp3", ".mp4", ".wav", ".ogg", ".flac", ".avi", ".mov", ".mkv", ".webm",
        ".ttf", ".otf", ".woff", ".woff2", ".eot", ".psd", ".ai", ".tiff", ".tif",
        ".db", ".sqlite", ".mdb", ".pyc", ".pyo", ".wasm", ".cache", ".suo", ".lock.db",
    };

    public static bool IsImage(string path) => ImageExt.Contains(Path.GetExtension(path));
    public static bool IsKnownBinary(string path) => BinaryExt.Contains(Path.GetExtension(path));
}
