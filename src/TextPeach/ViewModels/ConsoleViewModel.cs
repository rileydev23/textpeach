using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TextPeach.Services;

namespace TextPeach.ViewModels;

/// <summary>Where a project's console lives: below the code, in a tab of its own (the whole content area) or in a window.</summary>
public enum ConsolePlace { Panel, Page, Window }

/// <summary>How the terminals of a console share its space: one above the other, side by side, or two by two.</summary>
public enum ConsoleArrangement { Rows, Columns, Grid }

/// <summary>
/// One shell. The view model only knows which shell, its title and folder: the pseudo-terminal and the process belong to the view
/// (<see cref="Views.ConsoleView"/>), which ends them when <see cref="Closed"/> fires.
/// </summary>
public sealed partial class TerminalViewModel : ObservableObject
{
    public required string StartDirectory { get; init; }
    public required ShellInfo Shell { get; init; }
    [ObservableProperty] private string _title = "";
    /// <summary>The terminal with the keyboard focus (or the last one that had it): its header is highlighted.</summary>
    [ObservableProperty] private bool _isActive;
    public IRelayCommand? CloseCommand { get; set; }
    public event Action? Closed;

    internal void Close() => Closed?.Invoke();
}

/// <summary>
/// The console of a project: up to <see cref="MaxTerminals"/> shells in the project's folder. Shown as a panel below
/// the code, as a page that takes the whole content area (then it is also a tab of the file strip), or in a window
/// of its own. Terminals keep running while the console is hidden; they end when closed or with the project.
/// </summary>
public sealed partial class ConsoleViewModel : TabViewModel, IDisposable
{
    public const int MaxTerminals = 4;

    private readonly ViewerSettings _settings;

    public string Directory { get; }
    public string ProjectName { get; }
    /// <summary>A simple view (one loose file) has no file strip, so its console is never a page.</summary>
    public bool AllowsPage { get; }
    public ObservableCollection<TerminalViewModel> Terminals { get; } = new();

    /// <summary>Visible at its place: the panel is open, the page tab is in the strip, or the window is shown.</summary>
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private ConsolePlace _place;
    [ObservableProperty] private ConsoleArrangement _arrangement;

    public bool IsPanel => Place == ConsolePlace.Panel;
    public bool IsPage => Place == ConsolePlace.Page;
    public bool IsWindow => Place == ConsolePlace.Window;
    public bool IsRows => Arrangement == ConsoleArrangement.Rows;
    public bool IsColumns => Arrangement == ConsoleArrangement.Columns;
    public bool IsGrid => Arrangement == ConsoleArrangement.Grid;
    public bool HasSeveral => Terminals.Count > 1;
    public bool CanAdd => Terminals.Count < MaxTerminals;

    /// <summary>A short message for the project's status bar.</summary>
    public event Action<string>? Notice;
    /// <summary>The view should put the keyboard focus in this terminal.</summary>
    public event Action<TerminalViewModel>? FocusRequested;
    /// <summary>Ctrl+J pressed inside a terminal: the project decides what showing or hiding means at this place.</summary>
    public event Action? ToggleRequested;
    /// <summary>The project closed: the view lets go of the console (and closes its window).</summary>
    public event Action? Disposed;

