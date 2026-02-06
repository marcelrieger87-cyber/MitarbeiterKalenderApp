namespace Mitarbeiter.Kalender.App.ViewModels;

public sealed class DayCellViewModel : ObservableObject
{
    private bool _isSelected;

    public DateOnly Date { get; }
    public bool IsInCurrentMonth { get; }

    public string DayNumber => Date.Day.ToString();
    public string DayLabel => Date.ToDateTime(TimeOnly.MinValue).ToString("ddd");

    public IReadOnlyList<AppointmentChipViewModel> Appointments { get; }
    public IReadOnlyList<string> Absences { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public DayCellViewModel(DateOnly date, bool isInCurrentMonth,
        IReadOnlyList<AppointmentChipViewModel> appointments,
        IReadOnlyList<string> absences)
    {
        Date = date;
        IsInCurrentMonth = isInCurrentMonth;
        Appointments = appointments;
        Absences = absences;
    }
}
