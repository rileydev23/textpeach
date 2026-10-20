using System.Text.RegularExpressions;

namespace TextPeach.Services;

public enum SymbolKind { Namespace, Class, Interface, Struct, Enum, Record, Function, Method, Property, Heading, Key, Selector, Table, Element }

public sealed record OutlineItem(SymbolKind Kind, string Name, int Line, int Depth);

public sealed record MarkerItem(string Tag, string Text, int Line);

public sealed class FileAnalysis
{
    public int Lines { get; init; }
    public int Code { get; init; }
    public int Comment { get; init; }
    public int Blank { get; init; }
    public int Chars { get; init; }
    public int LongestLine { get; init; }
    public int LongestLineNo { get; init; }
    public string Indent { get; init; } = "—";
    public string LineEnding { get; init; } = "—";
    public int Branches { get; init; }
    public int MaxDepth { get; init; }
    public bool TrailingWhitespace { get; init; }
    public List<OutlineItem> Outline { get; init; } = new();
    public List<MarkerItem> Markers { get; init; } = new();

    public static readonly FileAnalysis Empty = new();
}

/// <summary>
/// Heuristic, regex-based analysis. It is meant to give a quick feel for a file while reading it,
/// not to be a parser: numbers are labelled "aprox." where they can be off.
/// </summary>
public static partial class FileAnalyzer
{
    private const int MaxOutline = 2000, MaxMarkers = 1000;

    public static FileAnalysis Analyze(string text, LanguageInfo lang)
    {
        var lines = SplitLines(text, out int crlf, out int lf);
        int blank = 0, comment = 0, longest = 0, longestNo = 0, tabs = 0, spaces = 0, trailing = 0;
        var spaceWidths = new Dictionary<int, int>();
        int prevSpaces = 0;
        string? inBlock = null;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length > longest) { longest = line.Length; longestNo = i + 1; }
            var t = line.Trim();
            if (t.Length == 0) { blank++; continue; }
            if (line.Length > 0 && (line[^1] == ' ' || line[^1] == '\t')) trailing++;

            if (line[0] == '\t') tabs++;
            else if (line[0] == ' ')
            {
                spaces++;
                int n = 0;
                while (n < line.Length && line[n] == ' ') n++;
                int delta = Math.Abs(n - prevSpaces);
                if (delta is 2 or 3 or 4 or 8) spaceWidths[delta] = spaceWidths.GetValueOrDefault(delta) + 1;
                prevSpaces = n;
            }
            else prevSpaces = 0;

