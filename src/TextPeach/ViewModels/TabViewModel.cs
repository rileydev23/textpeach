using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TextPeach.ViewModels;

/// <summary>A tab in the window strip (Proyectos / one per project) or in a project's file strip.</summary>
public abstract partial class TabViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _toolTip = "";
    public virtual bool Closable => true;
    public ICommand? CloseCommand { get; set; }
}
