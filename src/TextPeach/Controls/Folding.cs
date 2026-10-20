using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using TextPeach.Services;

namespace TextPeach.Controls;

/// <summary>Fold regions for the viewer: braces for C-like files, indentation for Python/YAML, tags for XML.</summary>
public static class Folding
{
    private const int MaxChars = 3_000_000;

    public static IEnumerable<NewFolding> Compute(TextDocument doc, LanguageInfo lang)
    {
        if (doc.TextLength == 0 || doc.TextLength > MaxChars) return Array.Empty<NewFolding>();
        try
        {
            return lang.Family switch
            {
                OutlineFamily.Xml => Xml(doc),
                OutlineFamily.Python or OutlineFamily.Yaml => Indent(doc),
                OutlineFamily.Markdown => Headings(doc),
                OutlineFamily.None when lang.LineComments.Length == 0 && lang.BlockComments.Length == 0 => Array.Empty<NewFolding>(),
                OutlineFamily.Ruby or OutlineFamily.Lua => Indent(doc),
                _ => Braces(doc),
            };
        }
        catch { return Array.Empty<NewFolding>(); }
    }

    private static IEnumerable<NewFolding> Xml(TextDocument doc)
    {
        var s = new XmlFoldingStrategy { ShowAttributesWhenFolded = true };
        return s.CreateNewFoldings(doc, out _);
    }

    private static IEnumerable<NewFolding> Braces(TextDocument doc)
    {
        var list = new List<NewFolding>();
        var stack = new Stack<int>();
        var text = doc.Text;
        char quote = '\0';
        bool lineComment = false, blockComment = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (lineComment) { if (c == '\n') lineComment = false; continue; }
            if (blockComment) { if (c == '*' && next == '/') { blockComment = false; i++; } continue; }
            if (quote != '\0')
            {
                if (c == '\\') i++;
                else if (c == quote || (c == '\n' && quote != '`')) quote = '\0';
                continue;
            }
            switch (c)
            {
                case '/' when next == '/': lineComment = true; i++; break;
                case '/' when next == '*': blockComment = true; i++; break;
                case '"' or '\'' or '`': quote = c; break;
                case '{' or '[': stack.Push(i); break;
                case '}' or ']':
                    if (stack.Count == 0) break;
                    int start = stack.Pop();
                    if (doc.GetLineByOffset(start).LineNumber < doc.GetLineByOffset(i).LineNumber)
                        list.Add(new NewFolding(start, i + 1));
                    break;
            }
        }
        list.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return list;
    }

    private static IEnumerable<NewFolding> Indent(TextDocument doc)
    {
        var list = new List<NewFolding>();
        var stack = new Stack<(int Indent, DocumentLine Line)>();
        DocumentLine? lastContent = null;
        foreach (var line in doc.Lines)
        {
            var text = doc.GetText(line);
            if (string.IsNullOrWhiteSpace(text)) continue;
            int indent = 0;
            while (indent < text.Length && (text[indent] == ' ' || text[indent] == '\t')) indent += text[indent] == '\t' ? 4 : 1;
            while (stack.Count > 0 && indent <= stack.Peek().Indent)
            {
                var (_, startLine) = stack.Pop();
                if (lastContent is not null && lastContent.LineNumber > startLine.LineNumber)
                    list.Add(new NewFolding(startLine.EndOffset, lastContent.EndOffset));
            }
            stack.Push((indent, line));
            lastContent = line;
        }
        while (stack.Count > 0 && lastContent is not null)
        {
            var (_, startLine) = stack.Pop();
            if (lastContent.LineNumber > startLine.LineNumber)
                list.Add(new NewFolding(startLine.EndOffset, lastContent.EndOffset));
        }
        list.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return list;
    }

    private static IEnumerable<NewFolding> Headings(TextDocument doc)
    {
        var list = new List<NewFolding>();
        var open = new List<(int Level, DocumentLine Line)>();
        DocumentLine? last = null;
        bool fence = false;
        foreach (var line in doc.Lines)
        {
            var text = doc.GetText(line);
            if (text.TrimStart().StartsWith("```")) fence = !fence;
            int level = 0;
            if (!fence) while (level < text.Length && text[level] == '#') level++;
            if (level is > 0 and <= 6 && text.Length > level && text[level] == ' ')
            {
                for (int k = open.Count - 1; k >= 0; k--)
                {
                    if (open[k].Level < level) break;
                    if (last is not null && last.LineNumber > open[k].Line.LineNumber)
                        list.Add(new NewFolding(open[k].Line.EndOffset, last.EndOffset));
                    open.RemoveAt(k);
                }
                open.Add((level, line));
            }
            if (!string.IsNullOrWhiteSpace(text)) last = line;
        }
        foreach (var (_, l) in open)
            if (last is not null && last.LineNumber > l.LineNumber) list.Add(new NewFolding(l.EndOffset, last.EndOffset));
        list.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return list;
    }
}
