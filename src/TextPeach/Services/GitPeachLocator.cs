using System.Diagnostics;

namespace TextPeach.Services;

/// <summary>
/// Finds GitPeach (the git client of the same family), which is where everything git happens: TextPeach only
/// shows the state. Mirror of GitPeach's TextPeachLocator; keep both in sync.
/// </summary>
public static class GitPeachLocator
{
    public const string ExeName = "GitPeach.exe";
    public const string RepoUrl = "https://github.com/rileydev23/gitpeach";

    /// <summary>
    /// Fallback locations (when GitPeach has never announced itself) in priority order: next to TextPeach, a sibling
    /// "GitPeach" folder, the usual per-user and per-machine install folders, then every folder in PATH.
    /// </summary>
    public static IEnumerable<string> Candidates(string baseDir, string? localAppData, string? programFiles, string? pathVariable)
    {
        yield return Path.Combine(baseDir, ExeName);
        var parent = Path.GetDirectoryName(baseDir.TrimEnd('\\', '/'));
        if (parent is not null) yield return Path.Combine(parent, "GitPeach", ExeName);
        if (!string.IsNullOrEmpty(localAppData)) yield return Path.Combine(localAppData, "Programs", "GitPeach", ExeName);
        if (!string.IsNullOrEmpty(programFiles)) yield return Path.Combine(programFiles, "GitPeach", ExeName);
        foreach (var dir in (pathVariable ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return Path.Combine(dir.Trim('"'), ExeName);
    }

    public static string? Find(Func<string, bool> exists, string baseDir, string? localAppData, string? programFiles, string? pathVariable)
    {
        foreach (var c in Candidates(baseDir, localAppData, programFiles, pathVariable))
        {
            try { if (exists(c)) return c; } catch { /* malformed PATH entry */ }
        }
        return null;
    }

    /// <summary>Where GitPeach last said it runs from (peachapps/GitPeach/location.json), else the usual folders.</summary>
    public static string? FindOnThisMachine() => PeachApps.Locate("GitPeach") ?? Find(File.Exists,
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetEnvironmentVariable("PATH"));

    /// <summary>Opens <paramref name="repo"/> in GitPeach. False when GitPeach is not on this machine.</summary>
    public static bool Open(string repo)
    {
        var exe = FindOnThisMachine();
        if (exe is null) return false;
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = repo };
            psi.ArgumentList.Add(repo);
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }
}
