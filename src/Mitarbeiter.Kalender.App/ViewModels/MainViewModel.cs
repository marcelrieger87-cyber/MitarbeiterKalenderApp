using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using Mitarbeiter.Kalender.App.Core.Abstractions;
using Mitarbeiter.Kalender.App.Core.Models;
using Mitarbeiter.Kalender.App.Domain.Entities;
using Mitarbeiter.Kalender.App.Domain.Enums;

namespace Mitarbeiter.Kalender.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ICalendarRepository _repo;
    private readonly ICalendarService _service;

    public ObservableCollection<Employee> Employees { get; } = new();

    private string _newEmployeeName = "";
    public string NewEmployeeName
    {
        get => _newEmployeeName;
        set => SetProperty(ref _newEmployeeName, value);
    }

    private string _customerFilter = "";
    public string CustomerFilter
    {
        get => _customerFilter;
        set => SetProperty(ref _customerFilter, value);
    }

    private int _year = DateTime.Today.Year;
    public int Year
    {
        get => _year;
        set
        {
            value = ClampYear(value);
            if (SetProperty(ref _year, value))
            {
                Raise(nameof(MonthTitle));
                _ = RefreshAsync();
            }
        }
    }

    private int _month = DateTime.Today.Month;
    public int Month
    {
        get => _month;
        set
        {
            value = ClampMonth(value);
            if (SetProperty(ref _month, value))
            {
                Raise(nameof(MonthTitle));
                _ = RefreshAsync();
            }
        }
    }

    public string MonthTitle => new DateTime(Year, Month, 1)
        .ToString("MMMM yyyy", CultureInfo.GetCultureInfo("de-DE"));

    private MonthView? _monthView;
    public MonthView? MonthView
    {
        get => _monthView;
        private set => SetProperty(ref _monthView, value);
    }

    public RelayCommand PrevMonthCommand { get; }
    public RelayCommand NextMonthCommand { get; }
    public RelayCommand TodayCommand { get; }

    public RelayCommand AddAppointmentCommand { get; }
    public RelayCommand DemoCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand SyncCommand { get; }
    public RelayCommand AddAbsenceCommand { get; }
    public RelayCommand StatusCommand { get; }
    public RelayCommand AddEmployeeCommand { get; }
    public RelayCommand RemoveEmployeeCommand { get; }

    public MainViewModel(ICalendarRepository repo, ICalendarService service)
    {
        _repo = repo;
        _service = service;

        PrevMonthCommand = new RelayCommand(PrevMonth);
        NextMonthCommand = new RelayCommand(NextMonth);
        TodayCommand = new RelayCommand(Today);

        AddAppointmentCommand = new RelayCommand(() => _ = AddQuickAppointmentAsync());
        DemoCommand = new RelayCommand(() => _ = SeedDemoAsync());

        EditCommand = new RelayCommand(() => MessageBox.Show("Termin ändern: kommt als nächster Schritt (Zelle auswählen → Dialog)."));
        DeleteCommand = new RelayCommand(() => MessageBox.Show("Termin löschen: kommt als nächster Schritt (Zelle auswählen → löschen)."));
        SyncCommand = new RelayCommand(() => MessageBox.Show("SYNC: kommt als nächster Schritt."));
        AddAbsenceCommand = new RelayCommand(() => _ = AddQuickAbsenceAsync());
        StatusCommand = new RelayCommand(() => MessageBox.Show("Status ändern: kommt als nächster Schritt."));

        AddEmployeeCommand = new RelayCommand(() => _ = AddEmployeeAsync());
        RemoveEmployeeCommand = new RelayCommand(() => _ = RemoveLastEmployeeAsync());

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _repo.InitializeAsync();

            var emps = (await _repo.GetEmployeesAsync()).ToList();

            // Wenn noch keiner existiert: 1 Default anlegen (damit du sofort testen kannst)
            if (emps.Count == 0)
            {
                emps.Add(new Employee(Guid.NewGuid().ToString("N"), "Marcel", true));
                await _repo.SaveEmployeesAsync(emps);
            }

            Employees.Clear();
            foreach (var e in emps)
                Employees.Add(e);

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Init Fehler:\n" + ex.Message);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            MonthView = await _service.BuildMonthViewAsync(Year, Month, employeeIdFilter: null);

            // Sync Employees from MonthView
            if (MonthView is not null && MonthView.Employees.Count > 0)
            {
                Employees.Clear();
                foreach (var e in MonthView.Employees)
                    Employees.Add(e);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Refresh Fehler:\n" + ex.Message);
        }
    }

    private void PrevMonth()
    {
        var dt = new DateTime(Year, Month, 1).AddMonths(-1);
        Year = dt.Year;
        Month = dt.Month;
    }

    private void NextMonth()
    {
        var dt = new DateTime(Year, Month, 1).AddMonths(1);
        Year = dt.Year;
        Month = dt.Month;
    }

    private void Today()
    {
        Year = DateTime.Today.Year;
        Month = DateTime.Today.Month;
    }

    private async Task AddEmployeeAsync()
    {
        var name = (NewEmployeeName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Bitte Mitarbeitername eingeben.");
            return;
        }

        var emps = (await _repo.GetEmployeesAsync()).ToList();

        // Doppelname vermeiden (einfach)
        if (emps.Any(e => string.Equals(e.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("Mitarbeiter existiert schon.");
            return;
        }

        emps.Add(new Employee(Guid.NewGuid().ToString("N"), name, true));
        await _repo.SaveEmployeesAsync(emps);

        NewEmployeeName = "";
        await RefreshAsync();
    }

    private async Task RemoveLastEmployeeAsync()
    {
        var emps = (await _repo.GetEmployeesAsync()).ToList();
        if (emps.Count == 0)
        {
            MessageBox.Show("Keine Mitarbeiter vorhanden.");
            return;
        }

        // Einfach: letzten löschen (später bauen wir Auswahl + Löschen gezielt)
        emps.RemoveAt(emps.Count - 1);
        await _repo.SaveEmployeesAsync(emps);
        await RefreshAsync();
    }

    private async Task AddQuickAppointmentAsync()
    {
        var emps = (await _repo.GetEmployeesAsync()).ToList();
        if (emps.Count == 0)
        {
            MessageBox.Show("Bitte zuerst einen Mitarbeiter anlegen.");
            return;
        }

        // Nimm den ersten aktiven Mitarbeiter (später: Klick in Zelle)
        var emp = emps.FirstOrDefault(e => e.IsActive) ?? emps[0];

        var kunde = string.IsNullOrWhiteSpace(CustomerFilter) ? "Kunde (Demo)" : CustomerFilter.Trim();

        // Quick Add: 1. Tag 09:00-10:00
        var date = new DateOnly(Year, Month, 1);
        var start = new TimeOnly(9, 0);
        var end = new TimeOnly(10, 0);

        var appt = new Appointment(
            Id: Guid.NewGuid().ToString("N"),
            EmployeeId: emp.Id,
            Date: date,
            Start: start,
            End: end,
            CustomerName: kunde,
            Status: AppointmentStatus.Fixed
        );

        await _service.UpsertAppointmentAsync(appt);
        await RefreshAsync();
    }

    private async Task AddQuickAbsenceAsync()
    {
        var emps = (await _repo.GetEmployeesAsync()).ToList();
        if (emps.Count == 0)
        {
            MessageBox.Show("Bitte zuerst einen Mitarbeiter anlegen.");
            return;
        }

        var emp = emps.FirstOrDefault(e => e.IsActive) ?? emps[0];
        var date = new DateOnly(Year, Month, Math.Min(2, DateTime.DaysInMonth(Year, Month)));

        var ab = new Absence(
            Id: Guid.NewGuid().ToString("N"),
            EmployeeId: emp.Id,
            Date: date,
            Type: AbsenceType.Vacation,
            Note: "Urlaub"
        );

        await _repo.UpsertAbsenceAsync(ab);
        await RefreshAsync();
    }

    private async Task SeedDemoAsync()
    {
        try
        {
            var emps = (await _repo.GetEmployeesAsync()).ToList();

            // Wenn zu wenig da sind, ergänzen wir, damit es „Excel-mäßig“ wirkt
            if (emps.All(e => e.DisplayName != "Marcel"))
                emps.Add(new Employee(Guid.NewGuid().ToString("N"), "Marcel", true));
            if (emps.All(e => e.DisplayName != "Heike"))
                emps.Add(new Employee(Guid.NewGuid().ToString("N"), "Heike", true));

            await _repo.SaveEmployeesAsync(emps);

            var daysInMonth = DateTime.DaysInMonth(Year, Month);
            var rand = new Random(42);
            var kunden = new[] { "VW AG", "Kunde Müller", "Kunde Schmidt" };

            foreach (var emp in emps.Where(e => e.IsActive))
            {
                for (int i = 0; i < 6; i++)
                {
                    var day = 1 + rand.Next(0, Math.Max(1, daysInMonth - 1));
                    var date = new DateOnly(Year, Month, day);

                    var startHour = 8 + rand.Next(0, 8);
                    var start = new TimeOnly(startHour, rand.Next(0, 2) == 0 ? 0 : 30);
                    var end = start.AddMinutes(60);

                    var status = (AppointmentStatus)rand.Next(0, 4);
                    var kunde = kunden[i % kunden.Length];

                    var appt = new Appointment(
                        Id: Guid.NewGuid().ToString("N"),
                        EmployeeId: emp.Id,
                        Date: date,
                        Start: start,
                        End: end,
                        CustomerName: kunde,
                        Status: status
                    );

                    await _service.UpsertAppointmentAsync(appt);
                }
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Demo Fehler:\n" + ex.Message);
        }
    }

    private static int ClampMonth(int m) => m < 1 ? 1 : (m > 12 ? 12 : m);
    private static int ClampYear(int y) => y < 2000 ? 2000 : (y > 2100 ? 2100 : y);
}
