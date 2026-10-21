using System.Text.Json;
using System.Text.Json.Serialization;

namespace TextPeach.Services;

public sealed class ProjectEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Favorite { get; set; }
    public DateTimeOffset LastOpened { get; set; }
    /// <summary>Name of the <see cref="ProjectGroup"/> it belongs to; null = ungrouped.</summary>
    public string? Group { get; set; }
    [JsonIgnore] public string? Kind { get; set; }
    [JsonIgnore] public bool Missing { get; set; }
}

public sealed class ProjectGroup
{
    public string Name { get; set; } = "";
    public bool Collapsed { get; set; }
}

/// <summary>Files left open inside a project, restored with its tab.</summary>
public sealed class ProjectSession
{
    public List<string> Files { get; set; } = new();
    public string? Active { get; set; }
    /// <summary>The file in the preview tab (opened with one click, replaced by the next one).</summary>
    public string? Preview { get; set; }
}

/// <summary>Per-project choices that outlive the open tabs.</summary>
public sealed class ProjectPrefs
{
    /// <summary>Files (relative) the reader switched to the code view; the rest of the documents open rendered.</summary>
    public List<string> CodeView { get; set; } = new();
    /// <summary>Lines of <see cref="IgnoreRules"/>: extra folders to leave out, or "!name" to show a default one.</summary>
    public List<string> Ignore { get; set; } = new();
}

public sealed class AppState
{
    public List<ProjectEntry> Projects { get; set; } = new();
    /// <summary>Groups in display order.</summary>
    public List<ProjectGroup> Groups { get; set; } = new();
    public bool UngroupedCollapsed { get; set; }
    public List<string> OpenTabs { get; set; } = new();
    public string? ActiveTab { get; set; }
    public Dictionary<string, ProjectSession> Sessions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ProjectPrefs> Prefs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double Width { get; set; }
    public double Height { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool Maximized { get; set; }
    public double FontSize { get; set; } = 13;
    public bool WordWrap { get; set; }
    /// <summary>Diff view: both versions side by side (true) or one column (false).</summary>
    public bool DiffSideBySide { get; set; } = true;
    /// <summary>Interface language: "en", "es" or null to follow Windows.</summary>
    public string? Language { get; set; }
}

[JsonSerializable(typeof(AppState))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class AppStateContext : JsonSerializerContext { }

public sealed class ProjectStore
{
    private readonly string _file;
    private readonly object _lock = new();

    public AppState State { get; private set; } = new();
    public string DataFolder => System.IO.Path.GetDirectoryName(_file)!;

    public ProjectStore()
    {
        var dir = PeachApps.SettingsFolder("TextPeach");
        Directory.CreateDirectory(dir);
        _file = System.IO.Path.Combine(dir, "state.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_file))
                State = JsonSerializer.Deserialize(File.ReadAllText(_file), AppStateContext.Default.AppState) ?? new AppState();
        }
        catch
        {
            State = new AppState();
        }
        // The deserializer builds a case-sensitive dictionary; paths on Windows are not.
        State.Sessions = new Dictionary<string, ProjectSession>(State.Sessions, StringComparer.OrdinalIgnoreCase);
        State.Prefs = new Dictionary<string, ProjectPrefs>(State.Prefs, StringComparer.OrdinalIgnoreCase);
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var tmp = _file + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(State, AppStateContext.Default.AppState));
                File.Move(tmp, _file, true);
            }
            catch { /* best effort */ }
        }
    }

    public static string Normalize(string p) => System.IO.Path.GetFullPath(p).TrimEnd('\\', '/');

    public ProjectEntry? Find(string path)
        => State.Projects.FirstOrDefault(r => string.Equals(Normalize(r.Path), Normalize(path), StringComparison.OrdinalIgnoreCase));

    public ProjectEntry Add(string path)
    {
        var existing = Find(path);
        if (existing is not null) return existing;
        var norm = Normalize(path);
        var name = System.IO.Path.GetFileName(norm);
        var e = new ProjectEntry { Path = norm, Name = name.Length > 0 ? name : norm, LastOpened = DateTimeOffset.Now };
        State.Projects.Add(e);
        Save();
        return e;
    }

    public void Remove(ProjectEntry e)
    {
        State.Projects.Remove(e);
        State.OpenTabs.RemoveAll(t => string.Equals(t, e.Path, StringComparison.OrdinalIgnoreCase));
        State.Sessions.Remove(e.Path);
        State.Prefs.Remove(e.Path);
        Save();
    }

    public ProjectPrefs PrefsFor(string projectPath)
    {
        if (!State.Prefs.TryGetValue(projectPath, out var p)) State.Prefs[projectPath] = p = new ProjectPrefs();
        return p;
    }

    public void Touch(ProjectEntry e)
    {
        e.LastOpened = DateTimeOffset.Now;
        Save();
    }
}
