using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TextPeach.Services;
using TextPeach.Views;

namespace TextPeach.ViewModels;

/// <summary>The "Proyectos" tab: saved project folders, organised in collapsible groups.</summary>
public partial class HomeViewModel : TabViewModel
{
    private readonly ProjectStore _store;
    private readonly Action<ProjectEntry> _open;

    public override bool Closable => false;
    public ObservableCollection<HomeRow> Rows { get; } = new();

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _onlyFavorites;
    [ObservableProperty] private HomeRow? _selectedRow;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _isEmpty;

    public ProjectEntry? Selected => (SelectedRow as ProjectRow)?.Entry;
    private AppState State => _store.State;
    private bool IsFiltering => OnlyFavorites || Filter.Trim().Length > 0;

    public HomeViewModel(ProjectStore store, Action<ProjectEntry> open)
    {
        _store = store;
        _open = open;
        Title = "Proyectos";
        Reload();
        _ = RefreshKindsAsync();
    }

    partial void OnFilterChanged(string value) => Reload();
    partial void OnOnlyFavoritesChanged(bool value) => Reload();

    // ---------------------------------------------------------------- list

    public void Reload()
    {
        var keep = Selected;
        var q = Filter.Trim();
        bool filtering = IsFiltering;
        var sections = ProjectGrouping.Build(State,
            p => (!OnlyFavorites || p.Favorite) && Dialogs.Fuzzy(p.Name + " " + p.Path, q),
            hideEmpty: filtering);

        Rows.Clear();
        // With no groups at all the list stays flat.
        bool headers = State.Groups.Count > 0;
        foreach (var section in sections)
        {
            // While filtering every section is expanded, so matches are never hidden.
            bool collapsed = !filtering && (section.Group?.Collapsed ?? State.UngroupedCollapsed);
            if (headers)
            {
                var s = section;
                Rows.Add(new GroupHeaderRow
                {
                    Group = s.Group,
                    Count = s.Projects.Count,
                    IsCollapsed = collapsed,
                    ToggleCommand = new RelayCommand(() => ToggleGroup(s.Group)),
                    OpenAllCommand = new RelayCommand(() => OpenAll(s.Projects)),
                });
            }
            if (collapsed) continue;
            foreach (var p in section.Projects) Rows.Add(new ProjectRow { Entry = p, Indented = headers });
        }

        SelectedRow = keep is null ? null : Rows.OfType<ProjectRow>().FirstOrDefault(r => r.Entry == keep);
        IsEmpty = !Rows.OfType<ProjectRow>().Any() && !Rows.OfType<GroupHeaderRow>().Any(h => h.Count > 0);
    }

