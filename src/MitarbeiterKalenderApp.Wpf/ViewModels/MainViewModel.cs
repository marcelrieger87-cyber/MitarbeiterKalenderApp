using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MitarbeiterKalenderApp.Core.Domain;
using MitarbeiterKalenderApp.Core.Services;
using MitarbeiterKalenderApp.Wpf.Dialogs;

namespace MitarbeiterKalenderApp.Wpf.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly HolidayService _holidays = new();

    [ObservableProperty] private string monthTitle = "";
    [ObservableProperty] private string searchText = "";

    // Speichern/OneDrive Bezug (Eingabefeld)
    [ObservableProperty] private string storageBasePath =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Toggle für Feiertag-Markierung
    [ObservableProperty] private bool showHolidays = true;

    public ObservableCollection<Employee> Employees { get; } = new();
    [ObservableProperty] private Employee? selectedEmployee;

    public ObservableCollection<Customer> Customers { get; } = new();
    [ObservableProperty] private Customer? selectedCustomer;

    public ObservableCollection<DayHeaderVm> DayHeaders { get; } = new();
    public ObservableCollection<EmployeeRowVm> EmployeeRows { get; } = new();

    private int _year;
    private int _month;

    // Farben für Wochenende/Feiertag (hellgrün)
    private static readonly Brush WeekendBrush = new SolidColorBrush(Color.FromRgb(220, 252, 231)); // light green
    private static readonly Brush HolidayBrush = new SolidColorBrush(Color.FromRgb(187, 247, 208)); // a bit stronger
    private static readonly Brush NormalBrush = Brushes.White;

    public MainViewModel()
    {
        var now = DateTime.Now;
        _year = now.Year;
        _month = now.Month;

        // Demo-Daten (später aus Datei/DB)
        Employees.Add(new Employee { Name = "Max Mustermann", ShortCode = "MM" });
        Employees.Add(new Employee { Name = "Lisa Beispiel", ShortCode = "LB" });
        Employees.Add(new Employee { Name = "Tom Kalender", ShortCode = "TK" });
        SelectedEmployee = Employees.FirstOrDefault();

        Customers.Add(new Customer { DisplayName = "Kunde A" });
        Customers.Add(new Customer { DisplayName = "Kunde B" });
        Customers.Add(new Customer { DisplayName = "Kunde C" });
        SelectedCustomer = Customers.FirstOrDefault();

        BuildHeaders();
        BuildRows();
        UpdateTitle();
    }

    [RelayCommand]
    private void PrevMonth()
    {
        var d = new DateTime(_year, _month, 1).AddMonths(-1);
        _year = d.Year;
        _month = d.Month;
        BuildHeaders();
        BuildRows();
        UpdateTitle();
    }

    [RelayCommand]
    private void NextMonth()
    {
        var d = new DateTime(_year, _month, 1).AddMonths(1);
        _year = d.Year;
        _month = d.Month;
        BuildHeaders();
        BuildRows();
        UpdateTitle();
    }

    [RelayCommand]
    private void ToggleHolidays()
    {
        ShowHolidays = !ShowHolidays;
        BuildRows();
    }

    [RelayCommand]
    private void AddEmployee()
    {
        var dlg = new AddEmployeeDialog();
        if (dlg.ShowDialog() != true) return;

        var name = dlg.EmployeeName?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        Employees.Add(new Employee { Name = name });
        SelectedEmployee = Employees.LastOrDefault();
        BuildRows();
    }

    [RelayCommand]
    private void DeleteEmployee()
    {
        if (SelectedEmployee is null) return;

        var res = MessageBox.Show(
            $"Mitarbeiter '{SelectedEmployee.Name}' wirklich löschen?",
            "Bestätigen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (res != MessageBoxResult.Yes) return;

        Employees.Remove(SelectedEmployee);
        SelectedEmployee = Employees.FirstOrDefault();
        BuildRows();
    }

    [RelayCommand]
    private void SortEmployees()
    {
        var sorted = Employees.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Employees.Clear();
        foreach (var e in sorted) Employees.Add(e);
        SelectedEmployee = Employees.FirstOrDefault();
        BuildRows();
    }

    [RelayCommand]
    private void NewAppointment()
    {
        // Platzhalter: später echter Termin-Dialog wie Excel
        MessageBox.Show("Neuer Termin (Dialog kommt als nächstes).", "Info");
    }

    [RelayCommand]
    private void NewSeries()
    {
        // Platzhalter: später Serie-Dialog wie Excel
        MessageBox.Show("Serie erstellen (Dialog kommt als nächstes).", "Info");
    }

    [RelayCommand]
    private void ExportExcel()
    {
        MessageBox.Show($"Export Excel (kommt als nächstes). Basis: {StorageBasePath}", "Info");
    }

    [RelayCommand]
    private void ExportPdf()
    {
        MessageBox.Show($"Export PDF (kommt als nächstes). Basis: {StorageBasePath}", "Info");
    }

    [RelayCommand]
    private void Reload()
    {
        // später: Kunden/Mitarbeiter/Termine neu laden
        BuildHeaders();
        BuildRows();
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        MonthTitle = $"{new DateTime(_year, _month, 1):MMMM yyyy}";
    }

    private void BuildHeaders()
    {
        DayHeaders.Clear();
        var start = new DateTime(_year, _month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        for (var dt = start; dt <= end; dt = dt.AddDays(1))
        {
            DayHeaders.Add(new DayHeaderVm
            {
                Title = dt.ToString("dd.MM"),
                SubTitle = dt.ToString("ddd")
            });
        }
    }

    private void BuildRows()
    {
        EmployeeRows.Clear();

        var start = new DateTime(_year, _month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        foreach (var e in Employees)
        {
            var row = new EmployeeRowVm
            {
                Name = e.Name,
                Meta = string.IsNullOrWhiteSpace(e.ShortCode) ? "" : e.ShortCode!
            };

            for (var dt = start; dt <= end; dt = dt.AddDays(1))
            {
                var d = DateOnly.FromDateTime(dt);

                var isWeekend = dt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var isHoliday = ShowHolidays && _holidays.IsHoliday(d, out var holidayName);

                var bg = NormalBrush;
                var l2 = "";

                if (isHoliday)
                {
                    bg = HolidayBrush;
                    l2 = holidayName ?? "Feiertag";
                }
                else if (isWeekend)
                {
                    bg = WeekendBrush;
                    l2 = "Wochenende";
                }

                // Demo: später RenderService-Ergebnis hier rein mappen
                row.DayCells.Add(new DayCellVm
                {
                    Line1 = "",
                    Line2 = l2,
                    Background = bg
                });
            }

            EmployeeRows.Add(row);
        }
    }
}

public sealed class DayHeaderVm
{
    public string Title { get; init; } = "";
    public string SubTitle { get; init; } = "";
}
