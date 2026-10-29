using System.Diagnostics;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using TextPeach.Services;
using TextPeach.Views;

namespace TextPeach.ViewModels;

/// <summary>The tree's context menu: the usual file actions (new, rename, copy, cut, paste, duplicate, delete) and more.</summary>
public partial class ProjectViewModel
{
    /// <summary>
    /// What "Copiar" / "Cortar" took, shared by every project so a file can go from one to another. The system clipboard
    /// gets the same files, so pasting in the Explorer works too; whatever the clipboard holds when pasting wins.
    /// </summary>
    private static (string[] Paths, bool Cut)? _fileClip;

    public IReadOnlyList<MenuAction> MenuForNode(FileNode? node)
    {
        if (node is { IsPlaceholder: true }) node = null;
        var items = new List<MenuAction>();
        if (node is { IsDirectory: false })
        {
            items.Add(MenuAction.Sync(Loc.T("files.open"), () => PinNode(node)));
            items.Add(MenuAction.Sync(Loc.T("common.openDefault"), () => OpenExternal(node.FullPath)));
            items.Add(MenuAction.Separator);
        }
        items.Add(new MenuAction(Loc.T("files.newFile"), () => NewFileAsync(node)));
        items.Add(new MenuAction(Loc.T("files.newFolder"), () => NewFolderAsync(node)));
        items.Add(MenuAction.Separator);
        if (node is not null)
        {
            items.Add(new MenuAction(Loc.T("files.cut"), () => ClipAsync(node, cut: true), "Ctrl+X"));
            items.Add(new MenuAction(Loc.T("files.copy"), () => ClipAsync(node, cut: false), "Ctrl+C"));
        }
        items.Add(new MenuAction(Loc.T("files.paste"), () => PasteAsync(node), "Ctrl+V"));
        if (node is not null) items.Add(MenuAction.Sync(Loc.T("files.duplicate"), () => Duplicate(node)));
        items.Add(MenuAction.Separator);
        if (node is not null)
        {
            items.Add(new MenuAction(Loc.T("pv.copyFull"), () => CopyTextAsync(node.FullPath)));
            items.Add(new MenuAction(Loc.T("pv.copyRel"), () => CopyTextAsync(node.RelPath)));
        }
        items.Add(MenuAction.Sync(Loc.T(OperatingSystem.IsWindows() ? "files.reveal" : "files.revealFolder"), () => RevealInSystem(node)));
        var here = DirOf(node);
        items.Add(Console.Shells.Count > 1
            ? MenuAction.Submenu(Loc.T("files.consoleHere"), Console.Shells.Select(s => MenuAction.Sync(s.Name, () => OpenConsoleAt(here, s))).ToList())
            : MenuAction.Sync(Loc.T("files.consoleHere"), () => OpenConsoleAt(here)));
        items.Add(MenuAction.Separator);
        if (node is { IsDirectory: true })
            items.Add(MenuAction.Sync(Loc.T("ignore.folder"), () => IgnoreFolder(node)));
        items.Add(new MenuAction(Loc.T("ignore.edit"), EditIgnoredAsync));
        if (node is not null)
        {
            items.Add(MenuAction.Separator);
            items.Add(new MenuAction(Loc.T("files.rename"), () => RenameAsync(node), "F2"));
            items.Add(new MenuAction(Loc.T("files.delete"), () => DeleteAsync(node), "Delete"));
        }
        return MenuList.Clean(items);
    }

