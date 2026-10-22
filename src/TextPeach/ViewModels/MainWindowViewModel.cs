using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TextPeach.Services;
using TextPeach.Views;

namespace TextPeach.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public ProjectStore Store { get; }
    public ViewerSettings Settings { get; }
    public HomeViewModel Home { get; }
    /// <summary>Every tab, Home first (order used by Ctrl+Tab and Ctrl+1..9).</summary>
    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    /// <summary>Project tabs only: the tab strip. Home has its own button.</summary>
    public ObservableCollection<ProjectViewModel> ProjectTabs { get; } = new();

    [ObservableProperty] private TabViewModel? _selectedTab;
    private readonly bool _ready;

    public MainWindowViewModel(ProjectStore store)
    {
        Store = store;
        Settings = new ViewerSettings(store);
        Home = new HomeViewModel(store, OpenProject);
        Tabs.Add(Home);
        SelectedTab = Home;

        var saved = store.State.OpenTabs.ToList();
        var active = store.State.ActiveTab;
        foreach (var path in saved)
        {
            if (Directory.Exists(path)) AddProjectTab(path, select: false);
        }
        SelectedTab = Tabs.OfType<ProjectViewModel>().FirstOrDefault(t =>
            string.Equals(t.RootPath, active, StringComparison.OrdinalIgnoreCase)) ?? (TabViewModel)Home;
        _ready = true;
    }

    /// <summary>
    /// <c>TextPeach.exe &lt;carpeta|archivo&gt;</c>: a folder becomes (or reuses) a project; a file opens inside
    /// the saved project that contains it, or inside its own folder.
    /// </summary>
    public void OpenFromArgs(IEnumerable<string> args)
    {
        foreach (var raw in args)
        {
            string path;
            try { path = ProjectStore.Normalize(raw); } catch { continue; }
            if (Directory.Exists(path))
            {
                OpenProject(Store.Add(path));
            }
            else if (File.Exists(path))
            {
                var root = Store.State.Projects
                    .Where(p => path.StartsWith(ProjectStore.Normalize(p.Path) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.Path.Length).FirstOrDefault()
                    ?? Store.Add(Path.GetDirectoryName(path)!);
                OpenProject(root);
                if (SelectedTab is ProjectViewModel p)
                    p.OpenFile(Path.GetRelativePath(p.RootPath, path).Replace('\\', '/'));
            }
        }
        Home.Reload();
    }

    public void OpenProject(ProjectEntry entry)
    {
        Store.Touch(entry);
        AddProjectTab(entry.Path, select: true);
        SaveTabs();
    }

    private void AddProjectTab(string path, bool select)
    {
        var existing = Tabs.OfType<ProjectViewModel>().FirstOrDefault(t =>
            string.Equals(t.RootPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (select) SelectedTab = existing;
            return;
        }
        var tab = new ProjectViewModel(path, Store, Settings);
        tab.CloseCommand = new AsyncRelayCommand(() => CloseTabAsync(tab));
        Tabs.Add(tab);
        ProjectTabs.Add(tab);
        if (select) SelectedTab = tab;
    }

    public async Task CloseTabAsync(TabViewModel tab)
    {
        if (!tab.Closable) return;
        if (tab is ProjectViewModel p && p.HasUnsavedChanges &&
            !await Dialogs.Confirm(Loc.T("common.unsaved"), Loc.F("main.closeProject.text", p.Title), Loc.T("common.close")))
            return;
        int idx = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (tab is ProjectViewModel project) ProjectTabs.Remove(project);
        (tab as IDisposable)?.Dispose();
        if (SelectedTab == tab || SelectedTab is null)
            SelectedTab = Tabs[Math.Clamp(idx - 1, 0, Tabs.Count - 1)];
        SaveTabs();
    }

    /// <summary>Ctrl+W: closes the current file; with no file open, the project tab.</summary>
    [RelayCommand]
    private async Task CloseCurrentAsync()
    {
        if (SelectedTab is ProjectViewModel p && p.CloseCurrentDoc()) return;
        if (SelectedTab is { Closable: true } t) await CloseTabAsync(t);
    }

    [RelayCommand]
    private void NextTab()
    {
        if (Tabs.Count < 2 || SelectedTab is null) return;
        SelectedTab = Tabs[(Tabs.IndexOf(SelectedTab) + 1) % Tabs.Count];
    }

    [RelayCommand]
    private void PreviousTab()
    {
        if (Tabs.Count < 2 || SelectedTab is null) return;
        SelectedTab = Tabs[(Tabs.IndexOf(SelectedTab) - 1 + Tabs.Count) % Tabs.Count];
    }

    [RelayCommand]
    private void SelectTab(string? number)
    {
        if (int.TryParse(number, out var n) && n >= 1 && n <= Tabs.Count) SelectedTab = Tabs[n - 1];
    }

    /// <summary>The settings folder; Debug builds can show a generic one (TEXTPEACH_DEMO_DATAFOLDER) for screenshots.</summary>
    private string ShownDataFolder =>
#if DEBUG
        Environment.GetEnvironmentVariable("TEXTPEACH_DEMO_DATAFOLDER") ??
#endif
        Store.DataFolder;

    [RelayCommand]
    private Task ShowAbout() => Dialogs.About(Loc.T("dlg.about.text"), ShownDataFolder, Shortcuts.Groups, Store, () =>
    {
        try { Process.Start(new ProcessStartInfo(Store.DataFolder) { UseShellExecute = true }); } catch { }
    }, () => CheckForUpdatesAsync(manual: true));

    // ------------------------------------------------------------ updates

    /// <summary>Text of the button next to "?": the new version, or the download progress. Null hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private string? _updateLabel;
    public bool HasUpdate => UpdateLabel is not null;
    private bool _updating;

    /// <summary>Asks the window to close without the unsaved-changes prompt (already answered).</summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Automatic (at start): at most once a day, only if enabled, and silent on errors. Manual ("Check now" in
    /// About): always asks GitHub and tells the result.
    /// </summary>
    public async Task CheckForUpdatesAsync(bool manual)
    {
        var st = Store.State;
        if (!manual && (!st.CheckUpdates || st.LastUpdateCheck is { } last && DateTimeOffset.Now - last < TimeSpan.FromHours(24)))
        {
            ShowKnownUpdate();
            return;
        }
        UpdateInfo? info;
        try { info = await Updater.CheckAsync(Updater.Current); }
        catch (Exception ex)
        {
            if (manual) await Dialogs.Message(Loc.T("update.check.title"), Loc.F("update.check.failed", ex.Message));
            return;
        }
        st.LastUpdateCheck = DateTimeOffset.Now;
        st.LatestVersion = info?.Version.ToString(3);
        // Asking explicitly brings back a version that was skipped.
        if (manual && info is not null && st.SkippedVersion == st.LatestVersion) st.SkippedVersion = null;
        Store.Save();
        ShowKnownUpdate();
        if (!manual) return;
        if (info is null) await Dialogs.Message(Loc.T("update.check.title"), Loc.F("update.upToDate", Updater.Current.ToString(3)));
        else await OfferUpdateAsync(info);
    }

    private void ShowKnownUpdate()
    {
        if (_updating) return;
        var st = Store.State;
        UpdateLabel = Updater.TryParseTag(st.LatestVersion, out var v) && v > Updater.Current && st.LatestVersion != st.SkippedVersion
            ? "↑ " + st.LatestVersion
            : null;
    }

    /// <summary>The button next to "?": fresh release data (the saved check only keeps the version), then the popup.</summary>
    [RelayCommand]
    private async Task ShowUpdateAsync()
    {
        if (_updating) return;
        UpdateInfo? info;
        try { info = await Updater.CheckAsync(Updater.Current); }
        catch (Exception ex)
        {
            if (await Dialogs.Confirm(Loc.T("update.check.title"), Loc.F("update.check.failed", ex.Message), Loc.T("update.openPage")))
                Updater.OpenInBrowser(Updater.ReleasesPage);
            return;
        }
        if (info is null)
        {
            Store.State.LatestVersion = null;
            Store.Save();
            ShowKnownUpdate();
            return;
        }
        await OfferUpdateAsync(info);
    }

    private async Task OfferUpdateAsync(UpdateInfo info)
    {
        var choice = await Dialogs.Update(info, Updater.Current);
        if (choice == UpdateChoice.Skip)
        {
            Store.State.SkippedVersion = info.Version.ToString(3);
            Store.Save();
            ShowKnownUpdate();
            return;
        }
        if (choice != UpdateChoice.Install) return;
        if (HasUnsavedChanges &&
            !await Dialogs.Confirm(Loc.T("common.unsaved"), Loc.T("update.unsaved"), Loc.T("update.install")))
            return;

        _updating = true;
        UpdateLabel = Loc.F("update.downloading", 0);
        try
        {
            var next = await Updater.ApplyAsync(info, new Progress<double>(f => UpdateLabel = Loc.F("update.downloading", (int)(f * 100))));
            Process.Start(next);
            CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            _updating = false;
            ShowKnownUpdate();
            if (await Dialogs.Confirm(Loc.T("update.failed.title"), Loc.F("update.failed.text", ex.Message), Loc.T("update.openPage")))
                Updater.OpenInBrowser(info.PageUrl);
        }
    }

    /// <summary>Ctrl+P: inside a project, jump to a file; on the home tab, pick a project.</summary>
    [RelayCommand]
    private async Task QuickOpenAsync()
    {
        if (SelectedTab is ProjectViewModel p) await p.QuickOpenFileAsync();
        else await QuickOpenProjectAsync();
    }

    [RelayCommand]
    private async Task QuickOpenProjectAsync()
    {
        var picked = await Dialogs.QuickOpen(Store.State.Projects
            .OrderByDescending(r => r.Favorite).ThenByDescending(r => r.LastOpened).ToList());
        if (picked is not null) OpenProject(picked);
    }

    public bool IsHomeSelected => SelectedTab == Home;

    /// <summary>Selection of the tab strip; null while Home is shown (a null coming back from the strip is ignored).</summary>
    public ProjectViewModel? SelectedProjectTab
    {
        get => SelectedTab as ProjectViewModel;
        set { if (value is not null) SelectedTab = value; }
    }

    [RelayCommand]
    private void ShowHome() => SelectedTab = Home;

    /// <summary>Next to the app name in the title bar: the open project and where it lives.</summary>
    public string Subtitle => SelectedTab is ProjectViewModel p ? $"{p.Title}  ·  {p.RootPath}" : "";
    /// <summary>Taskbar and Alt+Tab text.</summary>
    public string WindowTitle => SelectedTab is ProjectViewModel p ? $"{p.Title} — TextPeach" : "TextPeach";

    partial void OnSelectedTabChanged(TabViewModel? value)
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(IsHomeSelected));
        OnPropertyChanged(nameof(SelectedProjectTab));
        if (_ready) SaveTabs();
    }

    public void SaveTabs()
    {
        Store.State.OpenTabs = Tabs.OfType<ProjectViewModel>().Select(t => t.RootPath).ToList();
        Store.State.ActiveTab = (SelectedTab as ProjectViewModel)?.RootPath;
        Store.Save();
    }

    public bool HasUnsavedChanges => Tabs.OfType<ProjectViewModel>().Any(p => p.HasUnsavedChanges);

    public void Shutdown()
    {
        SaveTabs();
        foreach (var t in Tabs.OfType<ProjectViewModel>()) t.Dispose();
    }
}
