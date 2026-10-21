using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using TextPeach.Services;

namespace TextPeach.Controls;

/// <summary>
/// Installs TextMate highlighting on an editor. TextMate colors lines in the background and only repaints the lines it
/// believes are on screen; right after a document is set (or filled by an async load) that belief is stale, so the
/// first screen stays white until the user scrolls. A few repaints while the tokenizer catches up fix it.
/// </summary>
public static class SyntaxColoring
{
    private static readonly int[] RepaintAfterMs = { 50, 150, 350, 700, 1200, 2500 };

    public static TextMate.Installation Install(TextEditor editor, string? scope)
    {
        var tm = editor.InstallTextMate(Highlighting.Instance);
        tm.SetTheme(Highlighting.Instance.Theme);
        if (scope is not null) tm.SetGrammar(scope);
        RepaintSoon(editor);
        return tm;
    }

    /// <summary>Repaints the visible lines a few times over the next seconds (cheap: only what is on screen).</summary>
    public static void RepaintSoon(TextEditor editor)
    {
        var doc = editor.Document;
        foreach (var ms in RepaintAfterMs)
        {
            DispatcherTimer.RunOnce(() =>
            {
                // Another file was shown meanwhile: its own install schedules its repaints.
                if (ReferenceEquals(editor.Document, doc)) editor.TextArea.TextView.Redraw();
            }, TimeSpan.FromMilliseconds(ms));
        }
    }
}
