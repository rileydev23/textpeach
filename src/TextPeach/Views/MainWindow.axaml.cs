using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using TextPeach.ViewModels;

namespace TextPeach.Views;

public partial class MainWindow : Window
{
    private bool _confirmedClose;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        });
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_confirmedClose && DataContext is MainWindowViewModel { HasUnsavedChanges: true })
        {
            e.Cancel = true;
            if (await Dialogs.Confirm("Cambios sin guardar", "Hay archivos con cambios sin guardar. ¿Salir y descartarlos?", "Salir"))
            {
                _confirmedClose = true;
                Close();
            }
            return;
        }
        base.OnClosing(e);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var files = e.Data.GetFiles();
        if (files is null) return;
        foreach (var item in files)
        {
            var path = item.TryGetLocalPath();
            if (path is null) continue;
            if (Directory.Exists(path)) await vm.Home.AddOrImportAsync(path);
            else if (File.Exists(path) && vm.SelectedTab is ProjectViewModel p &&
                     path.StartsWith(p.RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                p.OpenFile(Path.GetRelativePath(p.RootPath, path).Replace('\\', '/'));
        }
    }
}
