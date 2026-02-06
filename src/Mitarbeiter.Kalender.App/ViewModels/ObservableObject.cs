using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Mitarbeiter.Kalender.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T backing, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(backing, value)) return false;
        backing = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
