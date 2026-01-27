using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MitarbeiterKalenderApp.Core.Domain;

namespace MitarbeiterKalenderApp.Wpf.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private string monthTitle = "";
    [ObservableProperty] private string searchText = "";

    public ObservableCollection<Employee> Employees { get; } = new();
    [ObservableProperty] private Employee? selectedEmployee;

    public ObservableCollection<DayHeaderVm> DayHeaders { get; } = new();
    public ObservableCollection<EmployeeRowVm> EmployeeRows { get; } = new();

    private int _year;
    private int _month;

    public MainViewModel()
    {
        // Demo-Daten (später: aus DB)
        Employees.Add(new Employee { Name = "Max Mustermann", ShortCode = "MM" });
        Employees.Add(new Employee { Name = "Lisa Beispiel", ShortCode = "LB" });
        Employees.Add(new Employee { Name = "Tom Kalender", ShortCode = "TK" });

        SelectedEmployee = Employees.FirstOrDefault();

        var now = DateTime.Now;
        _year = now.Year;
        _month = now.Month;

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
    private void NewAppointment()
    {
        // später: Dialog wie Excel (Termin anlegen)
    }

    [RelayCommand]
    private void NewAbsence()
    {
        // später: Dialog wie Excel (Abwesenheit)
    }

    [RelayCommand]
    private void ExportExcel()
    {
        // später: Excel Export
    }

    [RelayCommand]
    private void ExportPdf()
    {
        // später: PDF Export
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

        // Excel-Feeling: alle Mitarbeiter untereinander
        foreach (var e in Employees)
        {
            var row = new EmployeeRowVm
            {
                Name = e.Name,
                Meta = e.ShortCode ?? ""
            };

            // pro Tag eine Zelle (hier Demo-Füllung)
            var start = new DateTime(_year, _month, 1);
            var end = start.AddMonths(1).AddDays(-1);

            for (var dt = start; dt <= end; dt = dt.AddDays(1))
            {
                var isWeekend = dt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

                row.DayCells.Add(new DayCellVm
                {
                    Line1 = isWeekend ? "" : "",
                    Line2 = isWeekend ? "Wochenende" : "",
                    Background = isWeekend ? new SolidColorBrush(Color.FromRgb(250, 250, 250)) : Brushes.White
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
