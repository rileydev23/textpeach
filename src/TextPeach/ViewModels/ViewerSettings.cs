using CommunityToolkit.Mvvm.ComponentModel;
using TextPeach.Services;

namespace TextPeach.ViewModels;

/// <summary>Viewer preferences shared by every open project and persisted in the state file.</summary>
public sealed partial class ViewerSettings : ObservableObject
{
    public const double DefaultFontSize = 13, MinFontSize = 8, MaxFontSize = 32;

    private readonly ProjectStore _store;

    [ObservableProperty] private double _fontSize;
    [ObservableProperty] private bool _wordWrap;
    [ObservableProperty] private bool _showWhitespace;
    /// <summary>Diff view layout: the two versions side by side, or one column.</summary>
    [ObservableProperty] private bool _diffSideBySide;
    /// <summary>Where a project's console opens, and how its terminals share the space.</summary>
    [ObservableProperty] private ConsolePlace _consolePlace;
    [ObservableProperty] private ConsoleArrangement _consoleArrangement;
    [ObservableProperty] private double _consoleHeight;

    public ViewerSettings(ProjectStore store)
    {
        _store = store;
        _fontSize = Math.Clamp(store.State.FontSize, MinFontSize, MaxFontSize);
        _wordWrap = store.State.WordWrap;
        _diffSideBySide = store.State.DiffSideBySide;
        _consolePlace = Enum.TryParse<ConsolePlace>(store.State.ConsolePlace, ignoreCase: true, out var place) ? place : ConsolePlace.Panel;
        _consoleArrangement = Enum.TryParse<ConsoleArrangement>(store.State.ConsoleArrangement, ignoreCase: true, out var arr) ? arr : ConsoleArrangement.Rows;
        _consoleHeight = Math.Clamp(store.State.ConsoleHeight, 80, 2000);
    }

    partial void OnConsolePlaceChanged(ConsolePlace value)
    {
        _store.State.ConsolePlace = value.ToString().ToLowerInvariant();
        _store.Save();
    }

    partial void OnConsoleArrangementChanged(ConsoleArrangement value)
    {
        _store.State.ConsoleArrangement = value.ToString().ToLowerInvariant();
        _store.Save();
    }

    partial void OnConsoleHeightChanged(double value)
    {
        _store.State.ConsoleHeight = value;
        _store.Save();
    }

    partial void OnDiffSideBySideChanged(bool value)
    {
        _store.State.DiffSideBySide = value;
        _store.Save();
    }

    partial void OnFontSizeChanged(double value)
    {
        _store.State.FontSize = value;
        _store.Save();
    }

    partial void OnWordWrapChanged(bool value)
    {
        _store.State.WordWrap = value;
        _store.Save();
    }

    public void Zoom(int step) => FontSize = step == 0 ? DefaultFontSize : Math.Clamp(FontSize + step, MinFontSize, MaxFontSize);
}
