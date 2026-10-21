using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TextPeach.Services;

namespace TextPeach.ViewModels;

/// <summary>A tab in the window strip (Proyectos / one per project) or in a project's file strip.</summary>
public abstract partial class TabViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _toolTip = "";
    /// <summary>Git state of a file tab; colors its title like the tree does.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleBrush))]
    private GitChange _change;
    public IBrush TitleBrush => GitLook.NameBrush(Change);
    /// <summary>Opened with a single click: shown in italics and replaced by the next file opened that way.</summary>
    [ObservableProperty] private bool _isPreview;
    public virtual bool Closable => true;
    public ICommand? CloseCommand { get; set; }
}
