using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Renderers.Html;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TextPeach.Services;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace TextPeach.Controls;

/// <summary>A rendered document plus where each source line ended up, so the outline can scroll to it.</summary>
public sealed class RenderedDocument
{
    public required Control Root { get; init; }
    /// <summary>(1-based source line, control) in document order.</summary>
    public List<(int Line, Control Control)> Blocks { get; } = new();
    public Dictionary<string, Control> Anchors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Control? BlockAt(int line)
    {
        Control? best = null;
        foreach (var (l, c) in Blocks)
        {
            if (l > line) break;
            best = c;
        }
        return best ?? Blocks.FirstOrDefault().Control;
    }
}

/// <summary>
/// Builds native Avalonia controls for the "Renderizada" view: Markdown via Markdig, plain text as wrapped paragraphs.
/// Everything is read-only; text can be selected and copied but never looks like an input.
/// </summary>
public static class DocumentRenderer
{
    private const double BodySize = 14, BodyLineHeight = 22, CodeSize = 12.5;
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New");
    private static readonly IBrush LinkBrush = Palette.FromHex("#7db4ea");
    private static readonly IBrush InlineCodeBg = Palette.Panel2;
    private static readonly IBrush InlineCodeFg = Palette.Amber;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseGridTables().UseTaskLists().UseAutoLinks().UseEmphasisExtras().UseAutoIdentifiers()
        .Build();

    private sealed record Ctx(string BaseDir, Action<string> OnLink, RenderedDocument Doc, bool Muted = false);

    public static RenderedDocument Render(RenderKind kind, string text, string fileDir, Action<string> onLink)
    {
        var body = new StackPanel { Spacing = 0 };
        var doc = new RenderedDocument
        {
            Root = new Border
            {
                Padding = new Thickness(40, 28, 40, 60),
                MaxWidth = 920,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = body,
            },
        };
        var ctx = new Ctx(fileDir, onLink, doc);
        if (kind == RenderKind.Markdown)
        {
            var md = Markdown.Parse(text, Pipeline);
            foreach (var block in md) AddBlock(body.Children, block, ctx);
        }
        else
        {
            RenderPlain(body.Children, text, ctx);
        }
        if (body.Children.Count == 0)
            body.Children.Add(new TextBlock { Text = "Documento vacío", Foreground = Palette.Dim, FontStyle = FontStyle.Italic });
        return doc;
    }

    // ------------------------------------------------------------ plain text

