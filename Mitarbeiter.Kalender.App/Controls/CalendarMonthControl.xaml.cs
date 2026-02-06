using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Mitarbeiter.Kalender.App.Controls;

public partial class CalendarMonthControl : UserControl
{
    public CalendarMonthControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(CalendarMonthControl),
        new PropertyMetadata(null));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectDayCommandProperty = DependencyProperty.Register(
        nameof(SelectDayCommand),
        typeof(ICommand),
        typeof(CalendarMonthControl),
        new PropertyMetadata(null));

    public ICommand? SelectDayCommand
    {
        get => (ICommand?)GetValue(SelectDayCommandProperty);
        set => SetValue(SelectDayCommandProperty, value);
    }
}
