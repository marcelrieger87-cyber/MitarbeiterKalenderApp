using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
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

    // ✅ Aktuell markierte Auswahl (Excel-like)
    private CalendarCellRef? _selectedCell;
    private Appointment? _selectedAppointment;

    public RelayCommand PrevMonthCommand { get; }
    public RelayCommand NextMonthCommand { get; }
    public RelayCommand TodayCommand { get; }

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

    public RelayCommand<CalendarCellRef> CalendarCellClickCommand { get; }

    private enum Mode
    {
        None,
        Distribute,              // Termin verteilen: Klick auf leere Zelle => Dauer => erstellen
        EditMove_WaitTarget      // Termin ändern Zeit: nach Button wartet auf Zielzelle
    }

    private Mode _mode = Mode.None;
    private Appointment? _pendingMove;
    private int _pendingMoveSlots;

    private const int SlotMinutes = 30;

    public MainViewModel(ICalendarRepository repo, ICalendarService service)
    {
        _repo = repo;
        _service = service;

        PrevMonthCommand = new RelayCommand(PrevMonth);
        NextMonthCommand = new RelayCommand(NextMonth);
        TodayCommand = new RelayCommand(Today);

        // ✅ Buttons arbeiten auf der markierten Auswahl (wie Excel)
        AddAppointmentCommand = new RelayCommand(StartDistribute);
        CreateSeriesCommand = new RelayCommand(CreateSeriesFromSelection);
        EditCommand = new RelayCommand(EditSelection);
        DeleteCommand = new RelayCommand(DeleteSelection);

        SyncCommand = new RelayCommand(() => MessageBox.Show("SYNC: kommt als nächster Schritt (Datenabgleich)."));
        AddAbsenceCommand = new RelayCommand(() => MessageBox.Show("Abwesenheit: kommt als nächster Schritt (Excel-Logik 1:1)."));
        StatusCommand = new RelayCommand(() => MessageBox.Show("Status ändern: kommt als nächster Schritt (Excel-Logik 1:1)."));

        AddEmployeeCommand = new RelayCommand(() => _ = AddEmployeeAsync());
        RemoveEmployeeCommand = new RelayCommand(() => _ = RemoveLastEmployeeAsync());
        SortEmployeesCommand = new RelayCommand(() => _ = SortEmployeesAsync());

        CalendarCellClickCommand = new RelayCommand<CalendarCellRef>(c => _ = OnCellClickAsync(c));

        _ = InitializeAsync();
    }

    // ===== Klick im Kalender =====

    private async Task OnCellClickAsync(CalendarCellRef? cell)
    {
        if (cell is null || MonthView is null) return;

        _selectedCell = cell;
        _selectedAppointment = FindAppointmentAt(cell);

        try
        {
            // 1) Wenn wir auf Zielzelle warten (Zeit ändern)
            if (_mode == Mode.EditMove_WaitTarget)
            {
                await ApplyMoveToTargetAsync(cell);
                return;
            }

            // 2) Termin verteilen Modus: Klick auf LEERE Zelle erzeugt Termin
            if (_mode == Mode.Distribute)
            {
                await CreateAppointmentAtCellAsync(cell);
                return;
            }

            // 3) Normalmodus: nur markieren (kein Popup-Spam)
            //    (Damit du erst markierst, dann Button drückst – wie Excel)
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler:\n" + ex.Message);
        }
    }

    // ===== Termin verteilen =====

    private void StartDistribute()
    {
        if (string.IsNullOrWhiteSpace(CustomerFilter))
        {
            MessageBox.Show("Bitte zuerst Kundenname oben eintragen.");
            return;
        }

        _mode = Mode.Distribute;
        _pendingMove = null;
        // keine MessageBox-Orgie – der Nutzer weiß, was er tut
    }

    private async Task CreateAppointmentAtCellAsync(CalendarCellRef cell)
    {
        // wenn bereits Termin da: nichts erstellen, nur markieren
        if (FindAppointmentAt(cell) is not null)
            return;

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
        _mode = Mode.None;
        await RefreshAsync();
    }

    // ===== Termin löschen =====

    private async void DeleteSelection()
    {
        try
        {
            if (_selectedAppointment is null)
            {
                MessageBox.Show("Bitte zuerst einen Termin im Kalender anklicken (markieren).");
                return;
            }

            var appt = _selectedAppointment;

            // ✅ Serien-Erkennung ohne extra DB-Feld:
            // Serie = gleicher Mitarbeiter + gleicher Kunde + gleiche Startzeit + gleiche Dauer mehrfach im Monat vorhanden
            var series = FindSeriesCandidates(appt);
            if (series.Count > 1)
            {
                var choice = AskDeleteSingleOrSeries(series.Count);
                if (choice == 0) return;

                if (choice == 1)
                {
                    await _service.DeleteAppointmentAsync(appt.Id);
                }
                else
                {
                    foreach (var a in series)
                        await _service.DeleteAppointmentAsync(a.Id);
                }
            }
            else
            {
                var ok = MessageBox.Show(
                    $"Termin löschen?\n\n{appt.CustomerName} ({appt.Start:HH\\:mm}–{appt.End:HH\\:mm})",
                    "Löschen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (ok != MessageBoxResult.Yes) return;

                await _service.DeleteAppointmentAsync(appt.Id);
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler:\n" + ex.Message);
        }
    }

    // ===== Termin ändern =====

    private async void EditSelection()
    {
        try
        {
            if (_selectedAppointment is null || _selectedCell is null)
            {
                MessageBox.Show("Bitte zuerst einen Termin im Kalender anklicken (markieren).");
                return;
            }

            var choice = AskEditMode(); // 1=Zeit, 2=Dauer, 0=Cancel
            if (choice == 0) return;

            if (choice == 2)
            {
                // Dauer ändern: sofort Dauer wählen, kein extra Klick nötig
                var slots = AskDurationSlots(defaultSlots: GetSlots(_selectedAppointment));
                if (slots <= 0) return;

                var updated = _selectedAppointment with
                {
                    End = _selectedAppointment.Start.AddMinutes(slots * SlotMinutes)
                };

                if (HasOverlap(updated, ignoreAppointmentId: updated.Id))
                {
                    MessageBox.Show("Konflikt: Die neue Dauer würde einen anderen Termin überlappen.");
                    return;
                }

                await _service.UpsertAppointmentAsync(updated);
                await RefreshAsync();
                return;
            }

            // Zeit ändern: wir warten auf Zielzelle (1 Klick)
            _pendingMove = _selectedAppointment;
            _pendingMoveSlots = GetSlots(_selectedAppointment);
            _mode = Mode.EditMove_WaitTarget;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler:\n" + ex.Message);
        }
    }

    private async Task ApplyMoveToTargetAsync(CalendarCellRef target)
    {
        if (_pendingMove is null) { _mode = Mode.None; return; }

        var moved = _pendingMove with
        {
            Date = target.Date,
            Start = target.SlotStart,
            End = target.SlotStart.AddMinutes(_pendingMoveSlots * SlotMinutes),
            EmployeeId = target.EmployeeId
        };

        if (HasOverlap(moved, ignoreAppointmentId: moved.Id))
        {
            MessageBox.Show("Konflikt: Der verschobene Termin würde einen anderen Termin überlappen.");
            return;
        }

        await _service.UpsertAppointmentAsync(moved);
        _pendingMove = null;
        _mode = Mode.None;
        await RefreshAsync();
    }

    // ===== Serie erstellen =====

    private async void CreateSeriesFromSelection()
    {
        try
        {
            if (_selectedAppointment is null || _selectedCell is null)
            {
                MessageBox.Show("Bitte zuerst einen Termin im Kalender anklicken (markieren).");
                return;
            }

            var intervalWeeks = AskSeriesIntervalWeeks();
            if (intervalWeeks <= 0) return;

            var slots = AskDurationSlots(defaultSlots: GetSlots(_selectedAppointment));
            if (slots <= 0) return;

            var source = _selectedAppointment;
            var start = source.Start;
            var end = start.AddMinutes(slots * SlotMinutes);

            var weekday = _selectedCell.Date.DayOfWeek;
            var anchor = _selectedCell.Date;

            var last = new DateOnly(Year, Month, DateTime.DaysInMonth(Year, Month));

            int created = 0;

            // Excel/VBA-like: gleiche Wochentage, Intervall in Wochen
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

            await RefreshAsync();
            MessageBox.Show($"Serie erstellt: {created} Termine im Monat.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler:\n" + ex.Message);
        }
    }

    // ===== Helpers =====

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

    private List<Appointment> FindSeriesCandidates(Appointment seed)
    {
        var mv = MonthView;
        if (mv is null) return new List<Appointment>();

        var durationMinutes = (int)(seed.End.ToTimeSpan() - seed.Start.ToTimeSpan()).TotalMinutes;

        var all = mv.Cells
            .SelectMany(c => c.Appointments)
            .Where(a =>
                a.EmployeeId == seed.EmployeeId &&
                string.Equals(a.CustomerName, seed.CustomerName, StringComparison.OrdinalIgnoreCase) &&
                a.Start == seed.Start &&
                (int)(a.End.ToTimeSpan() - a.Start.ToTimeSpan()).TotalMinutes == durationMinutes)
            .OrderBy(a => a.Date)
            .ToList();

        return all;
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

    private static int AskDeleteSingleOrSeries(int count)
    {
        int result = 0;

        var win = new Window
        {
            Title = "Löschen",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = $"Es scheint eine Serie zu sein ({count} Termine im Monat).\nWas löschen?",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        var b1 = new Button { Content = "Nur diesen", Margin = new Thickness(6), MinWidth = 140, MinHeight = 44 };
        b1.Click += (_, __) => { result = 1; win.Close(); };

        var b2 = new Button { Content = "Ganze Serie", Margin = new Thickness(6), MinWidth = 140, MinHeight = 44 };
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