    private static void RenderPlain(Avalonia.Controls.Controls target, string text, Ctx ctx)
    {
        var lines = FileAnalyzer.SplitLines(text, out _, out _);
        var para = new StringBuilder();
        int start = 0;
        void Flush()
        {
            if (para.Length == 0) return;
            var tb = Paragraph(ctx);
            tb.Text = para.ToString().TrimEnd('\n');
            tb.Margin = new Thickness(0, 0, 0, 12);
            target.Add(tb);
            ctx.Doc.Blocks.Add((start + 1, tb));
            para.Clear();
        }
        for (int i = 0; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) { Flush(); continue; }
            if (para.Length == 0) start = i;
            para.Append(lines[i]).Append('\n');
        }
        Flush();
    }

    // ------------------------------------------------------------ blocks

    private static SelectableTextBlock Paragraph(Ctx ctx) => new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = BodySize,
        LineHeight = BodyLineHeight,
        Foreground = ctx.Muted ? Palette.Dim : Palette.Text,
        SelectionBrush = Palette.Selection,
    };

    private static void Track(Control c, MdBlock b, Ctx ctx) => ctx.Doc.Blocks.Add((b.Line + 1, c));

    private static void AddBlock(Avalonia.Controls.Controls target, MdBlock block, Ctx ctx)
    {
        switch (block)
        {
            case HeadingBlock h:
            {
                var tb = Paragraph(ctx);
                (tb.FontSize, tb.LineHeight, var top) = h.Level switch
                {
                    1 => (26.0, 34.0, 8.0),
                    2 => (21.0, 28.0, 22.0),
                    3 => (17.0, 24.0, 18.0),
                    4 => (15.0, 22.0, 14.0),
                    _ => (14.0, 20.0, 12.0),
                };
                tb.FontWeight = FontWeight.SemiBold;
                if (h.Level >= 5) tb.Foreground = Palette.Dim;
                AddInlines(tb.Inlines!, h.Inline, ctx, Style.Normal);
                Control c = tb;
                if (h.Level <= 2)
                {
                    c = new Border
                    {
                        BorderBrush = Palette.Border,
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Padding = new Thickness(0, 0, 0, 6),
                        Child = tb,
                    };
                }
                c.Margin = new Thickness(0, target.Count == 0 ? 0 : top, 0, 10);
                target.Add(c);
                Track(c, h, ctx);
                if (h.GetAttributes().Id is { } id) ctx.Doc.Anchors.TryAdd(id, c);
                break;
            }
            case ParagraphBlock p:
            {
                var tb = Paragraph(ctx);
                AddInlines(tb.Inlines!, p.Inline, ctx, Style.Normal);
                // A paragraph that is just an image: show the image, not an empty line.
                var only = p.Inline?.FirstChild;
                if (only is LinkInline { IsImage: true } img && only.NextSibling is null && LoadImage(img.Url, ctx) is { } image)
                {
                    image.Margin = new Thickness(0, 0, 0, 14);
                    target.Add(image);
                    Track(image, p, ctx);
                    break;
                }
                tb.Margin = new Thickness(0, 0, 0, 12);
                target.Add(tb);
                Track(tb, p, ctx);
                break;
            }
            case ListBlock list:
                target.Add(RenderList(list, ctx));
                break;
            case QuoteBlock q:
            {
                var inner = new StackPanel();
                foreach (var b in q) AddBlock(inner.Children, b, ctx with { Muted = true });
                var border = new Border
                {
                    BorderBrush = Palette.Border,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(14, 2, 0, 0),
                    Margin = new Thickness(0, 0, 0, 12),
                    Child = inner,
                };
                target.Add(border);
                Track(border, q, ctx);
                break;
            }
            case FencedCodeBlock or CodeBlock:
            {
                var cb = (LeafBlock)block;
                var code = cb.Lines.ToString().TrimEnd('\n', '\r');
                var lang = (cb as FencedCodeBlock)?.Info;
                var c = CodeBox(code, lang);
                target.Add(c);
                Track(c, cb, ctx);
                break;
            }
            case ThematicBreakBlock:
                target.Add(new Border { Height = 1, Background = Palette.Border, Margin = new Thickness(0, 10, 0, 22) });
                break;
            case Table table:
            {
                var t = RenderTable(table, ctx);
                target.Add(t);
                Track(t, table, ctx);
                break;
            }
            case HtmlBlock html:
            {
                // Raw HTML is shown as-is (dim), never executed.
                var tb = new SelectableTextBlock
                {
                    Text = html.Lines.ToString().TrimEnd(), FontFamily = Mono, FontSize = CodeSize,
                    Foreground = Palette.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
                };
                target.Add(tb);
                Track(tb, html, ctx);
                break;
            }
            case LinkReferenceDefinitionGroup:
                break;
            case ContainerBlock container:
                foreach (var b in container) AddBlock(target, b, ctx);
                break;
            case LeafBlock leaf when leaf.Inline is not null:
            {
                var tb = Paragraph(ctx);
                AddInlines(tb.Inlines!, leaf.Inline, ctx, Style.Normal);
                tb.Margin = new Thickness(0, 0, 0, 12);
                target.Add(tb);
                break;
            }
        }
    }

    private static Control CodeBox(string code, string? lang)
    {
        var text = new SelectableTextBlock
        {
            Text = code, FontFamily = Mono, FontSize = CodeSize, LineHeight = 19,
            Foreground = Palette.Text, SelectionBrush = Palette.Selection,
        };
        var panel = new Panel();
        panel.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = text,
        });
        if (!string.IsNullOrWhiteSpace(lang))
            panel.Children.Add(new TextBlock
            {
                Text = lang, FontSize = 10, Foreground = Palette.Dim,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -6, -6, 0),
            });
        return new Border
        {
            Background = Palette.Code,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 0, 0, 14),
            Child = panel,
        };
    }

    private static Control RenderList(ListBlock list, Ctx ctx)
    {
        var panel = new StackPanel { Spacing = list.IsLoose ? 6 : 2, Margin = new Thickness(0, 0, 0, 12) };
        int n = int.TryParse(list.OrderedStart, out var s) ? s : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var content = new StackPanel();
            foreach (var b in item) AddBlock(content.Children, b, ctx);
            // Inside lists, paragraphs and nested lists don't need their own bottom gap.
            foreach (var c in content.Children) if (c is not Border) c.Margin = new Thickness(c.Margin.Left, c.Margin.Top, c.Margin.Right, 0);
            bool task = item.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: TaskList };
            var marker = new TextBlock
            {
                Text = task ? "" : list.IsOrdered ? $"{n}{list.OrderedDelimiter}" : "•",
                Foreground = Palette.Dim,
                FontSize = BodySize,
                LineHeight = BodyLineHeight,
                MinWidth = task ? 0 : 22,
                Margin = new Thickness(task ? 0 : 4, 0, task ? 0 : 4, 0),
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            row.Children.Add(marker);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            panel.Children.Add(row);
            ctx.Doc.Blocks.Add((item.Line + 1, row));
            n++;
        }
        return panel;
    }

    private static Control RenderTable(Table table, Ctx ctx)
    {
        var grid = new Grid();
        int cols = table.ColumnDefinitions.Count;
        if (cols == 0) cols = table.OfType<TableRow>().Select(r => r.Count).DefaultIfEmpty(1).Max();
        // Narrow columns size to content; the last one takes the remaining width and wraps.
        for (int c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(c == cols - 1 ? GridLength.Star : GridLength.Auto));
        int r = 0;
        foreach (var row in table.OfType<TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            int c = 0;
            foreach (var cell in row.OfType<TableCell>())
            {
                if (c >= cols) break;
                var tb = Paragraph(ctx);
                tb.TextWrapping = TextWrapping.Wrap;
                if (c < cols - 1) tb.MaxWidth = 340;
                if (row.IsHeader) tb.FontWeight = FontWeight.SemiBold;
                foreach (var b in cell)
                    if (b is LeafBlock { Inline: { } inl }) AddInlines(tb.Inlines!, inl, ctx, Style.Normal);
                var align = c < table.ColumnDefinitions.Count ? table.ColumnDefinitions[c].Alignment : null;
                tb.TextAlignment = align switch { TableColumnAlign.Center => TextAlignment.Center, TableColumnAlign.Right => TextAlignment.Right, _ => TextAlignment.Left };
                var border = new Border
                {
                    BorderBrush = Palette.Border,
                    BorderThickness = new Thickness(c == 0 ? 1 : 0, r == 0 ? 1 : 0, 1, 1),
                    Background = row.IsHeader ? Palette.Panel : Brushes.Transparent,
                    Padding = new Thickness(10, 5),
                    Child = tb,
                };
                Grid.SetRow(border, r);
                Grid.SetColumn(border, c);
                grid.Children.Add(border);
                c++;
            }
            // Keep the grid lines continuous when a row has fewer cells.
            for (; c < cols; c++)
            {
                var filler = new Border { BorderBrush = Palette.Border, BorderThickness = new Thickness(0, r == 0 ? 1 : 0, 1, 1) };
                Grid.SetRow(filler, r);
                Grid.SetColumn(filler, c);
                grid.Children.Add(filler);
            }
            r++;
        }
        grid.Margin = new Thickness(0, 0, 0, 14);
        return grid;
    }

    // ------------------------------------------------------------ inlines

    [Flags]
    private enum Style { Normal = 0, Bold = 1, Italic = 2, Strike = 4 }

    private static void AddInlines(InlineCollection target, ContainerInline? container, Ctx ctx, Style style)
    {
        if (container is null) return;
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
            AddInline(target, inline, ctx, style);
    }

    private static Run StyledRun(string text, Style style)
    {
        var run = new Run(text);
        if (style.HasFlag(Style.Bold)) run.FontWeight = FontWeight.SemiBold;
        if (style.HasFlag(Style.Italic)) run.FontStyle = FontStyle.Italic;
        if (style.HasFlag(Style.Strike)) run.TextDecorations = TextDecorations.Strikethrough;
        return run;
    }

    private static void AddInline(InlineCollection target, MdInline inline, Ctx ctx, Style style)
    {
        switch (inline)
        {
            case LiteralInline lit:
                target.Add(StyledRun(lit.Content.ToString(), style));
                break;
            case EmphasisInline em:
            {
                var s = style;
                if (em.DelimiterChar == '~') s |= Style.Strike;
                else if (em.DelimiterCount >= 2) s |= Style.Bold;
                else s |= Style.Italic;
                AddInlines(target, em, ctx, s);
                break;
            }
            case CodeInline code:
                target.Add(new Run(" " + code.Content + " ")
                {
                    FontFamily = Mono, FontSize = CodeSize, Background = InlineCodeBg, Foreground = InlineCodeFg,
                });
                break;
            case LineBreakInline br:
                if (br.IsHard) target.Add(new LineBreak());
                else target.Add(new Run(" "));
                break;
            case TaskList task:
                target.Add(new Run(task.Checked ? "☑ " : "☐ ") { Foreground = task.Checked ? Palette.Green : Palette.Dim });
                break;
            case AutolinkInline auto:
                target.Add(Link(auto.Url, auto.IsEmail ? "mailto:" + auto.Url : auto.Url, ctx, style));
                break;
            case LinkInline { IsImage: true } img:
                if (LoadImage(img.Url, ctx) is { } image) target.Add(new InlineUIContainer(image));
                else target.Add(new Run("[" + PlainText(img) + "]") { Foreground = Palette.Dim });
                break;
            case LinkInline link:
            {
                var label = PlainText(link);
                target.Add(Link(label.Length > 0 ? label : link.Url ?? "", link.Url ?? "", ctx, style));
                break;
            }
            case HtmlEntityInline entity:
                target.Add(StyledRun(entity.Transcoded.ToString(), style));
                break;
            case HtmlInline html:
                // <br> is common inside tables; other tags are dropped.
                if (html.Tag.StartsWith("<br", StringComparison.OrdinalIgnoreCase)) target.Add(new LineBreak());
                break;
            case ContainerInline c:
                AddInlines(target, c, ctx, style);
                break;
        }
    }

    private static InlineUIContainer Link(string text, string url, Ctx ctx, Style style)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = LinkBrush,
            TextDecorations = TextDecorations.Underline,
            FontSize = BodySize,
            Cursor = new Cursor(StandardCursorType.Hand),
            FontWeight = style.HasFlag(Style.Bold) ? FontWeight.SemiBold : FontWeight.Normal,
            FontStyle = style.HasFlag(Style.Italic) ? FontStyle.Italic : FontStyle.Normal,
        };
        ToolTip.SetTip(tb, url);
        tb.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left) ctx.OnLink(url);
        };
        return new InlineUIContainer(tb) { BaselineAlignment = BaselineAlignment.TextBottom };
    }

    private static string PlainText(ContainerInline c)
    {
        var sb = new StringBuilder();
        void Walk(ContainerInline ci)
        {
            for (var i = ci.FirstChild; i is not null; i = i.NextSibling)
            {
                switch (i)
                {
                    case LiteralInline l: sb.Append(l.Content.ToString()); break;
                    case CodeInline code: sb.Append(code.Content); break;
                    case ContainerInline nested: Walk(nested); break;
                }
            }
        }
        Walk(c);
        return sb.ToString();
    }

    private static Image? LoadImage(string? url, Ctx ctx)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Contains("://")) return null;
        try
        {
            var path = Path.GetFullPath(Path.Combine(ctx.BaseDir, Uri.UnescapeDataString(url.Split('#', '?')[0]).Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(path) || !Languages.IsImage(path)) return null;
            var bmp = new Bitmap(path);
            return new Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                MaxWidth = bmp.PixelSize.Width,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
        }
        catch { return null; }
    }
}