    /// <summary>The folder an action lands in: the node itself if it is a folder, its folder if it is a file, else the root.</summary>
    private string DirOf(FileNode? node) =>
        node is null ? RootPath : node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath)!;

    private string RelOf(string full) => Path.GetRelativePath(RootPath, full).Replace('\\', '/');

    // ------------------------------------------------------------ new

    private async Task NewFileAsync(FileNode? node)
    {
        var dir = DirOf(node);
        var name = await Dialogs.Prompt(Loc.T("files.newFile.title"), Loc.F("files.newFile.label", RelOrRoot(dir)));
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Try(() => FileOps.CreateFile(dir, name.Trim())) is not { } full) return;
        AfterDiskChange(full);
        OpenFile(RelOf(full));
        EditorFocusRequested?.Invoke();
    }

    private async Task NewFolderAsync(FileNode? node)
    {
        var dir = DirOf(node);
        var name = await Dialogs.Prompt(Loc.T("files.newFolder.title"), Loc.F("files.newFolder.label", RelOrRoot(dir)));
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Try(() => FileOps.CreateFolder(dir, name.Trim())) is { } full) AfterDiskChange(full);
    }

    private string RelOrRoot(string dir) => FileOps.IsSameOrInside(dir, RootPath) && RelOf(dir) is var rel && rel != "." ? rel + "/" : Title + "/";

    // ------------------------------------------------------------ rename / delete

    public async Task RenameAsync(FileNode node)
    {
        if (Busy(node.FullPath)) return;
        var name = await Dialogs.Prompt(Loc.T("files.rename.title"), Loc.F("files.rename.label", node.RelPath), node.Name);
        if (string.IsNullOrWhiteSpace(name) || name == node.Name) return;
        if (Try(() => FileOps.Rename(node.FullPath, name.Trim())) is not { } full) return;
        RelocateDocs(node.FullPath, full);
        AfterDiskChange(full);
        Notice = Loc.F("files.renamed", node.Name, Path.GetFileName(full));
    }

    public async Task DeleteAsync(FileNode node)
    {
        if (Busy(node.FullPath)) return;
        var what = node.IsDirectory ? Loc.F("files.delete.folder", node.RelPath) : node.RelPath;
        if (!await Dialogs.Confirm(Loc.T("files.delete.title"), Loc.F("files.delete.text", what), Loc.T("files.delete.ok"))) return;
        var path = node.FullPath;
        bool trashed;
        try { trashed = await Task.Run(() => FileOps.TryTrash(path)); }
        catch (Exception ex) { Notice = ex.Message; return; }
        if (!trashed)
        {
            // No trash to send it to: deleting for good needs its own, explicit yes.
            if (!await Dialogs.Confirm(Loc.T("files.delete.title"), Loc.F("files.delete.forever", what), Loc.T("files.delete.ok"))) return;
            try { await Task.Run(() => FileOps.DeleteForever(path)); }
            catch (Exception ex) { Notice = ex.Message; return; }
        }
        RelocateDocs(path, null);
        AfterDiskChange(null);
        Notice = Loc.F(trashed ? "files.trashed" : "files.deleted", node.RelPath);
    }

    // ------------------------------------------------------------ clipboard

    public async Task ClipAsync(FileNode node, bool cut)
    {
        _fileClip = ([node.FullPath], cut);
        if (Dialogs.Owner is { Clipboard: { } cb, StorageProvider: { } sp })
        {
            try
            {
                IStorageItem? item = node.IsDirectory ? await sp.TryGetFolderFromPathAsync(node.FullPath) : await sp.TryGetFileFromPathAsync(node.FullPath);
                if (item is not null) await cb.SetFilesAsync([item]);
                else await cb.SetTextAsync(node.FullPath);
            }
            catch { /* the in-app copy still works */ }
        }
        Notice = Loc.F(cut ? "files.cutDone" : "files.copyDone", node.RelPath);
    }

    public async Task PasteAsync(FileNode? node)
    {
        var dir = DirOf(node);
        var (paths, cut) = await ReadClipAsync();
        if (paths.Length == 0) { Notice = Loc.T("files.clipEmpty"); return; }
        if (cut && paths.Any(Busy)) return;
        string? last = null;
        try
        {
            foreach (var src in paths)
            {
                if (!FileOps.Exists(src)) { Notice = Loc.F("pv.notFound", src); continue; }
                var target = await Task.Run(() => cut ? FileOps.MoveInto(src, dir) : FileOps.CopyInto(src, dir, Loc.T("files.copySuffix")));
                if (cut) RelocateDocs(src, target);
                last = target;
            }
        }
        catch (Exception ex) { Notice = ex.Message; }
        // What was cut is gone from where it was: pasting it again would find nothing to move.
        if (cut) _fileClip = null;
        if (last is null) return;
        AfterDiskChange(last);
        Notice = paths.Length == 1 ? Loc.F("files.pasted.one", RelOf(last)) : Loc.F("files.pasted.many", paths.Length);
    }

    /// <summary>
    /// The files to paste: those on the system clipboard (copied here or in the Explorer), cut only if they are exactly
    /// what was cut here; otherwise what was copied here.
    /// </summary>
    private static async Task<(string[] Paths, bool Cut)> ReadClipAsync()
    {
        string[] system = [];
        if (Dialogs.Owner?.Clipboard is { } cb)
        {
            try
            {
                system = (await cb.TryGetFilesAsync() ?? [])
                    .Select(i => i.TryGetLocalPath()).OfType<string>().ToArray();
            }
            catch { /* unreadable clipboard: fall back to the in-app copy */ }
        }
        if (system.Length == 0) return _fileClip is { } c ? (c.Paths, c.Cut) : ([], false);
        bool cut = _fileClip is { Cut: true } clip && clip.Paths.Order().SequenceEqual(system.Order(), StringComparer.OrdinalIgnoreCase);
        return (system, cut);
    }

    private void Duplicate(FileNode node)
    {
        var dir = Path.GetDirectoryName(node.FullPath)!;
        if (Try(() => FileOps.CopyInto(node.FullPath, dir, Loc.T("files.copySuffix"))) is { } full)
        {
            AfterDiskChange(full);
            Notice = Loc.F("files.duplicated", RelOf(full));
        }
    }

    private async Task CopyTextAsync(string text)
    {
        if (Dialogs.Owner?.Clipboard is not { } cb) return;
        await cb.SetTextAsync(text);
        Notice = Loc.F("pv.copied", text);
    }

    // ------------------------------------------------------------ the system

    /// <summary>The file selected in the Explorer (Windows) or its folder opened in the file manager.</summary>
    private void RevealInSystem(FileNode? node)
    {
        var target = node?.FullPath ?? RootPath;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", node is null ? $"\"{target}\"" : $"/select,\"{target}\"") { UseShellExecute = false });
            else
                OpenExternal(node is null || node.IsDirectory ? target : Path.GetDirectoryName(target)!);
        }
        catch (Exception ex) { Notice = ex.Message; }
    }

    // ------------------------------------------------------------ keeping open files in step

    /// <summary>A file of <paramref name="full"/> (or under it) is being edited: renaming, moving or deleting it waits.</summary>
    private bool Busy(string full)
    {
        if (Docs.OfType<OpenFileViewModel>().FirstOrDefault(d => (d.IsDirty || d.IsEditing) && FileOps.IsSameOrInside(d.FullPath, full)) is not { } busy)
            return false;
        Notice = Loc.F("files.busy", busy.RelPath);
        return true;
    }

    /// <summary>
    /// Open files at or under <paramref name="from"/> follow it to <paramref name="to"/> (a rename or a move inside the
    /// project), or close (a delete, or a move out of the project).
    /// </summary>
    private void RelocateDocs(string from, string? to)
    {
        var moved = Docs.OfType<OpenFileViewModel>().Where(d => FileOps.IsSameOrInside(d.FullPath, from)).ToList();
        if (moved.Count == 0) return;
        var selected = SelectedFile;
        OpenFileViewModel? reselect = null;
        foreach (var d in moved)
        {
            _ = CloseDocAsync(d); // never editing here (see Busy), so this finishes at once
            if (to is null) continue;
            var target = to + d.FullPath[from.TrimEnd('\\', '/').Length..];
            if (!FileOps.IsSameOrInside(target, RootPath) || !File.Exists(target)) continue;
            var doc = OpenFile(RelOf(target), preview: d.IsPreview);
            if (d == selected) reselect = doc;
        }
        if (reselect is not null) SelectedDoc = reselect;
    }

    /// <summary>Shows a change made from the menu right away (the watcher would take a moment) and selects it.</summary>
    private void AfterDiskChange(string? select)
    {
        RebuildTree();
        _ = RescanAsync();
        ScheduleGitRefresh();
        if (select is not null && FileOps.IsSameOrInside(select, RootPath)) RevealInTree(RelOf(select));
    }

    private string? Try(Func<string> action)
    {
        try { return action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Notice = ex.Message;
            return null;
        }
    }
}
