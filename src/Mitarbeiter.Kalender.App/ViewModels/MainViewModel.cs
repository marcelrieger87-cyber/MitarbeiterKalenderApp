using System.Collections.ObjectModel;
using System.Windows.Controls.Primitives;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mitarbeiter.Kalender.App.Controls; // ✅ WICHTIG: CalendarCellRef kommt aus ScheduleMonthControl.xaml.cs
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

    // VBA/Excel Buttons
    public RelayCommand AddAppointmentCommand { get; }
    public RelayCommand CreateSeriesCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand DeleteCommand { get; }

    public RelayCommand SyncCommand { get; }
    public RelayCommand AddAbsenceCommand { get; }
    public RelayCommand StatusCommand { get; }
    public RelayCommand AddEmployeeCommand { get; }
    public RelayCommand RemoveEmployeeCommand { get; }
    public RelayCommand SortEmployeesCommand { get; }

    // ✅ Klick in Kalenderzelle (kommt aus Controls.ScheduleMonthControl)
    public RelayCommand<CalendarCellRef> CalendarCellClickCommand { get; }

    private enum Mode
    {
        None,
        Distribute,
        Delete,
        EditMove_SelectSource,
        EditMove_SelectTarget,
        EditDuration_SelectSource,
        Series_SelectSource
    }

    private Mode _mode = Mode.None;
    private Appointment? _pending;
    private int _pendingSlots;

    private const int SlotMinutes = 30;

    public MainViewModel(ICalendarRepository repo, ICalendarService service)
    {
        _repo = repo;
        _service = service;

        PrevMonthCommand = new RelayCommand(PrevMonth);
        NextMonthCommand = new RelayCommand(NextMonth);
        TodayCommand = new RelayCommand(Today);

        // Excel-Flow: Button setzt Modus -> Klick im Grid führt Aktion aus
        AddAppointmentCommand = new RelayCommand(StartDistribute);
        CreateSeriesCommand = new RelayCommand(StartSeries);
        EditCommand = new RelayCommand(StartEdit);
        DeleteCommand = new RelayCommand(StartDelete);

        SyncCommand = new RelayCommand(() => MessageBox.Show("SYNC: kommt als nächster Schritt (Datenabgleich)."));
        AddAbsenceCommand = new RelayCommand(() => MessageBox.Show("Abwesenheit: kommt als nächster Schritt (Excel-Logik 1:1)."));
        StatusCommand = new RelayCommand(() => MessageBox.Show("Status ändern: kommt als nächster Schritt (Excel-Logik 1:1)."));

        AddEmployeeCommand = new RelayCommand(() => _ = AddEmployeeAsync());
        RemoveEmployeeCommand = new RelayCommand(() => _ = RemoveLastEmployeeAsync());
        SortEmployeesCommand = new RelayCommand(() => _ = SortEmployeesAsync());

        CalendarCellClickCommand = new RelayCommand<CalendarCellRef>(c => _ = OnCellClickAsync(c));

        _ = InitializeAsync();
    }

    // ===== Button-Start (VBA-like) =====

    private void StartDistribute()
    {
        if (string.IsNullOrWhiteSpace(CustomerFilter))
        {
            MessageBox.Show("Bitte zuerst Kundenname oben eintragen.");
            return;
        }

        _mode = Mode.Distribute;
        _pending = null;
        MessageBox.Show("Termin verteilen: Bitte in die gewünschte Zelle (Tag + Uhrzeit) klicken.\nDanach wählst du nur noch die Dauer.");
    }

    private void StartDelete()
    {
        _mode = Mode.Delete;
        _pending = null;
        MessageBox.Show("Termin löschen: Bitte auf einen Termin im Kalender klicken.");
    }

    private void StartSeries()
    {
        _mode = Mode.Series_SelectSource;
        _pending = null;
        MessageBox.Show("Serie erstellen: Bitte zuerst auf den QUELL-Termin klicken.\nDanach wählst du Intervall & Dauer.");
    }

    private void StartEdit()
    {
        var choice = AskEditMode(); // 1=Zeit, 2=Dauer, 0=Cancel
        if (choice == 0) return;

        _pending = null;
        _pendingSlots = 0;

        if (choice == 1)
        {
            _mode = Mode.EditMove_SelectSource;
            MessageBox.Show("Termin ändern (Zeit): Bitte zuerst auf den Termin klicken.\nDanach auf die Ziel-Zelle klicken.");
        }
        else
        {
            _mode = Mode.EditDuration_SelectSource;
            MessageBox.Show("Termin ändern (Dauer): Bitte auf den Termin klicken, dann Dauer auswählen.");
        }
    }

    // ===== Kalender-Klick =====

    private async Task OnCellClickAsync(CalendarCellRef? cell)
    {
        if (cell is null) return;
        if (MonthView is null) return;

        try
        {
            switch (_mode)
            {
                case Mode.Distribute:
                    await HandleDistributeAsync(cell);
                    break;

                case Mode.Delete:
                    await HandleDeleteAsync(cell);
                    break;

                case Mode.EditMove_SelectSource:
                    await HandleEditMoveSelectSourceAsync(cell);
                    break;

                case Mode.EditMove_SelectTarget:
                    await HandleEditMoveSelectTargetAsync(cell);
                    break;

                case Mode.EditDuration_SelectSource:
                    await HandleEditDurationAsync(cell);
                    break;

                case Mode.Series_SelectSource:
                    await HandleSeriesAsync(cell);
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler:\n" + ex.Message);
        }
    }

    // ===== Implementierung der 4 Kern-Aktionen =====

    private async Task HandleDistributeAsync(CalendarCellRef cell)
    {
        var existing = FindAppointmentAt(cell);
        if (existing is not null)
        {
            MessageBox.Show("Hier ist bereits ein Termin. (Termin ändern oder löschen verwenden.)");
            return;
        }

        var slots = AskDurationSlots();
        if (slots <= 0) return;

        var start = cell.SlotStart;
        var end = start.AddMinutes(slots * SlotMinutes);

        var appt = new Appointment(
            Id: Guid.NewGuid().ToString("N"),
            EmployeeId: cell.EmployeeId,
            Date: cell.Date,
            Start: start,
            End: end,
            CustomerName: CustomerFilter.Trim(),
            Status: AppointmentStatus.Fixed);

        if (HasOverlap(appt, ignoreAppointmentId: null))
        {
            MessageBox.Show("Konflikt: Der Termin würde einen anderen Termin überlappen.");
            return;
        }

        await _service.UpsertAppointmentAsync(appt);
        await RefreshAsync();
    }

    private async Task HandleDeleteAsync(CalendarCellRef cell)
    {
        var existing = FindAppointmentAt(cell);
        if (existing is null)
        {
            MessageBox.Show("Kein Termin in dieser Zelle.");
            return;
        }

        var ok = MessageBox.Show(
            $"Termin löschen?\n\n{existing.CustomerName} ({existing.Start:HH\\:mm}–{existing.End:HH\\:mm})",
            "Löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (ok != MessageBoxResult.Yes) return;

        await _service.DeleteAppointmentAsync(existing.Id);
        await RefreshAsync();
    }

    private async Task HandleEditMoveSelectSourceAsync(CalendarCellRef cell)
    {
        var existing = FindAppointmentAt(cell);
        if (existing is null)
        {
            MessageBox.Show("Bitte zuerst auf einen bestehenden Termin klicken.");
            return;
        }

        _pending = existing;
        _pendingSlots = GetSlots(existing);
        _mode = Mode.EditMove_SelectTarget;

        MessageBox.Show("Jetzt Ziel-Zelle klicken (Tag + Uhrzeit).", "Zeit ändern");
    }

    private async Task HandleEditMoveSelectTargetAsync(CalendarCellRef target)
    {
        if (_pending is null)
        {
            _mode = Mode.EditMove_SelectSource;
            return;
        }

        var moved = _pending with
        {
            Date = target.Date,
            Start = target.SlotStart,
            End = target.SlotStart.AddMinutes(_pendingSlots * SlotMinutes),
            EmployeeId = target.EmployeeId
        };

        if (HasOverlap(moved, ignoreAppointmentId: moved.Id))
        {
            MessageBox.Show("Konflikt: Der verschobene Termin würde einen anderen Termin überlappen.");
            return;
        }

        await _service.UpsertAppointmentAsync(moved);
        _pending = null;
        _mode = Mode.None;
        await RefreshAsync();
    }

    private async Task HandleEditDurationAsync(CalendarCellRef cell)
    {
        var existing = FindAppointmentAt(cell);
        if (existing is null)
        {
            MessageBox.Show("Bitte auf einen bestehenden Termin klicken.");
            return;
        }

        var slots = AskDurationSlots(defaultSlots: GetSlots(existing));
        if (slots <= 0) return;

        var updated = existing with
        {
            End = existing.Start.AddMinutes(slots * SlotMinutes)
        };

        if (HasOverlap(updated, ignoreAppointmentId: updated.Id))
        {
            MessageBox.Show("Konflikt: Die neue Dauer würde einen anderen Termin überlappen.");
            return;
        }

        await _service.UpsertAppointmentAsync(updated);
        _mode = Mode.None;
        await RefreshAsync();
    }

    private async Task HandleSeriesAsync(CalendarCellRef cell)
    {
        var source = FindAppointmentAt(cell);
        if (source is null)
        {
            MessageBox.Show("Serie erstellen: Bitte auf einen bestehenden Termin klicken (Quelltermin).");
            return;
        }

        var intervalWeeks = AskSeriesIntervalWeeks();
        if (intervalWeeks <= 0) return;

        var slots = AskDurationSlots(defaultSlots: GetSlots(source));
        if (slots <= 0) return;

        var start = source.Start;
        var end = start.AddMinutes(slots * SlotMinutes);
        var weekday = cell.Date.DayOfWeek;

        var anchor = cell.Date;
        var daysInMonth = DateTime.DaysInMonth(Year, Month);
        var last = new DateOnly(Year, Month, daysInMonth);

        int created = 0;

        for (var d = anchor; d <= last; d = d.AddDays(1))
        {
            if (d.DayOfWeek != weekday) continue;

            var deltaDays = (d.ToDateTime(TimeOnly.MinValue) - anchor.ToDateTime(TimeOnly.MinValue)).Days;
            var deltaWeeks = deltaDays / 7;

            if (deltaWeeks % intervalWeeks != 0) continue;

            var appt = new Appointment(
                Id: Guid.NewGuid().ToString("N"),
                EmployeeId: source.EmployeeId,
                Date: d,
                Start: start,
                End: end,
                CustomerName: source.CustomerName,
                Status: source.Status);

            if (HasOverlap(appt, ignoreAppointmentId: null))
                continue;

            await _service.UpsertAppointmentAsync(appt);
            created++;
        }

        _mode = Mode.None;
        await RefreshAsync();
        MessageBox.Show($"Serie erstellt: {created} Termine im Monat.");
    }

    // ===== Helper =====

    private Appointment? FindAppointmentAt(CalendarCellRef cell)
    {
        var mv = MonthView;
        if (mv is null) return null;

        var day = mv.Cells.FirstOrDefault(c => c.Date == cell.Date);
        if (day is null) return null;

        return day.Appointments
            .Where(a => a.EmployeeId == cell.EmployeeId)
            .FirstOrDefault(a => a.Start <= cell.SlotStart && a.End > cell.SlotStart);
    }

    private bool HasOverlap(Appointment candidate, string? ignoreAppointmentId)
    {
        var mv = MonthView;
        if (mv is null) return false;

        var day = mv.Cells.FirstOrDefault(c => c.Date == candidate.Date);
        if (day is null) return false;

        foreach (var a in day.Appointments.Where(a => a.EmployeeId == candidate.EmployeeId))
        {
            if (!string.IsNullOrWhiteSpace(ignoreAppointmentId) && a.Id == ignoreAppointmentId)
                continue;
            if (a.End <= candidate.Start || a.Start >= candidate.End) continue;
            return true;
        }

        return false;
    }

    private static int GetSlots(Appointment a)
    {
        var mins = (int)(a.End.ToTimeSpan() - a.Start.ToTimeSpan()).TotalMinutes;
        if (mins <= 0) return 1;
        return Math.Max(1, (int)Math.Round(mins / (double)SlotMinutes));
    }

    // ===== Dialoge (Buttons statt InputBox) =====

    private static int AskDurationSlots(int defaultSlots = 2)
    {
        int result = 0;

        var win = new Window
        {
            Title = "Dauer auswählen",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "Wie lange?",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var grid = new UniformGrid { Columns = 2, Rows = 2 };
        root.Children.Add(grid);

        void Add(string label, int slots)
        {
            var btn = new Button
            {
                Content = label,
                Margin = new Thickness(6),
                MinWidth = 140,
                MinHeight = 44,
                FontWeight = slots == defaultSlots ? FontWeights.SemiBold : FontWeights.Normal
            };
            btn.Click += (_, __) => { result = slots; win.Close(); };
            grid.Children.Add(btn);
        }

        Add("0,5 h", 1);
        Add("1,0 h", 2);
        Add("1,5 h", 3);
        Add("2,0 h", 4);

        var cancel = new Button { Content = "Abbrechen", Margin = new Thickness(6), MinHeight = 38 };
        cancel.Click += (_, __) => { result = 0; win.Close(); };
        root.Children.Add(cancel);

        win.Content = root;
        win.Owner = Application.Current?.MainWindow;
        win.ShowDialog();

        return result;
    }

    private static int AskEditMode()
    {
        int result = 0;

        var win = new Window
        {
            Title = "Termin ändern",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "Was möchtest du ändern?",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        var b1 = new Button { Content = "Zeit", Margin = new Thickness(6), MinWidth = 120, MinHeight = 44 };
        b1.Click += (_, __) => { result = 1; win.Close(); };

        var b2 = new Button { Content = "Dauer", Margin = new Thickness(6), MinWidth = 120, MinHeight = 44 };
        b2.Click += (_, __) => { result = 2; win.Close(); };

        row.Children.Add(b1);
        row.Children.Add(b2);
        root.Children.Add(row);

        var cancel = new Button { Content = "Abbrechen", Margin = new Thickness(6), MinHeight = 38 };
        cancel.Click += (_, __) => { result = 0; win.Close(); };
        root.Children.Add(cancel);

        win.Content = root;
        win.Owner = Application.Current?.MainWindow;
        win.ShowDialog();

        return result;
    }

    private static int AskSeriesIntervalWeeks()
    {
        int result = 0;

        var win = new Window
        {
            Title = "Serie – Intervall",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "Alle wieviel Wochen?",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var grid = new UniformGrid { Columns = 4, Rows = 1 };
        root.Children.Add(grid);

        void Add(int w)
        {
            var btn = new Button { Content = w.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(6), MinWidth = 60, MinHeight = 44 };
            btn.Click += (_, __) => { result = w; win.Close(); };
            grid.Children.Add(btn);
        }

        Add(1); Add(2); Add(3); Add(4);

        var cancel = new Button { Content = "Abbrechen", Margin = new Thickness(6), MinHeight = 38 };
        cancel.Click += (_, __) => { result = 0; win.Close(); };
        root.Children.Add(cancel);

        win.Content = root;
        win.Owner = Application.Current?.MainWindow;
        win.ShowDialog();

        return result;
    }

    // ===== Month / Employees =====

    private async Task InitializeAsync()
    {
        try
        {
            await _repo.InitializeAsync();

            var emps = (await _repo.GetEmployeesAsync()).ToList();
            if (emps.Count == 0)
            {
                emps.Add(new Employee(Guid.NewGuid().ToString("N"), "Marcel", true));
                await _repo.SaveEmployeesAsync(emps);
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Init Fehler:\n" + ex.Message);
        }
    }

    private async Task RefreshAsync()
    {
        MonthView = await _service.BuildMonthViewAsync(Year, Month, employeeIdFilter: null);

        Employees.Clear();
        foreach (var e in MonthView.Employees)
            Employees.Add(e);
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

        emps.RemoveAt(emps.Count - 1);
        await _repo.SaveEmployeesAsync(emps);
        await RefreshAsync();
    }

    private async Task SortEmployeesAsync()
    {
        var emps = (await _repo.GetEmployeesAsync()).ToList();
        emps = emps.OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        await _repo.SaveEmployeesAsync(emps);
        await RefreshAsync();
    }

    private static int ClampMonth(int m) => m < 1 ? 1 : (m > 12 ? 12 : m);
    private static int ClampYear(int y) => y < 2000 ? 2000 : (y > 2100 ? 2100 : y);
}