            if (!lang.IsProse && IsCommentLine(t, lang, ref inBlock)) comment++;
        }

        string indent = tabs == 0 && spaces == 0 ? "—"
            : tabs > spaces ? (spaces > tabs / 10 ? "Mixta (tabs)" : "Tabs")
            : (tabs > spaces / 10 ? "Mixta (espacios)" : $"{(spaceWidths.Count > 0 ? spaceWidths.MaxBy(k => k.Value).Key : 4)} espacios");
        string eol = crlf == 0 && lf == 0 ? "—" : crlf > 0 && lf > 0 ? $"Mixto ({crlf} CRLF / {lf} LF)" : crlf > 0 ? "CRLF" : "LF";

        int total = lines.Count;
        return new FileAnalysis
        {
            Lines = total,
            Blank = blank,
            Comment = comment,
            Code = lang.IsProse ? 0 : total - blank - comment,
            Chars = text.Length,
            LongestLine = longest,
            LongestLineNo = longestNo,
            Indent = indent,
            LineEnding = eol,
            Branches = lang.IsProse || lang.Family is OutlineFamily.Json or OutlineFamily.Yaml or OutlineFamily.Xml ? 0 : BranchRegex().Count(text),
            MaxDepth = MaxBraceDepth(text, lang),
            TrailingWhitespace = trailing > 0,
            Outline = Outline(lines, lang),
            Markers = Markers(lines, lang.IsProse),
        };
    }

    public static List<string> SplitLines(string text, out int crlf, out int lf)
    {
        var result = new List<string>();
        crlf = lf = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                bool cr = i > start && text[i - 1] == '\r';
                if (cr) crlf++; else lf++;
                result.Add(text.Substring(start, i - start - (cr ? 1 : 0)));
                start = i + 1;
            }
        }
        if (start < text.Length || result.Count == 0) result.Add(text[start..]);
        return result;
    }

    /// <summary>Counts lines quickly (summary view: no allocations per line).</summary>
    public static (int Lines, int Blank) CountLines(string text)
    {
        int lines = 1, blank = 0;
        bool sawContent = false;
        foreach (var c in text)
        {
            if (c == '\n') { lines++; if (!sawContent) blank++; sawContent = false; }
            else if (!char.IsWhiteSpace(c)) sawContent = true;
        }
        if (text.Length > 0 && text[^1] == '\n') lines--;      // a final newline does not start a new line
        else if (!sawContent) blank++;
        return (lines, blank);
    }

    private static bool IsCommentLine(string t, LanguageInfo lang, ref string? inBlock)
    {
        if (inBlock is not null)
        {
            int end = t.IndexOf(inBlock, StringComparison.Ordinal);
            if (end < 0) return true;
            var rest = t[(end + inBlock.Length)..].Trim();
            inBlock = null;
            return rest.Length == 0 || IsCommentLine(rest, lang, ref inBlock);
        }
        foreach (var lc in lang.LineComments)
            if (t.StartsWith(lc, StringComparison.Ordinal)) return true;
        foreach (var (open, close) in lang.BlockComments)
        {
            if (!t.StartsWith(open, StringComparison.Ordinal)) continue;
            int end = t.IndexOf(close, open.Length, StringComparison.Ordinal);
            if (end < 0) { inBlock = close; return true; }
            var rest = t[(end + close.Length)..].Trim();
            return rest.Length == 0 || IsCommentLine(rest, lang, ref inBlock);
        }
        return false;
    }

    private static int MaxBraceDepth(string text, LanguageInfo lang)
    {
        if (lang.Family is OutlineFamily.Python or OutlineFamily.Yaml or OutlineFamily.Markdown or OutlineFamily.None or OutlineFamily.Xml or OutlineFamily.Ruby)
            return 0;
        int depth = 0, max = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == '\\') i++;
                else if (c == quote || c == '\n') quote = '\0';
                continue;
            }
            switch (c)
            {
                case '"' or '\'' or '`': quote = c; break;
                case '{': depth++; if (depth > max) max = depth; break;
                case '}': if (depth > 0) depth--; break;
            }
        }
        return max;
    }

    // ------------------------------------------------------------ markers

    [GeneratedRegex(@"\b(TODO|FIXME|HACK|XXX|BUG|NOTE|OPTIMIZE|REVIEW)\b\s*(?:\([^)]*\))?\s*[:\-]?\s*(.*)$")]
    private static partial Regex MarkerRegex();

    private static List<MarkerItem> Markers(List<string> lines, bool prose)
    {
        var list = new List<MarkerItem>();
        for (int i = 0; i < lines.Count && list.Count < MaxMarkers; i++)
        {
            var m = MarkerRegex().Match(lines[i]);
            if (!m.Success) continue;
            // A tag inside `inline code` is a mention (docs), not a marker.
            if (lines[i].AsSpan(0, m.Index).Count('`') % 2 == 1) continue;
            // In prose only a tag that opens the line (after bullets/quotes) is a marker; elsewhere it is just a word.
            if (prose && lines[i].AsSpan(0, m.Index).ContainsAnyExcept(" 	-*>#[]!<") ) continue;
            var text = m.Groups[2].Value.Trim().TrimEnd('*', '/', '-', '>', ' ');
            list.Add(new MarkerItem(m.Groups[1].Value, text.Length > 0 ? text : lines[i].Trim(), i + 1));
        }
        return list;
    }

    [GeneratedRegex(@"\b(if|else\s+if|elif|for|foreach|while|case|catch|except|when|guard)\b|&&|\|\||\?\?|(?<![?:])\?(?![?.:])")]
    private static partial Regex BranchRegex();

    // ------------------------------------------------------------ outline

    private static readonly HashSet<string> NotNames = new(StringComparer.Ordinal)
    {
        "if", "for", "foreach", "while", "switch", "catch", "return", "using", "lock", "fixed", "else", "do", "try",
        "new", "await", "throw", "sizeof", "typeof", "nameof", "function", "when", "match", "elif", "with", "base", "this",
    };

    private static readonly string Mods =
        @"(?:(?:public|private|protected|internal|static|virtual|override|abstract|async|sealed|extern|unsafe|new|readonly|partial|final|synchronized|native|default|required|file|const|volatile|transient|strictfp)\s+)";

    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|internal|static|sealed|abstract|partial|readonly|unsafe|file|ref|new)\s+)*(?:(class|interface|struct|enum|record(?:\s+struct|\s+class)?|namespace)\s+([A-Za-z_][\w.]*))")]
    private static partial Regex CsTypeRegex();

    private static readonly Regex CsMethodRegex = new(
        @"^\s*" + Mods + @"+(?:[\w<>\[\],.?()\s]+?\s+)?([A-Za-z_]\w*)\s*(?:<[^>()]*>)?\s*\(", RegexOptions.Compiled);

    [GeneratedRegex(@"^\s*(?:public|private|protected|internal|static|virtual|override|abstract|required|new|\s)+[\w<>\[\],.?]+\s+([A-Z]\w*)\s*(?:\{|=>)")]
    private static partial Regex CsPropertyRegex();

    [GeneratedRegex(@"^\s*(?:export\s+)?(?:default\s+)?(?:abstract\s+)?(?:declare\s+)?(class|interface|enum|type|namespace|module)\s+([A-Za-z_$][\w$]*)")]
    private static partial Regex JsTypeRegex();

    [GeneratedRegex(@"^\s*(?:export\s+)?(?:default\s+)?(?:async\s+)?function\s*\*?\s*([A-Za-z_$][\w$]*)")]
    private static partial Regex JsFunctionRegex();

    [GeneratedRegex(@"^\s*(?:export\s+)?(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*(?::[^=]+)?=\s*(?:async\s+)?(?:function\b|(?:\([^)]*\)|[A-Za-z_$][\w$]*)\s*(?::[^=]+)?=>)")]
    private static partial Regex JsArrowRegex();

    [GeneratedRegex(@"^\s+(?:(?:public|private|protected|static|async|readonly|override|abstract|get|set)\s+)*\*?\s*([A-Za-z_$#][\w$]*)\s*(?:<[^>]*>)?\s*\([^;]*\)\s*(?::\s*[^{;=]+)?\s*\{\s*$")]
    private static partial Regex JsMethodRegex();

    [GeneratedRegex(@"^(\s*)(?:async\s+)?(def|class)\s+([A-Za-z_]\w*)")]
    private static partial Regex PyRegex();

    [GeneratedRegex(@"^func\s+(?:\(\s*\w*\s*\*?(\w+)[^)]*\)\s*)?([A-Za-z_]\w*)")]
    private static partial Regex GoFuncRegex();

    [GeneratedRegex(@"^type\s+([A-Za-z_]\w*)\s+(struct|interface)?")]
    private static partial Regex GoTypeRegex();

    [GeneratedRegex(@"^\s*(?:pub(?:\([^)]*\))?\s+)?(?:(?:async|unsafe|const|extern(?:\s+""[^""]*"")?)\s+)*(fn|struct|enum|trait|impl|mod|type|union)\b(?:\s*<[^>]*>)?\s+([A-Za-z_][\w:<>, ]*?)(?:\s*[<({;:=]|\s+for\b|\s+where\b|\s*$)")]
    private static partial Regex RustRegex();

    [GeneratedRegex(@"^(?:(?:struct|class|enum|union|namespace)\s+([A-Za-z_]\w*))|^(?!\s)(?!(?:if|for|while|switch|return|else|do|case|typedef|#)\b)[A-Za-z_][\w\s\*&:<>,]*?[\s\*&]([A-Za-z_][\w:~]*)\s*\([^;]*$")]
    private static partial Regex CRegex();

    [GeneratedRegex(@"^\s*(?:(?:abstract|final|public|private|protected|static|readonly)\s+)*(class|interface|trait|enum|function)\s+&?([A-Za-z_]\w*)")]
    private static partial Regex PhpRegex();

    [GeneratedRegex(@"^(\s*)(class|module|def)\s+(?:self\.)?([A-Za-z_][\w:]*[?!=]?)")]
    private static partial Regex RubyRegex();

    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|internal|open|abstract|sealed|data|inline|suspend|override|operator|infix|enum|annotation|companion|value|tailrec)\s+)*(class|interface|object|fun)\s+(?:<[^>]*>\s*)?(?:[\w.]+\.)?([A-Za-z_]\w*)")]
    private static partial Regex KotlinRegex();

    [GeneratedRegex(@"^\s*(?:(?:public|private|fileprivate|internal|open|final|static|override|mutating|@\w+)\s+)*(class|struct|enum|protocol|extension|func|actor)\s+([A-Za-z_][\w.]*)")]
    private static partial Regex SwiftRegex();

    [GeneratedRegex(@"^\s*(?:function\s+([\w-]+)|([\w-]+)\s*\(\)\s*\{?)")]
    private static partial Regex ShellRegex();

    [GeneratedRegex(@"^\s*(function|filter|class|enum)\s+([\w-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PsRegex();

    [GeneratedRegex(@"^\s*(?:local\s+)?function\s+([\w.:]+)|^\s*(?:local\s+)?([\w.]+)\s*=\s*function\b")]
    private static partial Regex LuaRegex();

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex MdRegex();

    [GeneratedRegex(@"^(\s*)([^\s{}/@][^{};]*?)\s*\{\s*(?:$|[^}]*$)")]
    private static partial Regex CssRegex();

    [GeneratedRegex(@"^\s*@(media|keyframes|supports|layer|mixin|function|include)\b([^{;]*)")]
    private static partial Regex CssAtRegex();

    [GeneratedRegex(@"\bcreate\s+(?:or\s+(?:replace|alter)\s+)?(?:temp(?:orary)?\s+)?(table|view|function|procedure|proc|index|trigger|type|schema)\s+(?:if\s+not\s+exists\s+)?([\w.\[\]""`]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SqlRegex();

    [GeneratedRegex(@"^(\s*)(?:-\s+)?([\w.$""'\-\[\]]+)\s*[:=](?:\s|$)")]
    private static partial Regex YamlRegex();

    [GeneratedRegex(@"^\s*\[([^\]]+)\]")]
    private static partial Regex IniSectionRegex();

    [GeneratedRegex(@"<!--.*?-->|<(/?)([A-Za-z][\w:.-]*)((?:[^>""']|""[^""]*""|'[^']*')*?)(/?)>", RegexOptions.Singleline)]
    private static partial Regex XmlTagRegex();

    [GeneratedRegex(@"\b(?:id|x:Name|Name|name|x:Key|class)\s*=\s*""([^""]+)""")]
    private static partial Regex XmlIdRegex();

    private static readonly HashSet<string> VoidHtml = new(StringComparer.OrdinalIgnoreCase)
        { "br", "hr", "img", "input", "meta", "link", "area", "base", "col", "embed", "source", "track", "wbr" };

    /// <summary>Element hierarchy (a few levels deep), labelled with id / x:Name / class when present.</summary>
    private static List<OutlineItem> XmlOutline(List<string> lines)
    {
        const int MaxDepth = 5;
        var list = new List<OutlineItem>();
        var text = string.Join("\n", lines);
        if (text.Length > 2_000_000) return list;
        int depth = 0, line = 1, scanned = 0;
        foreach (Match m in XmlTagRegex().Matches(text))
        {
            if (!m.Groups[2].Success) continue; // comment
            for (; scanned < m.Index; scanned++) if (text[scanned] == '\n') line++;
            var name = m.Groups[2].Value;
            if (m.Groups[1].Value == "/") { depth = Math.Max(0, depth - 1); continue; }
            bool selfClosing = m.Groups[4].Value == "/" || VoidHtml.Contains(name);
            // Property elements (<Grid.RowDefinitions>) are structure noise, not symbols.
            if (depth < MaxDepth && !name.Contains('.'))
            {
                var id = XmlIdRegex().Match(m.Groups[3].Value);
                list.Add(new OutlineItem(SymbolKind.Element, id.Success ? $"{name} · {id.Groups[1].Value}" : name, line, depth));
                if (list.Count >= MaxOutline) break;
            }
            if (!selfClosing) depth++;
        }
        return list;
    }

    private static List<OutlineItem> Outline(List<string> lines, LanguageInfo lang)
    {
        var list = new List<OutlineItem>();
        var family = lang.Family;
        if (family == OutlineFamily.None) return list;
        if (family == OutlineFamily.Json) return JsonOutline(lines);
        if (family == OutlineFamily.Xml) return XmlOutline(lines);

        bool inFence = false;
        int unit = DetectIndentUnit(lines);
        for (int i = 0; i < lines.Count && list.Count < MaxOutline; i++)
        {
            var line = lines[i];
            if (line.Length > 400 || string.IsNullOrWhiteSpace(line)) continue;
            int depth = IndentOf(line) / unit;
            var t = line.TrimStart();
            if (family != OutlineFamily.Markdown && (t.StartsWith("//") || t.StartsWith("/*") || t.StartsWith("* ") || (t.StartsWith('#') && family is not OutlineFamily.C and not OutlineFamily.Css)))
                if (family is not (OutlineFamily.Yaml)) continue;

            void Add(SymbolKind k, string name, int d = -1)
            {
                name = name.Trim();
                if (name.Length == 0 || NotNames.Contains(name)) return;
                list.Add(new OutlineItem(k, name, i + 1, d < 0 ? depth : d));
            }

            Match m;
            switch (family)
            {
                case OutlineFamily.CSharp:
                case OutlineFamily.Java:
                    if ((m = CsTypeRegex().Match(line)).Success) Add(TypeKind(m.Groups[1].Value), m.Groups[2].Value);
                    else if ((m = CsMethodRegex.Match(line)).Success && !line.Contains(" new ") && !t.StartsWith("return")) Add(SymbolKind.Method, m.Groups[1].Value);
                    else if (family == OutlineFamily.CSharp && (m = CsPropertyRegex().Match(line)).Success) Add(SymbolKind.Property, m.Groups[1].Value);
                    break;
                case OutlineFamily.JavaScript:
                    if ((m = JsTypeRegex().Match(line)).Success) Add(TypeKind(m.Groups[1].Value), m.Groups[2].Value);
                    else if ((m = JsFunctionRegex().Match(line)).Success) Add(SymbolKind.Function, m.Groups[1].Value);
                    else if ((m = JsArrowRegex().Match(line)).Success) Add(SymbolKind.Function, m.Groups[1].Value);
                    else if ((m = JsMethodRegex().Match(line)).Success) Add(SymbolKind.Method, m.Groups[1].Value);
                    break;
                case OutlineFamily.Python:
                    if ((m = PyRegex().Match(line)).Success)
                        Add(m.Groups[2].Value == "class" ? SymbolKind.Class : depth > 0 ? SymbolKind.Method : SymbolKind.Function, m.Groups[3].Value);
                    break;
                case OutlineFamily.Go:
                    if ((m = GoFuncRegex().Match(line)).Success)
                        Add(m.Groups[1].Success ? SymbolKind.Method : SymbolKind.Function, m.Groups[1].Success ? $"{m.Groups[1].Value}.{m.Groups[2].Value}" : m.Groups[2].Value, 0);
                    else if ((m = GoTypeRegex().Match(line)).Success)
                        Add(m.Groups[2].Value == "interface" ? SymbolKind.Interface : SymbolKind.Struct, m.Groups[1].Value, 0);
                    break;
                case OutlineFamily.Rust:
                    if ((m = RustRegex().Match(line)).Success)
                        Add(m.Groups[1].Value switch
                        {
                            "fn" => depth > 0 ? SymbolKind.Method : SymbolKind.Function,
                            "struct" or "union" => SymbolKind.Struct,
                            "enum" => SymbolKind.Enum,
                            "trait" => SymbolKind.Interface,
                            "mod" => SymbolKind.Namespace,
                            "impl" => SymbolKind.Class,
                            _ => SymbolKind.Record,
                        }, (m.Groups[1].Value == "impl" ? "impl " : "") + m.Groups[2].Value);
                    break;
                case OutlineFamily.C:
                    if ((m = CRegex().Match(line)).Success)
                    {
                        if (m.Groups[1].Success) Add(t.StartsWith("namespace") ? SymbolKind.Namespace : SymbolKind.Struct, m.Groups[1].Value);
                        else if (!line.TrimEnd().EndsWith(';')) Add(SymbolKind.Function, m.Groups[2].Value);
                    }
                    break;
                case OutlineFamily.Php:
                    if ((m = PhpRegex().Match(line)).Success)
                        Add(m.Groups[1].Value == "function" ? (depth > 0 ? SymbolKind.Method : SymbolKind.Function) : TypeKind(m.Groups[1].Value), m.Groups[2].Value);
                    break;
                case OutlineFamily.Ruby:
                    if ((m = RubyRegex().Match(line)).Success)
                        Add(m.Groups[2].Value switch { "class" => SymbolKind.Class, "module" => SymbolKind.Namespace, _ => SymbolKind.Method }, m.Groups[3].Value);
                    break;
                case OutlineFamily.Kotlin:
                    if ((m = KotlinRegex().Match(line)).Success)
                        Add(m.Groups[1].Value == "fun" ? SymbolKind.Function : TypeKind(m.Groups[1].Value), m.Groups[2].Value);
                    break;
                case OutlineFamily.Swift:
                    if ((m = SwiftRegex().Match(line)).Success)
                        Add(m.Groups[1].Value switch { "func" => SymbolKind.Function, "protocol" => SymbolKind.Interface, "extension" => SymbolKind.Namespace, var k => TypeKind(k) }, m.Groups[2].Value);
                    break;
                case OutlineFamily.Shell:
                    if ((m = ShellRegex().Match(line)).Success && (m.Groups[1].Success || line.Contains("()")))
                        Add(SymbolKind.Function, m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
                    break;
                case OutlineFamily.PowerShell:
                    if ((m = PsRegex().Match(line)).Success)
                        Add(m.Groups[1].Value.ToLowerInvariant() switch { "class" => SymbolKind.Class, "enum" => SymbolKind.Enum, _ => SymbolKind.Function }, m.Groups[2].Value);
                    break;
                case OutlineFamily.Lua:
                    if ((m = LuaRegex().Match(line)).Success)
                        Add(SymbolKind.Function, m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
                    break;
                case OutlineFamily.Markdown:
                    if (t.StartsWith("```") || t.StartsWith("~~~")) { inFence = !inFence; break; }
                    if (!inFence && (m = MdRegex().Match(line)).Success)
                        Add(SymbolKind.Heading, m.Groups[2].Value, m.Groups[1].Value.Length - 1);
                    break;
                case OutlineFamily.Css:
                    if ((m = CssAtRegex().Match(line)).Success) Add(SymbolKind.Namespace, "@" + m.Groups[1].Value + " " + m.Groups[2].Value.Trim());
                    else if ((m = CssRegex().Match(line)).Success) Add(SymbolKind.Selector, m.Groups[2].Value);
                    break;
                case OutlineFamily.Sql:
                    if ((m = SqlRegex().Match(line)).Success)
                        Add(m.Groups[1].Value.ToLowerInvariant() switch { "table" => SymbolKind.Table, "view" => SymbolKind.Table, "index" => SymbolKind.Key, "schema" => SymbolKind.Namespace, _ => SymbolKind.Function }, m.Groups[2].Value, 0);
                    break;
                case OutlineFamily.Yaml:
                    if ((m = IniSectionRegex().Match(line)).Success) Add(SymbolKind.Namespace, m.Groups[1].Value, 0);
                    else if (depth <= 1 && !t.StartsWith('#') && !t.StartsWith(';') && (m = YamlRegex().Match(line)).Success) Add(SymbolKind.Key, m.Groups[2].Value.Trim('"', '\''));
                    break;
            }
        }
        // Normalise depths so the outermost symbol sits at 0.
        if (list.Count > 0)
        {
            int min = list.Min(o => o.Depth);
            if (min > 0) list = list.Select(o => o with { Depth = o.Depth - min }).ToList();
        }
        return list;
    }

    private static List<OutlineItem> JsonOutline(List<string> lines)
    {
        var list = new List<OutlineItem>();
        int depth = 0;
        for (int i = 0; i < lines.Count && list.Count < MaxOutline; i++)
        {
            var line = lines[i];
            bool inStr = false;
            int keyStart = -1;
            for (int j = 0; j < line.Length; j++)
            {
                char c = line[j];
                if (inStr)
                {
                    if (c == '\\') j++;
                    else if (c == '"')
                    {
                        inStr = false;
                        if (depth is 1 or 2)
                        {
                            int k = j + 1;
                            while (k < line.Length && char.IsWhiteSpace(line[k])) k++;
                            if (k < line.Length && line[k] == ':')
                                list.Add(new OutlineItem(SymbolKind.Key, line[(keyStart + 1)..j], i + 1, depth - 1));
                        }
                    }
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; keyStart = j; break;
                    case '{' or '[': depth++; break;
                    case '}' or ']': depth = Math.Max(0, depth - 1); break;
                }
            }
        }
        return list;
    }

    private static SymbolKind TypeKind(string keyword) => keyword.Split(' ')[0] switch
    {
        "class" or "object" => SymbolKind.Class,
        "interface" or "trait" or "protocol" => SymbolKind.Interface,
        "struct" => SymbolKind.Struct,
        "enum" => SymbolKind.Enum,
        "record" or "type" => SymbolKind.Record,
        "namespace" or "module" => SymbolKind.Namespace,
        _ => SymbolKind.Class,
    };

    private static int IndentOf(string line)
    {
        int n = 0;
        foreach (var c in line)
        {
            if (c == ' ') n++;
            else if (c == '\t') n += 4;
            else break;
        }
        return n;
    }

    private static int DetectIndentUnit(List<string> lines)
    {
        int min = int.MaxValue;
        foreach (var l in lines.Take(3000))
        {
            int n = IndentOf(l);
            if (n > 0 && n < min && l.Trim().Length > 0) min = n;
        }
        return min is int.MaxValue or > 8 ? 4 : Math.Max(2, min);
    }
}
