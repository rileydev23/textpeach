namespace TextPeach.Services;

/// <summary>A shell the console can start: its name in the menu, the program and its arguments.</summary>
public sealed record ShellInfo(string Id, string Name, string Path, string[] Args);

/// <summary>
/// The shells installed on this machine, found once. Windows: PowerShell 7, Windows PowerShell, cmd, Git Bash and
/// WSL (if a distribution is installed). Linux and macOS: the user's <c>$SHELL</c> first, then <c>/etc/shells</c>.
/// </summary>
public static class Shells
{
    private static IReadOnlyList<ShellInfo>? _available;

    public static IReadOnlyList<ShellInfo> Available => _available ??= Detect();

    /// <summary>The shell with <paramref name="id"/>, or the first one found (the best guess for a default).</summary>
    public static ShellInfo Find(string? id) =>
        Available.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Available[0];

    private static IReadOnlyList<ShellInfo> Detect()
    {
        var list = OperatingSystem.IsWindows() ? DetectWindows() : DetectUnix();
        // Never empty: the console always has something to start.
        if (list.Count == 0)
            list.Add(OperatingSystem.IsWindows() ? new("cmd", "cmd", "cmd.exe", []) : new("sh", "sh", "/bin/sh", []));
        return list;
    }

    private static List<ShellInfo> DetectWindows()
    {
        var list = new List<ShellInfo>();
        string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "";
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var programFiles = Env("ProgramFiles");

        var pwsh = OnPath("pwsh.exe") ?? FirstExisting(System.IO.Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"));
        if (pwsh is not null) list.Add(new("pwsh", "PowerShell 7", pwsh, ["-NoLogo"]));
        var powershell = FirstExisting(System.IO.Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"));
        if (powershell is not null) list.Add(new("powershell", "Windows PowerShell", powershell, ["-NoLogo"]));
        var cmd = FirstExisting(Env("ComSpec"), System.IO.Path.Combine(system, "cmd.exe"));
        if (cmd is not null) list.Add(new("cmd", "cmd", cmd, []));

        // Git Bash: next to git.exe on the PATH (…\Git\cmd\git.exe → …\Git\bin\bash.exe) or in the usual folders.
        var gitRoot = OnPath("git.exe") is { } git ? System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(git)) : null;
        var bash = FirstExisting(
            gitRoot is null ? "" : System.IO.Path.Combine(gitRoot, "bin", "bash.exe"),
            System.IO.Path.Combine(programFiles, "Git", "bin", "bash.exe"),
            System.IO.Path.Combine(Env("ProgramFiles(x86)"), "Git", "bin", "bash.exe"),
            System.IO.Path.Combine(Env("LOCALAPPDATA"), "Programs", "Git", "bin", "bash.exe"));
        if (bash is not null)
        {
            // A login shell would otherwise start in the home folder instead of the project.
            Environment.SetEnvironmentVariable("CHERE_INVOKING", "1");
            list.Add(new("gitbash", "Git Bash", bash, ["--login", "-i"]));
        }

        var wsl = FirstExisting(System.IO.Path.Combine(system, "wsl.exe"));
        if (wsl is not null && HasWslDistro()) list.Add(new("wsl", "WSL", wsl, []));
        return list;
    }

    private static bool HasWslDistro()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
            return key?.GetSubKeyNames().Length > 0;
        }
        catch { return false; }
    }

    private static List<ShellInfo> DetectUnix()
    {
        string etc = "";
        try { etc = File.ReadAllText("/etc/shells"); } catch { /* none listed: $SHELL and the fallback */ }
        return FromEtcShells(etc, Environment.GetEnvironmentVariable("SHELL"), File.Exists);
    }

    /// <summary>
    /// The shells of an <c>/etc/shells</c> text that exist, one per name (<c>/bin/bash</c> and <c>/usr/bin/bash</c>
    /// are the same shell), the user's own first. Logins that are not shells (nologin, false) are left out.
    /// </summary>
    public static List<ShellInfo> FromEtcShells(string etcShells, string? userShell, Func<string, bool> exists)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(userShell)) paths.Add(userShell.Trim());
        paths.AddRange(etcShells.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith('/')));
        var list = new List<ShellInfo>();
        foreach (var path in paths)
        {
            var name = path[(path.LastIndexOf('/') + 1)..];
            if (name is "" or "nologin" or "false" or "true" || !exists(path)) continue;
            if (list.Any(s => s.Id == name)) continue;
            list.Add(new(name, name, path, []));
        }
        return list;
    }

    private static string? OnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => System.IO.Path.Combine(d.Trim('"'), exe))
            .FirstOrDefault(File.Exists);

    private static string? FirstExisting(params string[] candidates) =>
        candidates.FirstOrDefault(c => c.Length > 0 && File.Exists(c));
}
