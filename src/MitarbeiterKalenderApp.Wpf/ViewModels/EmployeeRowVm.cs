using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MitarbeiterKalenderApp.Wpf.ViewModels;

public sealed partial class EmployeeRowVm : ObservableObject
{
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string meta = "";

    public ObservableCollection<DayCellVm> DayCells { get; } = new();
}

public sealed class DayCellVm
{
    public string Line1 { get; init; } = "";
    public string Line2 { get; init; } = "";

    public Brush Background { get; init; } = System.Windows.Media.Brushes.White;
}
