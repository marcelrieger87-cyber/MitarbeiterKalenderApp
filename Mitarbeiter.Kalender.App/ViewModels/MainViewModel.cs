using System.Collections.ObjectModel;
using System.Windows;
using Mitarbeiter.Kalender.App.Core.Abstractions;
using Mitarbeiter.Kalender.App.Domain.Entities;
using Mitarbeiter.Kalender.App.Domain.Enums;

namespace Mitarbeiter.Kalender.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ICalendarService _calendar;
    private readonly ICalendarRepository _repo;

    private DateTime _selectedMonth;
    private EmployeeOption _selectedEmployee;
    private string _customerText = string.Empty;
    private DayCellViewModel? _selectedDay;

    public ObservableCollection<EmployeeOption> Employees { get; } = new();
    public ObservableCollection<DayCellViewModel> Cells { get; } = new();

    public DateTime SelectedMonth
    {
        get => _selectedMonth;
        set
        {
            if (SetProperty(ref _selectedMonth, new DateTime(value.Year, value.Month, 1)))
                _ = ReloadAsync();
        }
    }

    public EmployeeOption SelectedEmployee
    {
        get => _selectedEmployee;
        set
        {
            if (SetProperty(ref _selectedEmployee, value))
                _ = ReloadAsync();
        }
    }

    public string CustomerText
    {
        get => _customerText;
        set
        {
            if (SetProperty(ref _customerText, value))
                _ = ReloadAsync();
        }
    }

    public DayCellViewModel? SelectedDay
    {
        get => _selectedDay;
        set => SetProperty(ref _selectedDay, value);
    }

    public RelayCommand PrevMonthCommand { get; }
    public RelayCommand NextMonthCommand { get; }
    public RelayCommand TodayCommand { get; }
    public RelayCommand AddAppointmentCommand { get; }
    public RelayCommand AddAbsenceCommand { get; }
    public RelayCommand SeedDemoCommand { get; }
    public RelayCommand<DayCellViewModel> SelectDayCommand { get; }

    public MainViewModel(ICalendarService calendar, ICalendarRepository repo)
    {
        _calendar = calendar;
        _repo = repo;

        _selectedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _selectedEmployee = EmployeeOption.All;

        PrevMonthCommand = new RelayCommand(() => SelectedMonth = SelectedMonth.AddMonths(-1));
        NextMonthCommand = new RelayCommand(() => SelectedMonth = SelectedMonth.AddMonths(1));
        TodayCommand = new RelayCommand(() => SelectedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));

        AddAppointmentCommand = new RelayCommand(AddAppointment);
        AddAbsenceCommand = new RelayCommand(AddAbsence);
        SeedDemoCommand = new RelayCommand(async () => await SeedDemoAsync());

        SelectDayCommand = new RelayCommand<DayCellViewModel>(cell =>
        {
            if (cell is null) return;
            if (SelectedDay is not null) SelectedDay.IsSelected = false;
            SelectedDay = cell;
            SelectedDay.IsSelected = true;
        });

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _repo.InitializeAsync();

        Employees.Clear();
        Employees.Add(EmployeeOption.All);
        foreach (var e in await _repo.GetEmployeesAsync())
            Employees.Add(new EmployeeOption(e.Id, e.DisplayName));

        SelectedEmployee = EmployeeOption.All;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var view = await _calendar.BuildMonthViewAsync(SelectedMonth.Year, SelectedMonth.Month, SelectedEmployee.Id);

        Cells.Clear();
        foreach (var c in view.Cells)
        {
            var chips = c.Appointments
                .Where(a => string.IsNullOrWhiteSpace(CustomerText)
                    || a.CustomerName.Contains(CustomerText, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Start)
                .Select(a => new AppointmentChipViewModel(
                    title: FormatTitle(a, SelectedEmployee.Id is null),
                    timeRange: $"{a.Start:HH\:mm}-{a.End:HH\:mm}",
                    employeeId: a.EmployeeId,
                    status: a.Status))
                .ToList();

            var abs = c.Absences
                .Select(a => $"{a.EmployeeId}: {a.Type}")
                .ToList();

            Cells.Add(new DayCellViewModel(c.Date, c.IsInCurrentMonth, chips, abs));
        }

        // keep selection if possible
        if (SelectedDay is not null)
        {
            var match = Cells.FirstOrDefault(x => x.Date == SelectedDay.Date);
            if (match is not null)
            {
                SelectedDay = match;
                match.IsSelected = true;
            }
        }

        static string FormatTitle(Appointment a, bool showEmployee)
            => showEmployee ? $"{a.EmployeeId} · {a.CustomerName}" : a.CustomerName;
    }

    private async void AddAppointment()
    {
        var day = SelectedDay?.Date ?? DateOnly.FromDateTime(DateTime.Today);
        var employeeId = SelectedEmployee.Id ?? (Employees.FirstOrDefault(e => e.Id is not null)?.Id ?? "MA1");

        var customer = string.IsNullOrWhiteSpace(CustomerText) ? "Neuer Kunde" : CustomerText.Trim();
        var start = new TimeOnly(9, 0);
        var end = new TimeOnly(10, 0);

        var appt = new Appointment(
            Id: Guid.NewGuid().ToString("N"),
            EmployeeId: employeeId!,
            Date: day,
            Start: start,
            End: end,
            CustomerName: customer,
            Status: AppointmentStatus.Normal
        );

        await _calendar.UpsertAppointmentAsync(appt);
        await ReloadAsync();
    }

    private async void AddAbsence()
    {
        var day = SelectedDay?.Date ?? DateOnly.FromDateTime(DateTime.Today);
        var employeeId = SelectedEmployee.Id ?? (Employees.FirstOrDefault(e => e.Id is not null)?.Id ?? "MA1");

        var absence = new Absence(
            Id: Guid.NewGuid().ToString("N"),
            EmployeeId: employeeId!,
            Date: day,
            Type: AbsenceType.Vacation,
            Note: ""
        );

        await _repo.UpsertAbsenceAsync(absence);
        await ReloadAsync();
    }

    private async Task SeedDemoAsync()
    {
        // A few demo entries to make the UI immediately look alive.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthStart = new DateOnly(SelectedMonth.Year, SelectedMonth.Month, 1);
        var d1 = monthStart.AddDays(2);
        var d2 = monthStart.AddDays(7);
        var d3 = monthStart.AddDays(12);

        await _calendar.UpsertAppointmentAsync(new Appointment(
            Id: Guid.NewGuid().ToString("N"),
            EmployeeId: "MA1",
            Date: d1,
            Start: new TimeOnly(8, 0),
            End: new TimeOnly(9, 30),
            CustomerName: "Müller GmbH",
            Status: AppointmentStatus.Fixed
        ));

        await _calendar.UpsertAppointmentAsync(new Appointment(
            Id: Guid.NewGuid().ToString("N"),
            EmployeeId: "MA2",
            Date: d2,
            Start: new TimeOnly(10, 0),
            End: new TimeOnly(11, 0),
            CustomerName: "Schmidt",
            Status: AppointmentStatus.Normal
        ));

        await _calendar.UpsertAppointmentAsync(new Appointment(
            Id: Guid.NewGuid().ToString("N"),
            EmployeeId: "MA3",
            Date: d3,
            Start: new TimeOnly(13, 0),
            End: new TimeOnly(14, 0),
            CustomerName: "Kunde X",
            Status: AppointmentStatus.Tentative
        ));

        await _repo.UpsertAbsenceAsync(new Absence(
            Id: Guid.NewGuid().ToString("N"),
            EmployeeId: "MA2",
            Date: monthStart.AddDays(14),
            Type: AbsenceType.Sick
        ));

        await ReloadAsync();
        MessageBox.Show("Demo-Daten wurden angelegt.", "Mitarbeiter Kalender");
    }
}
