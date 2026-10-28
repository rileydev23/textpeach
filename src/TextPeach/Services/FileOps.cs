using System.Diagnostics;

namespace TextPeach.Services;

/// <summary>
/// The file actions of the tree's context menu: new, rename, copy, move, duplicate and delete. Everything works on
/// full paths and throws <see cref="IOException"/> with a message fit for the status bar.
/// </summary>
public static class FileOps
{
    /// <summary>
    /// A name not taken in <paramref name="dir"/>, the way the Explorer does it: "a.txt", "a - copy.txt",
    /// "a - copy (2).txt". Folders keep their whole name ("src - copy").
    /// </summary>
    public static string FreeName(string dir, string name, string suffix, bool isDirectory = false)
    {
        if (!Exists(Path.Combine(dir, name))) return name;
        var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var ext = isDirectory ? "" : Path.GetExtension(name);
        // A dotfile (".gitignore") has no stem: the copy goes after the whole name.
        if (stem.Length == 0) { stem = name; ext = ""; }
        for (int i = 1; ; i++)
        {
            var candidate = stem + suffix + (i > 1 ? $" ({i})" : "") + ext;
            if (!Exists(Path.Combine(dir, candidate))) return candidate;
        }
    }

    /// <summary>Why <paramref name="name"/> cannot be a file or folder name, or null if it can.</summary>
    public static string? InvalidNameReason(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Loc.T("files.name.empty");
        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
            return Loc.F("files.name.invalid", name);
        if (name.EndsWith(' ') || name.EndsWith('.')) return Loc.F("files.name.invalid", name);
        return null;
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>
    /// <paramref name="relPath"/> may hold folders ("src/util/a.cs"): the missing ones are created. Returns the full path.
    /// </summary>
    public static string CreateFile(string dir, string relPath)
    {
        var full = Combine(dir, relPath);
        if (Exists(full)) throw new IOException(Loc.F("files.exists", relPath));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using (File.Create(full)) { }
        return full;
    }

    public static string CreateFolder(string dir, string relPath)
    {
        var full = Combine(dir, relPath);
        if (Exists(full)) throw new IOException(Loc.F("files.exists", relPath));
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>Renames in place. A change of case only ("readme.md" → "README.md") works on Windows too.</summary>
    public static string Rename(string path, string newName)
    {
        if (InvalidNameReason(newName) is { } why) throw new IOException(why);
        var target = Path.Combine(Path.GetDirectoryName(path)!, newName);
        bool caseOnly = string.Equals(path, target, StringComparison.OrdinalIgnoreCase);
        if (string.Equals(path, target, StringComparison.Ordinal)) return path;
        if (!caseOnly && Exists(target)) throw new IOException(Loc.F("files.exists", newName));
        if (caseOnly)
        {
            // Windows sees both names as the same entry: go through a temporary name.
            var temp = path + ".rename-" + Guid.NewGuid().ToString("N")[..8];
            MoveRaw(path, temp);
            MoveRaw(temp, target);
        }
        else MoveRaw(path, target);
        return target;
    }

    /// <summary>Copies a file or a whole folder into <paramref name="destDir"/>, with a free name if it is taken.</summary>
    public static string CopyInto(string source, string destDir, string copySuffix)
    {
        bool isDir = Directory.Exists(source);
        if (isDir && IsSameOrInside(destDir, source)) throw new IOException(Loc.T("files.intoItself"));
        var target = Path.Combine(destDir, FreeName(destDir, Path.GetFileName(source.TrimEnd('\\', '/')), copySuffix, isDir));
        if (isDir) CopyDirectory(source, target);
        else File.Copy(source, target);
        return target;
    }

    /// <summary>Moves a file or folder into <paramref name="destDir"/>; nothing happens if it already lives there.</summary>
    public static string MoveInto(string source, string destDir)
    {
        var name = Path.GetFileName(source.TrimEnd('\\', '/'));
        var target = Path.Combine(destDir, name);
        if (string.Equals(Path.GetDirectoryName(source.TrimEnd('\\', '/')), destDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return source;
        if (Directory.Exists(source) && IsSameOrInside(destDir, source)) throw new IOException(Loc.T("files.intoItself"));
        if (Exists(target)) throw new IOException(Loc.F("files.exists", name));
        MoveRaw(source, target);
        return target;
    }

    /// <summary>
    /// Sends to the recycle bin (Windows) or the trash (<c>gio trash</c> on Linux). False when there is no trash to
    /// use, so the caller can ask before deleting for good.
    /// </summary>
    public static bool TryTrash(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var ui = Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs;
            var bin = Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin;
            if (Directory.Exists(path)) Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, ui, bin);
            else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, ui, bin);
            return true;
        }
        try
        {
            using var p = Process.Start(new ProcessStartInfo("gio", ["trash", "--", path]) { UseShellExecute = false, RedirectStandardError = true });
            if (p is null) return false;
            p.WaitForExit(15000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    public static void DeleteForever(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else File.Delete(path);
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or somewhere below it.</summary>
    public static bool IsSameOrInside(string path, string folder)
    {
        var p = Path.GetFullPath(path).TrimEnd('\\', '/');
        var f = Path.GetFullPath(folder).TrimEnd('\\', '/');
        return string.Equals(p, f, StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Combine(string dir, string relPath)
    {
        var parts = relPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new IOException(Loc.T("files.name.empty"));
        foreach (var part in parts)
            if (InvalidNameReason(part) is { } why) throw new IOException(why);
        return Path.Combine([dir, .. parts]);
    }

    private static void MoveRaw(string source, string target)
    {
        if (Directory.Exists(source)) Directory.Move(source, target);
        else File.Move(source, target);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            // Links to folders are left out: a link that points back up would copy forever.
            if (new DirectoryInfo(dir).LinkTarget is not null) continue;
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }
}
