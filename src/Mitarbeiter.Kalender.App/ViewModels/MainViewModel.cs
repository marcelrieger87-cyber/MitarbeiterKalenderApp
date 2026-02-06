using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using Mitarbeiter.Kalender.App.Core.Abstractions;
using Mitarbeiter.Kalender.App.Core.Models;
using Mitarbeiter.Kalender.App.Core.Services;
using Mitarbeiter.Kalender.App.Domain.Entities;
using Mitarbeiter.Kalender.App.Domain.Enums;
using Mitarbeiter.Kalender.App.Infrastructure.Sqlite;

namespace Mitarbeiter.Kalender.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ICalendarRepository _repo;
    private readonly ICalendarService _service;

    public ObservableCollection<Employee> Employees { get; } = new();

    private Employee? _selectedEmployee;
    public Employee? SelectedEmployee
    {
        get => _selectedEmployee;
        set
        {
            if (SetProperty(ref _selectedEmployee, value))
                _ = RefreshAsync();
        }
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
            if (SetProperty(ref _year, ClampYear(value)))
                _ = RefreshAsync();
        }
    }

    private int _month = DateTime.Today.Month;
    public int Month
    {
        get => _month;
        set
        {
            if (SetProperty(ref _month, ClampMonth(value)))
                _ = RefreshAsync();
        }
    }

    public string MonthTitle
        => $"{new DateTime(Year, Month, 1).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("de-DE"))}";

    private MonthView? _monthView;
    public MonthView? MonthView
    {
        get => _monthView;
        private set
        {
            if (SetProperty(ref _monthView, value))
                OnPropertyChanged(nameof(MonthTitle));
        }
    }

    // Commands referenced by MainWindow.xaml
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

    public MainViewModel()
    {
        // SQLite backend (local user profile)
        _repo = new SqliteCalendarRepository(SqlitePaths.GetDefaultDbPath());
        _service = new CalendarService(_repo);

        PrevMonthCommand = new RelayCommand(_ => PrevMonth());
        NextMonthCommand = new RelayCommand(_ => NextMonth());
        TodayCommand = new RelayCommand(_ => Today());

        AddAppointmentCommand = new RelayCommand(_ => _ = AddQuickAppointmentAsync());
        DemoCommand = new RelayCommand(_ => _ = SeedDemoAsync());

        EditCommand = new RelayCommand(_ => MessageBox.Show("Termin ändern: kommt als nächster Schritt (Dialog wie Excel)."));
        DeleteCommand = new RelayCommand(_ => MessageBox.Show("Termin löschen: kommt als nächster Schritt (Zelle auswählen → löschen)."));
        SyncCommand = new RelayCommand(_ => MessageBox.Show("SYNC: kommt als nächster Schritt (Kundenliste/Abgleich)."));
        AddAbsenceCommand = new RelayCommand(_ => _ = AddQuickAbsenceAsync());
        StatusCommand = new RelayCommand(_ => MessageBox.Show("Status ändern: kommt als nächster Schritt (Fix/Normal/Cancelled/Tentative)."));

        AddEmployeeCommand = new RelayCommand(_ => _ = AddEmployeeAsync());
        RemoveEmployeeCommand = new RelayCommand(_ => _ = RemoveEmployeeAsync());

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _repo.InitializeAsync();

            var emps = await _repo.GetEmployeesAsync();
            Employees.Clear();
            foreach (var e in emps)
                Employees.Add(e);

            SelectedEmployee ??= Employees.FirstOrDefault();

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
            var mv = await _service.BuildMonthViewAsync(Year, Month, employeeIdFilter: null);
            MonthView = mv;

            // Keep Employees list in sync with repository (in case of seed/add/remove)
            if (mv.Employees.Count > 0)
            {
                Employees.Clear();
                foreach (var e in mv.Employees)
                    Employees.Add(e);

                if (SelectedEmployee is null)
                    SelectedEmployee = Employees.FirstOrDefault();
                else
                    SelectedEmployee = Employees.FirstOrDefault(x => x.Id == SelectedEmployee.Id) ?? Employees.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            // If something breaks, you still see the reason
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

    private async Task AddQuickAppointmentAsync()
    {
        if (SelectedEmployee is null)
        {
            MessageBox.Show("Bitte zuerst einen Mitarbeiter auswählen.");
            return;
        }

        // Simple “Excel-like quick add”: first day, 09:00–10:00
        var date = new DateOnly(Year
