using TextPeach.Services;

namespace TextPeach.ViewModels;

/// <summary>Single source for the keyboard shortcuts shown in the help dialog (F1).</summary>
public static class Shortcuts
{
    public static (string Gesture, string Description)[] Global =>
    [
        ("Ctrl+P", Loc.T("keys.ctrlP")),
        ("Ctrl+Shift+O", Loc.T("keys.ctrlShiftO")),
        ("Ctrl+Tab / Ctrl+Shift+Tab", Loc.T("keys.tabs")),
        ("Ctrl+1 … Ctrl+9", Loc.T("keys.tabN")),
        ("Ctrl+W / Ctrl+F4", Loc.T("keys.close")),
        ("F1", Loc.T("keys.about")),
    ];

    public static (string Gesture, string Description)[] Project =>
    [
        ("Ctrl+Shift+F", Loc.T("keys.searchProject")),
        ("Ctrl+Shift+E", Loc.T("keys.filterTree")),
        ("Ctrl+F", Loc.T("keys.findInFile")),
        ("Ctrl+G", Loc.T("keys.goToLine")),
        ("Ctrl+PageDown / PageUp", Loc.T("keys.nextFile")),
        ("Ctrl+E", Loc.T("keys.edit")),
        ("Ctrl+S", Loc.T("keys.save")),
        ("Ctrl + / Ctrl − / Ctrl+0", Loc.T("keys.zoom")),
        ("Alt+Z", Loc.T("keys.wrap")),
        ("F5", Loc.T("keys.refresh")),
        (Loc.T("keys.escFilter"), Loc.T("keys.clearFilter")),
    ];

    public static (string Title, (string Gesture, string Description)[] Items)[] Groups =>
        new[] { (Loc.T("keys.general"), Global), (Loc.T("keys.project"), Project) };
}
