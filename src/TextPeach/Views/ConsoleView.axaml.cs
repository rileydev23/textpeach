using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Iciclecreek.Terminal;
using TextPeach.Controls;
using TextPeach.Services;
using TextPeach.ViewModels;
using XTerm.Options;

namespace TextPeach.Views;

/// <summary>
/// A project's console: its bar and its terminals. One per <see cref="ConsoleViewModel"/>, kept alive while the
/// project is open (see <see cref="For"/>) and moved whole between the panel below the code, the page and a window
/// of its own, so the shells never notice they changed place.
/// </summary>
public partial class ConsoleView : UserControl
{
    private static readonly Dictionary<ConsoleViewModel, ConsoleView> Views = new();

    /// <summary>The view of a console, created the first time it is asked for.</summary>
    public static ConsoleView For(ConsoleViewModel console)
    {
        if (!Views.TryGetValue(console, out var view)) Views[console] = view = new ConsoleView(console);
        return view;
    }

    private const string MonoFonts = "Cascadia Mono, Consolas, Courier New, DejaVu Sans Mono, Liberation Mono";

    public ConsoleViewModel Console { get; }
    private readonly Dictionary<TerminalViewModel, (Control Pane, TerminalControl Terminal)> _panes = new();
    private Window? _window;

    private ConsoleView(ConsoleViewModel console)
    {
        InitializeComponent();
        Console = console;
        DataContext = console;
        foreach (var t in console.Terminals) AddPane(t);
        Rebuild();
        console.Terminals.CollectionChanged += OnTerminalsChanged;
        console.PropertyChanged += OnConsoleChanged;
        console.FocusRequested += FocusTerminal;
        console.Disposed += OnDisposed;
        ShellButton.Click += (_, _) => MenuPresenter.Show(ShellButton, Console.ShellMenu());
        // Ctrl+J hides the panel even from inside a shell (which would otherwise take it as a line feed).
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.J && e.KeyModifiers == KeyModifiers.Control) { Console.RequestToggle(); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Puts the console in <paramref name="host"/>, taking it out of wherever it was.</summary>
    public void PlaceIn(Border host)
    {
        if (ReferenceEquals(host.Child, this)) return;
        Detach();
        host.Child = this;
    }

    private void Detach()
    {
        switch (Parent)
        {
            case Border b: b.Child = null; break;
            case Window w: w.Content = null; break;
        }
    }

    // ------------------------------------------------------------ terminals

    private void OnTerminalsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var t in _panes.Keys.Where(t => !Console.Terminals.Contains(t)).ToList()) RemovePane(t);
        foreach (var t in Console.Terminals.Where(t => !_panes.ContainsKey(t)).ToList()) AddPane(t);
        Rebuild();
    }

    private void AddPane(TerminalViewModel t)
    {
        var term = new TerminalControl
        {
            Process = t.Shell.Path,
            Args = t.Shell.Args,
            StartingDirectory = t.StartDirectory,
            BufferSize = 5000,
            Background = Palette.Code,
            Foreground = Palette.Text,
            SelectionBrush = new SolidColorBrush(Color.Parse("#33507a")),
            FontFamily = new FontFamily(MonoFonts),
            FontSize = 13,
            Options = new TerminalOptions { Theme = Theme(), CursorBlink = true, Scrollback = 5000 },
            Margin = new Thickness(6, 4, 0, 0),
        };
        // Leaving the visual tree normally ends the shell; here it only means the console moved (or hid).
        term.TemplateApplied += (_, _) => term.BeginReparent();
        term.ProcessExited += (_, _) => Dispatcher.UIThread.Post(() => Console.Remove(t));

        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        title.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(TerminalViewModel.Title)) { Source = t });
        var close = new Button
        {
            Classes = { "flat" }, Content = "✕", FontSize = 9, Padding = new Thickness(5, 1), Foreground = Palette.Dim,
            VerticalAlignment = VerticalAlignment.Center, Command = t.CloseCommand,
        };
        ToolTip.SetTip(close, Loc.T("console.close.tip"));
        var head = new Border
        {
            Classes = { "termhead" },
            Child = new DockPanel { Children = { Dock(close, Avalonia.Controls.Dock.Right), title } },
        };
        ToolTip.SetTip(head, t.StartDirectory);
        head.Classes.Set("active", t.IsActive);
        t.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TerminalViewModel.IsActive)) head.Classes.Set("active", t.IsActive);
        };
        head.PointerPressed += (_, _) => { Console.Activate(t); term.Focus(); };

        var pane = new DockPanel { Background = Palette.Code, Children = { Dock(head, Avalonia.Controls.Dock.Top), term } };
        pane.AddHandler(GotFocusEvent, (_, _) => Console.Activate(t));
        _panes[t] = (pane, term);
    }

    private void RemovePane(TerminalViewModel t)
    {
        if (!_panes.Remove(t, out var p)) return;
        End(p.Terminal);
        (p.Pane.Parent as Panel)?.Children.Remove(p.Pane);
    }

    /// <summary>Ends the shell for good: the reparent guard off, the process killed.</summary>
    private static void End(TerminalControl term)
    {
        term.EndReparent();
        try { term.Kill(); } catch { /* never started, or already gone */ }
    }

    private static T Dock<T>(T control, Avalonia.Controls.Dock dock) where T : Control
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    /// <summary>
    /// Lays the terminals out: one above the other (rows), side by side (columns) or two per row (grid; with three,
    /// the last one takes the whole bottom row). Splitters between them, like the rest of the window.
    /// </summary>
    private void Rebuild()
    {
        // A lone terminal sits right in the border; several, in grids. Every pane must be free before the new layout.
        Panes.Child = null;
        foreach (var (pane, _) in _panes.Values) (pane.Parent as Panel)?.Children.Remove(pane);
        var panes = Console.Terminals.Where(_panes.ContainsKey).Select(t => _panes[t].Pane).ToList();
        if (panes.Count == 0) { Panes.Child = null; return; }

        var rows = Console.Arrangement switch
        {
            ConsoleArrangement.Columns => [panes],
            ConsoleArrangement.Grid => panes.Chunk(2).Select(c => c.ToList()).ToList(),
            _ => panes.Select(p => new List<Control> { p }).ToList(),
        };
        Panes.Child = Split(rows.Select(r => r.Count == 1 ? r[0] : Split(r, vertical: false)).ToList(), vertical: true);
    }

    private static Control Split(List<Control> items, bool vertical)
    {
        if (items.Count == 1) return items[0];
        var grid = new Grid();
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                var splitter = new GridSplitter { ResizeDirection = vertical ? GridResizeDirection.Rows : GridResizeDirection.Columns };
                AddCell(grid, splitter, 2 * i - 1, vertical, new GridLength(4));
            }
            AddCell(grid, items[i], 2 * i, vertical, new GridLength(1, GridUnitType.Star));
        }
        return grid;
    }

    private static void AddCell(Grid grid, Control child, int index, bool vertical, GridLength size)
    {
        if (vertical)
        {
            grid.RowDefinitions.Add(new RowDefinition(size) { MinHeight = size.IsStar ? 60 : 0 });
            Grid.SetRow(child, index);
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(size) { MinWidth = size.IsStar ? 120 : 0 });
            Grid.SetColumn(child, index);
        }
        grid.Children.Add(child);
    }

    private void FocusTerminal(TerminalViewModel t)
    {
        if (_window is not null) _window.Activate();
        Dispatcher.UIThread.Post(() =>
        {
            if (_panes.TryGetValue(t, out var p)) p.Terminal.Focus();
        }, DispatcherPriority.Background);
    }

    // ------------------------------------------------------------ place: the window

    private void OnConsoleChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ConsoleViewModel.Arrangement):
                Rebuild();
                break;
            case nameof(ConsoleViewModel.Place) or nameof(ConsoleViewModel.IsOpen):
                SyncWindow();
                break;
        }
    }

    private void SyncWindow()
    {
        bool want = Console is { IsOpen: true, IsWindow: true };
        if (want && _window is null)
        {
            Detach();
            var w = _window = new Window
            {
                Title = Loc.F("console.windowTitle", Console.ProjectName),
                Icon = Dialogs.Owner?.Icon,
                Width = 960,
                Height = 600,
                MinWidth = 400,
                MinHeight = 240,
                Content = this,
            };
            // Closing the window does not end the shells: the console goes back below the code.
            w.Closing += (_, _) =>
            {
                if (_window != w) return;
                _window = null;
                w.Content = null;
                Console.Place = ConsolePlace.Panel;
            };
            w.Show();
        }
        else if (!want && _window is { } open)
        {
            _window = null;
            open.Content = null;
            open.Close();
        }
    }

    private void OnDisposed()
    {
        Console.Terminals.CollectionChanged -= OnTerminalsChanged;
        Console.PropertyChanged -= OnConsoleChanged;
        Console.FocusRequested -= FocusTerminal;
        Console.Disposed -= OnDisposed;
        foreach (var t in _panes.Keys.ToList()) RemovePane(t);
        if (_window is { } w) { _window = null; w.Content = null; w.Close(); }
        Detach();
        Views.Remove(Console);
    }

    // ------------------------------------------------------------ shell and colors

    /// <summary>The sixteen terminal colors, taken from the Peach palette (docs/DISENO.md, 3.4).</summary>
    private static ThemeOptions Theme() => new()
    {
        Foreground = "#d4d4d4", Background = "#1b1c1f", Cursor = "#4a90d9", CursorAccent = "#1b1c1f",
        Selection = "#33507a", SelectionInactive = "#2d2e33",
        Black = "#1e1f22", Red = "#e0645a", Green = "#6cc070", Yellow = "#e0a24a",
        Blue = "#4a90d9", Magenta = "#9a7ae0", Cyan = "#4ac7c0", White = "#d4d4d4",
        BrightBlack = "#6f737a", BrightRed = "#f07a70", BrightGreen = "#8ad48e", BrightYellow = "#f0c890",
        BrightBlue = "#7db4ea", BrightMagenta = "#b49cec", BrightCyan = "#7adcd6", BrightWhite = "#ffffff",
    };
}
