using System.Text;
using AvaloniaEdit.Document;
using TextPeach.Controls;
using TextPeach.Services;
using TextPeach.Views;

namespace TextPeach.Tests;

public class AnalyzerTests
{
    private static FileAnalysis Analyze(string text, string file) => FileAnalyzer.Analyze(text, Languages.For(file));

    [Fact]
    public void CountsCodeCommentsAndBlankLines()
    {
        const string src = "// header\nusing System;\n\n/* block\n   still */\nclass A { }\n";
        var a = Analyze(src, "a.cs");
        Assert.Equal(6, a.Lines);
        Assert.Equal(3, a.Comment);
        Assert.Equal(1, a.Blank);
        Assert.Equal((6, 1), FileAnalyzer.CountLines(src)); // summary and per-file analysis must agree
        Assert.Equal(2, a.Code);
        Assert.Equal("LF", a.LineEnding);
    }

    [Fact]
    public void DetectsMixedLineEndingsAndIndentation()
    {
        var a = Analyze("a\r\nb\n\tc\n    d\n    e\n    f\n", "x.txt");
        Assert.True(a.LineEndingMixed);
        Assert.True(a.IndentMixed);
    }

    [Fact]
    public void CSharpOutlineFindsTypesMethodsAndProperties()
    {
        var src = """
            namespace Demo;

            public sealed class Repo : IDisposable
            {
                public string Name { get; set; } = "";
                private readonly List<int> _items = new();

                public async Task<int> LoadAsync(string path)
                {
                    if (path.Length > 0) return 1;
                    return 0;
                }

                public void Dispose() { }
            }

            public interface IThing { }
            internal enum Mode { A, B }
            """;
        var names = Analyze(src, "Repo.cs").Outline.Select(o => (o.Kind, o.Name)).ToList();
        Assert.Contains((SymbolKind.Namespace, "Demo"), names);
        Assert.Contains((SymbolKind.Class, "Repo"), names);
        Assert.Contains((SymbolKind.Property, "Name"), names);
        Assert.Contains((SymbolKind.Method, "LoadAsync"), names);
        Assert.Contains((SymbolKind.Method, "Dispose"), names);
        Assert.Contains((SymbolKind.Interface, "IThing"), names);
        Assert.Contains((SymbolKind.Enum, "Mode"), names);
        Assert.DoesNotContain(names, n => n.Name is "_items" or "if" or "new");
    }

    [Fact]
    public void TypeScriptOutline()
    {
        var src = """
            export interface Props { id: string }
            export function render(p: Props) { }
            export const useThing = async (x: number) => x;
            class Store {
              async load(id: string): Promise<void> {
              }
              if (x) {
              }
            }
            """;
        var names = Analyze(src, "a.ts").Outline.Select(o => o.Name).ToList();
        Assert.Equal(new[] { "Props", "render", "useThing", "Store", "load" }, names);
    }

    [Fact]
    public void PythonOutlineNestsMethods()
    {
        var o = Analyze("class A:\n    def run(self):\n        pass\n\ndef main():\n    pass\n", "a.py").Outline;
        Assert.Equal(new[] { SymbolKind.Class, SymbolKind.Method, SymbolKind.Function }, o.Select(x => x.Kind));
        Assert.Equal(1, o[1].Depth);
    }

    [Fact]
    public void MarkdownHeadingsIgnoreFencedCode()
    {
        var o = Analyze("# Title\ntext\n```\n# not a heading\n```\n## Sub\n", "README.md").Outline;
        Assert.Equal(new[] { "Title", "Sub" }, o.Select(x => x.Name));
        Assert.Equal(1, o[1].Depth);
    }

    [Fact]
    public void JsonOutlineListsTopLevelKeys()
    {
        var o = Analyze("{\n  \"name\": \"x\",\n  \"scripts\": {\n    \"build\": \"tsc\"\n  },\n  \"arr\": [ { \"deep\": 1 } ]\n}", "package.json").Outline;
        Assert.Contains(o, x => x.Name == "name" && x.Depth == 0);
        Assert.Contains(o, x => x.Name == "build" && x.Depth == 1);
    }

