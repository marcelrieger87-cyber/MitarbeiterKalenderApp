using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MitarbeiterKalenderApp.Core.Domain;
using MitarbeiterKalenderApp.Core.Services;


namespace MitarbeiterKalenderApp.Wpf.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly HolidayService _holidays = new();

    [ObservableProperty] private string monthTitle = "";
    [ObservableProperty] private string saveBasePath = @"C:\Users\%USERNAME%\OneDrive\Mitarbeiter_Kalender";

    public ObservableCollection<Employee> Employees { get; } = new();
    [ObservableProperty] private Employee? selectedEmployee;

    public ObservableCollection<Customer> Customers { get; } = new();
    [ObservableProperty] private Customer? selectedCustomer;

    public ObservableCollection<DayHeaderVm> DayHeaders { get; } = new();
    public ObservableCollection<EmployeeRowVm> EmployeeRows { get; } = new();

    private int _year;
    private int _month;

    private HashSet<DateOnly> _holidaySet = new();

    public MainViewModel()
    {
        // Demo-Mitarbeiter (später: DB)
        Employees.Add(new Employee { Name = "Max Mustermann", ShortCode = "MM" });
        Employees.Add(new Employee { Name = "Lisa Beispiel", ShortCode = "LB" });
        Employees.Add(new Employee { Name = "Tom Kalender", ShortCode = "TK" });
        SelectedEmployee = Employees.FirstOrDefault();

        // Demo-Kunden (später: aus Datei/DB)
        Customers.Add(new Customer { DisplayName = "Kunde A", CustomerNumber = "1001" });
        Customers.Add(new Customer { DisplayName = "Kunde B", CustomerNumber = "1002" });
        Customers.Add(new Customer { DisplayName = "Kunde C", CustomerNumber = "1003" });
        SelectedCustomer = Customers.FirstOrDefault();

        var now = DateTime.Now;
        _year = now.Year;
        _month = now.Month;

        RefreshAll();
    }

    private void RefreshAll()
    {
        _holidaySet = _holidays.GetHolidays(_year);

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
        RefreshAll();
    }

    [RelayCommand]
    private void NextMonth()
    {
        var d = new DateTime(_year, _month, 1).AddMonths(1);
        _year = d.Year;
        _month = d.Month;
        RefreshAll();
    }

    [RelayCommand]
    private void AddEmployee()
    {
        // Minimal-Input ohne extra Fenster
        var name = Microsoft.VisualBasic.Interaction.InputBox("Name des Mitarbeiters:", "Mitarbeiter anlegen", "");
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        var code = Microsoft.VisualBasic.Interaction.InputBox("Kürzel (optional):", "Mitarbeiter anlegen", "");
        code = (code ?? "").Trim();

        Employees.Add(new Employee { Name = name, ShortCode = string.IsNullOrWhiteSpace(code) ? null : code });
        SelectedEmployee = Employees.LastOrDefault();
        BuildRows();
    }

    [RelayCommand]
    private void DeleteEmployee()
    {
        if (SelectedEmployee is null) return;

        var res = MessageBox.Show($"Mitarbeiter '{SelectedEmployee.Name}' löschen?", "Bestätigung",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

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
        // Platzhalter: später echter Dialog + Klick in Zelle
        MessageBox.Show("Neuer Termin: kommt als nächstes (Dialog + Klick in Kalenderzelle).", "Info");
    }

    [RelayCommand]
    private void NewSeries()
    {
        MessageBox.Show("Serie erstellen: kommt als nächstes (wie Excel-Serienformular).", "Info");
    }

    [RelayCommand]
    private void NewAbsence()
    {
        MessageBox.Show("Abwesenheit: kommt als nächstes (wie Excel).", "Info");
    }

    [RelayCommand]
    private void ExportExcel()
    {
        MessageBox.Show("Export Excel: kommt als nächstes (ClosedXML).", "Info");
    }

    [RelayCommand]
    private void ExportPdf()
    {
        MessageBox.Show("Export PDF: kommt als nächstes (QuestPDF).", "Info");
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
            var day = DateOnly.FromDateTime(dt);
            var isWeekend = dt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var isHoliday = _holidaySet.Contains(day);

            DayHeaders.Add(new DayHeaderVm
            {
                Title = dt.ToString("dd.MM"),
                SubTitle = dt.ToString("ddd"),
                HolidayLabel = isHoliday ? "Feiertag" : "",
                Background = (isHoliday || isWeekend)
                    ? new SolidColorBrush(Color.FromRgb(232, 245, 233)) // hellgrün
                    : Brushes.White
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
                Meta = string.IsNullOrWhiteSpace(e.ShortCode) ? "" : $"Kürzel: {e.ShortCode}"
            };

            for (var dt = start; dt <= end; dt = dt.AddDays(1))
            {
                var day = DateOnly.FromDateTime(dt);
                var isWeekend = dt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var isHoliday = _holidaySet.Contains(day);

                // Demo-Inhalt (später: RenderService-Ergebnis je Mitarbeiter)
                var line2 = isHoliday ? "Feiertag" : (isWeekend ? "Wochenende" : "");
                var bg = (isHoliday || isWeekend)
                    ? new SolidColorBrush(Color.FromRgb(232, 245, 233)) // hellgrün
                    : Brushes.White;

                row.DayCells.Add(new DayCellVm
                {
                    Line1 = "",
                    Line2 = line2,
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
    public string HolidayLabel { get; init; } = "";
    public Brush Background { get; init; } = Brushes.White;
}