    public async Task RefreshKindsAsync()
    {
        var snapshot = State.Projects.ToList();
        await Task.Run(() =>
        {
            foreach (var p in snapshot)
            {
                p.Missing = !Directory.Exists(p.Path);
                p.Kind = p.Missing ? null : ProjectScanner.DetectKind(p.Path);
            }
        });
        Reload();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await RefreshKindsAsync();

    // ---------------------------------------------------------------- groups

    public void ToggleGroup(ProjectGroup? group)
    {
        if (IsFiltering) return; // everything is expanded while filtering
        if (group is null) State.UngroupedCollapsed = !State.UngroupedCollapsed;
        else group.Collapsed = !group.Collapsed;
        _store.Save();
        Reload();
    }

    [RelayCommand]
    private void CollapseAll() => SetAllCollapsed(true);

    [RelayCommand]
    private void ExpandAll() => SetAllCollapsed(false);

    private void SetAllCollapsed(bool collapsed)
    {
        foreach (var g in State.Groups) g.Collapsed = collapsed;
        State.UngroupedCollapsed = collapsed;
        _store.Save();
        Reload();
    }

    private void OpenAll(IReadOnlyList<ProjectEntry> projects)
    {
        var existing = projects.Where(p => Directory.Exists(p.Path)).ToList();
        foreach (var p in existing) _open(p);
        Status = existing.Count == projects.Count
            ? $"Abiertos {Plural(existing.Count)}"
            : $"Abiertos {existing.Count} de {projects.Count} (algunas carpetas ya no existen)";
    }

    [RelayCommand]
    private Task NewGroup() => MoveToNewGroup(Array.Empty<ProjectEntry>());

    private async Task MoveToNewGroup(IReadOnlyList<ProjectEntry> projects)
    {
        var name = await Dialogs.Prompt("Nuevo grupo", "Nombre del grupo", ProjectGrouping.UniqueName(State, "Grupo"));
        if (string.IsNullOrWhiteSpace(name)) return;
        var group = ProjectGrouping.Ensure(State, name);
        group.Collapsed = false;
        MoveTo(projects, group);
    }

    private void MoveTo(IReadOnlyList<ProjectEntry> projects, ProjectGroup? group)
    {
        ProjectGrouping.Move(State, projects, group);
        _store.Save();
        Reload();
        if (projects.Count > 0)
            Status = group is null
                ? $"{Plural(projects.Count)} sin grupo"
                : $"{Plural(projects.Count)} en el grupo \"{group.Name}\"";
    }

    private async Task RenameGroup(ProjectGroup group)
    {
        var name = await Dialogs.Prompt("Renombrar grupo", "Nuevo nombre", group.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == group.Name) return;
        if (!ProjectGrouping.Rename(State, group, name))
        {
            await Dialogs.Message("Renombrar grupo", $"Ya existe un grupo llamado \"{name.Trim()}\".");
            return;
        }
        _store.Save();
        Reload();
    }

    private async Task DeleteGroup(ProjectGroup group, bool removeProjects)
    {
        var count = ProjectGrouping.Members(State, group).Count();
        var message = removeProjects
            ? $"Se quitará el grupo \"{group.Name}\" y sus {Plural(count)} de la lista.\nNo se borra nada del disco."
            : $"Se eliminará el grupo \"{group.Name}\". Sus {Plural(count)} pasarán a \"Sin grupo\".";
        if (count > 0 && !await Dialogs.Confirm("Eliminar grupo", message, "Eliminar")) return;
        ProjectGrouping.Delete(State, group, removeProjects);
        _store.Save();
        Reload();
    }

    private static string Plural(int n) => n == 1 ? "1 proyecto" : $"{n} proyectos";

    // ---------------------------------------------------------------- menus

    public IReadOnlyList<MenuAction> MenuForGroup(GroupHeaderRow header)
    {
        var g = header.Group;
        var projects = ProjectGrouping.Members(State, g).ToList();
        var items = new List<MenuAction>
        {
            MenuAction.Sync($"Abrir todos ({projects.Count})", () => OpenAll(projects)),
            MenuAction.Sync(header.IsCollapsed ? "Expandir" : "Contraer", () => ToggleGroup(g)),
        };
        if (g is not null)
        {
            items.Add(MenuAction.Separator);
            items.Add(new MenuAction("Renombrar grupo…", () => RenameGroup(g)));
            items.Add(new MenuAction("Eliminar grupo (los proyectos quedan sin grupo)…", () => DeleteGroup(g, removeProjects: false)));
            items.Add(new MenuAction("Quitar el grupo y sus proyectos de la lista…", () => DeleteGroup(g, removeProjects: true)));
        }
        return MenuList.Clean(items);
    }

    public IReadOnlyList<MenuAction> MenuForProjects(IReadOnlyList<ProjectEntry> projects)
    {
        if (projects.Count == 0) return Array.Empty<MenuAction>();
        var single = projects.Count == 1 ? projects[0] : null;
        var items = new List<MenuAction>
        {
            single is not null
                ? MenuAction.Sync("Abrir", () => Open(single))
                : MenuAction.Sync($"Abrir los {projects.Count}", () => OpenAll(projects)),
        };
        if (single is not null)
        {
            items.Add(MenuAction.Sync("Favorito (alternar)", () => ToggleFavorite(single)));
            items.Add(new MenuAction("Renombrar en la lista…", () => RenameAsync(single)));
        }

        items.Add(MenuAction.Separator);
        var targets = new List<MenuAction>();
        foreach (var g in State.Groups)
        {
            var group = g;
            if (projects.All(p => ProjectGrouping.Find(State, p.Group) == group)) continue;
            targets.Add(MenuAction.Sync(group.Name, () => MoveTo(projects, group)));
        }
        if (targets.Count > 0) targets.Add(MenuAction.Separator);
        targets.Add(new MenuAction("Nuevo grupo…", () => MoveToNewGroup(projects)));
        items.Add(MenuAction.Submenu(projects.Count == 1 ? "Mover a grupo" : $"Mover los {projects.Count} a grupo", targets));
        if (projects.Any(p => ProjectGrouping.Find(State, p.Group) is not null))
            items.Add(MenuAction.Sync("Quitar del grupo", () => MoveTo(projects, null)));

        if (single is not null)
        {
            items.Add(MenuAction.Separator);
            items.Add(MenuAction.Sync("Abrir con la aplicación predeterminada", () => OpenWithDefault(single)));
        }
        items.Add(MenuAction.Separator);
        items.Add(MenuAction.Sync(single is not null ? "Quitar de la lista" : $"Quitar los {projects.Count} de la lista", () => RemoveMany(projects)));
        return MenuList.Clean(items);
    }

    // ---------------------------------------------------------------- projects

    [RelayCommand]
    private void Open(ProjectEntry? entry)
    {
        entry ??= Selected;
        if (entry is null) return;
        if (!Directory.Exists(entry.Path))
        {
            Status = $"La carpeta ya no existe: {entry.Path}";
            return;
        }
        _open(entry);
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var dir = await Dialogs.PickFolder("Seleccionar carpeta del proyecto");
        if (dir is null) return;
        AddPath(dir, open: true);
    }

    public void AddPath(string dir, bool open)
    {
        if (!Directory.Exists(dir))
        {
            Status = $"No es una carpeta: {dir}";
            return;
        }
        var entry = _store.Add(dir);
        Reload();
        SelectedRow = Rows.OfType<ProjectRow>().FirstOrDefault(r => r.Entry == entry);
        Status = $"Agregado: {entry.Name}";
        _ = RefreshKindsAsync();
        if (open) _open(entry);
    }

    /// <summary>Dropped folder: a project is added as is; a folder that only holds projects becomes a group.</summary>
    public async Task AddOrImportAsync(string dir)
    {
        if (ProjectScanner.DetectKind(dir) is null)
        {
            var inside = await Task.Run(() => ProjectScanner.FindProjects(dir, 4));
            if (inside.Count > 1) { await ImportFolderAsGroupAsync(dir); return; }
        }
        AddPath(dir, open: false);
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        var dir = await Dialogs.PickFolder("Carpeta a escanear");
        if (dir is not null) await ImportFolderAsGroupAsync(dir);
    }

    /// <summary>
    /// Finds the projects under <paramref name="dir"/> and puts them in a group named after the
    /// folder. Projects already in another group stay where they are.
    /// </summary>
    public async Task ImportFolderAsGroupAsync(string dir)
    {
        Busy = true;
        Status = "Escaneando...";
        try
        {
            var found = await Task.Run(() => ProjectScanner.FindProjects(dir, 4));
            if (found.Count == 0)
            {
                Status = $"No se encontraron proyectos en {dir}";
                return;
            }
            var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
            var group = ProjectGrouping.Ensure(State, string.IsNullOrEmpty(name) ? dir : name);
            group.Collapsed = false;

            int added = 0, kept = 0;
            foreach (var f in found)
            {
                var entry = _store.Find(f);
                if (entry is null) { entry = _store.Add(f); added++; }
                if (ProjectGrouping.Find(State, entry.Group) is { } other && other != group) { kept++; continue; }
                entry.Group = group.Name;
            }
            _store.Save();
            Reload();
            _ = RefreshKindsAsync();
            Status = $"Grupo \"{group.Name}\": {Plural(found.Count)} encontrados, {added} nuevos" +
                     (kept > 0 ? $", {kept} ya estaban en otro grupo" : "") + ".";
        }
        finally { Busy = false; }
    }

    public void RemoveMany(IReadOnlyList<ProjectEntry> projects)
    {
        foreach (var p in projects) _store.Remove(p);
        Reload();
        Status = projects.Count == 1
            ? $"Quitado de la lista: {projects[0].Name} (no se borró nada del disco)"
            : $"Quitados {projects.Count} proyectos de la lista (no se borró nada del disco)";
    }

    [RelayCommand]
    private void ToggleFavorite(ProjectEntry? entry)
    {
        entry ??= Selected;
        if (entry is null) return;
        entry.Favorite = !entry.Favorite;
        _store.Save();
        Reload();
    }

    private async Task RenameAsync(ProjectEntry entry)
    {
        var name = await Dialogs.Prompt("Renombrar en la lista", "Nombre visible (la carpeta no cambia)", entry.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        entry.Name = name.Trim();
        _store.Save();
        Reload();
    }

    private void OpenWithDefault(ProjectEntry entry)
    {
        try { Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true }); }
        catch (Exception ex) { Status = ex.Message; }
    }
}