    public ConsoleViewModel(string directory, string projectName, ViewerSettings settings, bool allowsPage = true)
    {
        Directory = directory;
        ProjectName = projectName;
        AllowsPage = allowsPage;
        _settings = settings;
        _place = !allowsPage && settings.ConsolePlace == ConsolePlace.Page ? ConsolePlace.Panel : settings.ConsolePlace;
        _arrangement = settings.ConsoleArrangement;
        Title = Loc.T("console.tab");
        ToolTip = directory;
        Terminals.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSeveral));
            OnPropertyChanged(nameof(CanAdd));
        };
    }

    partial void OnPlaceChanged(ConsolePlace value)
    {
        _settings.ConsolePlace = value;
        OnPropertyChanged(nameof(IsPanel));
        OnPropertyChanged(nameof(IsPage));
        OnPropertyChanged(nameof(IsWindow));
    }

    partial void OnArrangementChanged(ConsoleArrangement value)
    {
        _settings.ConsoleArrangement = value;
        OnPropertyChanged(nameof(IsRows));
        OnPropertyChanged(nameof(IsColumns));
        OnPropertyChanged(nameof(IsGrid));
    }

    public void RequestToggle() => ToggleRequested?.Invoke();

    /// <summary>Shows the console where it lives, with at least one terminal.</summary>
    public void Show()
    {
        if (Terminals.Count == 0) Add(Directory);
        IsOpen = true;
        if (ActiveTerminal is { } t) FocusRequested?.Invoke(t);
    }

    /// <summary>Every shell found on this machine, for the "+ Nueva ▾" menu and the tree's "Abrir consola aquí".</summary>
    public IReadOnlyList<ShellInfo> Shells => Services.Shells.Available;

    /// <summary>What "+ Nueva" starts: the shell chosen last (remembered for every project), else the first found.</summary>
    public ShellInfo DefaultShell => Services.Shells.Find(_settings.ConsoleShell);

    public string NewTip => Loc.F("console.new.tip", DefaultShell.Name, MaxTerminals);

    /// <summary>
    /// A new terminal in <paramref name="directory"/> (the project's folder by default) running <paramref name="shell"/>
    /// (the default one if null), shown right away. Picking a shell makes it the default.
    /// </summary>
    public TerminalViewModel? Add(string? directory = null, ShellInfo? shell = null)
    {
        if (!CanAdd)
        {
            Notice?.Invoke(Loc.F("console.max", MaxTerminals));
            return null;
        }
        if (shell is not null && shell.Id != _settings.ConsoleShell)
        {
            _settings.ConsoleShell = shell.Id;
            OnPropertyChanged(nameof(DefaultShell));
            OnPropertyChanged(nameof(NewTip));
        }
        shell ??= DefaultShell;
        var dir = directory is not null && System.IO.Directory.Exists(directory) ? directory : Directory;
        var t = new TerminalViewModel { StartDirectory = dir, Shell = shell, Title = TitleFor(shell, dir) };
        t.CloseCommand = new RelayCommand(() => Remove(t));
        Terminals.Add(t);
        Activate(t);
        IsOpen = true;
        FocusRequested?.Invoke(t);
        return t;
    }

    /// <summary>"PowerShell 7 · textpeach", numbered when the same shell and folder are open twice.</summary>
    private string TitleFor(ShellInfo shell, string dir)
    {
        var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name)) name = dir;
        var title = $"{shell.Name} · {name}";
        int same = Terminals.Count(t => t.Title == title || t.Title.StartsWith(title + " ("));
        return same > 0 ? $"{title} ({same + 1})" : title;
    }

    /// <summary>The "▾" next to "+ Nueva": one entry per shell found, the default one marked.</summary>
    public IReadOnlyList<MenuAction> ShellMenu(string? directory = null) =>
        Shells.Select(s => MenuAction.Sync(s.Id == DefaultShell.Id ? Loc.F("console.shell.default", s.Name) : s.Name,
            () => Add(directory, s))).ToList();

    public TerminalViewModel? ActiveTerminal => Terminals.FirstOrDefault(t => t.IsActive) ?? Terminals.LastOrDefault();

    public void Activate(TerminalViewModel t)
    {
        foreach (var other in Terminals) other.IsActive = ReferenceEquals(other, t);
    }

    /// <summary>Closes one terminal (its ✕, or its shell ended). The last one closed hides the console.</summary>
    public void Remove(TerminalViewModel t)
    {
        int idx = Terminals.IndexOf(t);
        if (idx < 0) return;
        bool wasActive = t.IsActive;
        Terminals.RemoveAt(idx);
        t.Close();
        if (Terminals.Count == 0)
        {
            IsOpen = false;
            return;
        }
        if (wasActive)
        {
            var next = Terminals[Math.Clamp(idx - 1, 0, Terminals.Count - 1)];
            Activate(next);
            FocusRequested?.Invoke(next);
        }
    }

    [RelayCommand]
    private void New() => Add();

    [RelayCommand]
    private void Hide() => IsOpen = false;

    [RelayCommand]
    private void MoveTo(string where)
    {
        if (!Enum.TryParse<ConsolePlace>(where, ignoreCase: true, out var place)) return;
        if (place == ConsolePlace.Page && !AllowsPage) return;
        Place = place;
        Show();
    }

    [RelayCommand]
    private void Arrange(string how)
    {
        if (Enum.TryParse<ConsoleArrangement>(how, ignoreCase: true, out var arrangement)) Arrangement = arrangement;
    }

    public void Dispose()
    {
        foreach (var t in Terminals.ToList()) t.Close();
        Terminals.Clear();
        IsOpen = false;
        Disposed?.Invoke();
    }
}