    [Fact]
    public void FindsMarkers()
    {
        var m = Analyze("// TODO: refactor this\nint x; // FIXME(ana) - overflow\n# NOTE ok", "a.cs").Markers;
        Assert.Equal(new[] { "TODO", "FIXME", "NOTE" }, m.Select(x => x.Tag));
        Assert.Equal("refactor this", m[0].Text);
        Assert.Equal("overflow", m[1].Text);
        Assert.Equal(2, m[1].Line);
    }

    [Fact]
    public void DecodeDetectsBomUtf8Latin1AndBinary()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' };
        Assert.Equal(("hi", "UTF-8 BOM"), (FileLoader.Decode(bom).Text, FileLoader.Decode(bom).Name));
        Assert.Equal("UTF-8", FileLoader.Decode(Encoding.UTF8.GetBytes("ñandú")).Name);
        var latin = FileLoader.Decode(new byte[] { (byte)'a', 0xF1, (byte)'o' });
        Assert.Equal(("año", "Latin-1"), (latin.Text, latin.Name));
        Assert.Null(FileLoader.Decode(new byte[] { 1, 0, 2 }).Text);
    }

    [Fact]
    public void RankPathsPrefersFileNameMatches()
    {
        var paths = new[] { "src/views/MainView.cs", "docs/main.md", "src/Main.cs", "src/domain/Remain.cs" };
        var r = Dialogs.RankPaths(paths, "main", 10);
        Assert.Equal(new[] { "src/Main.cs", "docs/main.md" }, r.Take(2)); // name prefix; shorter path wins ties
        Assert.Equal("src/domain/Remain.cs", r[^1]);
    }

    [Fact]
    public void BraceFoldingSkipsStringsAndComments()
    {
        var doc = new TextDocument("class A\n{\n  var s = \"{\";\n  // }\n  void M()\n  {\n  }\n}\n");
        var folds = Folding.Compute(doc, Languages.For("a.cs")).ToList();
        Assert.Equal(2, folds.Count);
        Assert.Equal(doc.Text.IndexOf('{'), folds[0].StartOffset);
    }

    [Fact]
    public void IndentFoldingForPython()
    {
        var doc = new TextDocument("def a():\n    x = 1\n    y = 2\n\ndef b():\n    pass\n");
        Assert.Equal(2, Folding.Compute(doc, Languages.For("a.py")).Count());
    }

    [Fact]
    public void HighlightingResolvesCommonScopes()
    {
        Assert.Equal("source.cs", Highlighting.Instance.ScopeFor("a.cs"));
        Assert.Equal("source.ts", Highlighting.Instance.ScopeFor("a.ts"));
        Assert.Equal("text.xml", Highlighting.Instance.ScopeFor("App.axaml"));
        Assert.NotNull(Highlighting.Instance.Theme);
    }
}

public class XmlOutlineTests
{
    [Fact]
    public void XamlOutlineShowsElementHierarchyAndNames()
    {
        var src = "<Window x:Class=\"A\">\n  <Window.KeyBindings>\n    <KeyBinding Gesture=\"F1\" />\n  </Window.KeyBindings>\n  <DockPanel>\n    <!-- <Fake/> -->\n    <ListBox x:Name=\"Tabs\" />\n  </DockPanel>\n</Window>";
        var o = TextPeach.Services.FileAnalyzer.Analyze(src, TextPeach.Services.Languages.For("MainWindow.axaml")).Outline;
        Assert.Equal(new[] { "Window", "KeyBinding", "DockPanel", "ListBox · Tabs" }, o.Select(x => x.Name));
        Assert.Equal(new[] { 0, 2, 1, 2 }, o.Select(x => x.Depth));
        Assert.Equal(7, o[^1].Line);
    }
}

public class ProseMarkerTests
{
    [Fact]
    public void MarkdownOnlyCountsLineLeadingMarkers()
    {
        var md = "Se dejan como se usan (stage, TODO).\nUsa `FIXME` en el código.\n- TODO: revisar tablas\n> NOTE nota\n";
        var m = TextPeach.Services.FileAnalyzer.Analyze(md, TextPeach.Services.Languages.For("README.md")).Markers;
        Assert.Equal(new[] { ("TODO", 3), ("NOTE", 4) }, m.Select(x => (x.Tag, x.Line)));
    }
}
