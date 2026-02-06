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
    public string NewEmployeeName { get => _newEmployeeName; set => SetProperty(ref _newEmployeeName, value); }

    private string _customerFilter = "";
    public string CustomerFilter { get => _customerFilter; set => SetProperty(ref _customerFilter, value); }

    private int _year = DateTime.Today.Year;
    public int Year
    {
        get => _year;
        set { value = ClampYear(value); if (SetProperty(ref _year, value)) { Raise(nameof(MonthTitle)); _ = RefreshAsync(); } }
    }

    private int _month = DateTime.Today.Month;
    public int Month
    {
        get => _month;
        set { value = ClampMonth(value); if (SetProperty(ref _month, value)) { Raise(nameof(MonthTitle)); _ = RefreshAsync(); } }
    }

    public string MonthTitle => new DateTime(Year, Month, 1).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("de-DE"));

    private MonthView? _monthView;
    public MonthView? MonthView { get => _monthView; private set => SetProperty(ref _monthView, value); }

    // ✅ Modus-Highlight
    private bool _isDistributeModeActive;
    public bool IsDistributeModeActive
    {
        get => _isDistributeModeActive;
        private set => SetProperty(ref _isDistributeModeActive, value);
    }

    // ✅ Auswahl (markierter Termin)
    private CalendarCellRef? _selectedCell;
    private Appointment? _selectedAppointment;

    // ✅ Mitarbeiter-Filter (Multi)
    private HashSet<string>? _employeeFilterIds; // null = alle

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
    public RelayCommand FilterEmployeesCommand { get; }

    public RelayCommand<CalendarCellRef> CalendarCellClickCommand { get; }

    private enum Mode { None, Distribute, Move_WaitTarget }
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

        AddAppointmentCommand = new RelayCommand(StartDistribute);
        CreateSeriesCommand = new RelayCommand(CreateSeriesFromSelection);
        EditCommand = new RelayCommand(EditSelection);
        DeleteCommand = new RelayCommand(DeleteSelection);

        SyncCommand = new RelayCommand(() => MessageBox.Show("SYNC: kommt als nächster Schritt."));
        AddAbsenceCommand = new RelayCommand(() => MessageBox.Show("Abwesenheit: kommt als nächster Schritt."));
        StatusCommand = new RelayCommand(() => MessageBox.Show("Status ändern: kommt als nächster Schritt."));

        AddEmployeeCommand = new RelayCommand(() => _ = AddEmployeeAsync());
        RemoveEmployeeCommand = new RelayCommand(() => _ = RemoveLastEmployeeAsync());
        SortEmployeesCommand = new RelayCommand(() => _ = SortEmployeesAsync());
        FilterEmployeesCommand = new RelayCommand(() => _ = FilterEmployeesAsync());

        CalendarCellClickCommand = new RelayCommand<CalendarCellRef>(c => _ = OnCellClickAsync(c));

        _ = InitializeAsync();
    }

    // ===== Klick =====

    private async Task OnCellClickAsync(CalendarCellRef? cell)
    {
        if (cell is null || MonthView is null) return;

        _selectedCell = cell;
        _selectedAppointment = FindAppointmentAt(cell);

        try
        {
            if (_mode == Mode.Move_WaitTarget)
            {
                await ApplyMoveToTargetAsync(cell);
                return;
            }

            if (_mode == Mode.Distribute)
            {
                // Nur leere Zelle erstellt Termin
                if (_selectedAppointment is null)
                    await CreateAppointmentAtCellAsync(cell);

                return;
            }

            // Normal: nur markieren (kein PopUp-Spam)
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler:\n" + ex.Message);
        }
    }

    // ===== Termin verteilen (Modus + Highlight) =====

    private void StartDistribute()
    {
        if (string.IsNullOrWhiteSpace(CustomerFilter))
        {
            MessageBox.Show("Bitte zuerst Kundenname oben eintragen.");
            return;
        }

        _mode = Mode.Distribute;
        IsDistributeModeActive = true;
    }

    private async Task CreateAppointmentAtCellAsync(CalendarCellRef cell)
    {
        var slots = AskDurationSlots();
        if (slots <= 0) return;

        var appt = new Appointment(
            Id: Guid.NewGuid().ToString("N"),
            EmployeeId: cell.EmployeeId,
            Date: cell.Date,
            Start: cell.SlotStart,
            End: cell.SlotStart.AddMinutes(slots * SlotMinutes),
            CustomerName: CustomerFilter.Trim(),
            Status: AppointmentStatus.Fixed,
            IsFromRecurrence: false,
            RecurrenceRuleId: null);

        if (HasOverlap(appt, ignoreAppointmentId: null))
        {
            MessageBox.Show("Konflikt: Überlappt mit einem anderen Termin.");
            return;
        }

        await _service.UpsertAppointmentAsync(appt);
        _mode = Mode.None;
        IsDistributeModeActive = false;
        await RefreshAsync();
    }

    // ===== Serie erstellen (endlos via RecurrenceRule) =====
    // ✅ nur: Jede Woche / Jede zweite Woche
    // ✅ keine Dauer-Abfrage (kommt vom markierten Termin)
    private async void CreateSeriesFromSelection()
    {
        try
        {
            if (_selectedAppointment is null || _selectedCell is null)
            {
                MessageBox.Show("Bitte zuerst einen Termin anklicken (markieren).");
                return;
            }

            var interval = AskSeriesWeeklyOrBiWeekly();
            if (interval == 0) return;

            var source = _selectedAppointment;

            // Rule anlegen (endlos; monatliche Anzeige wird expandiert)
            var ruleId = source.RecurrenceRuleId ?? Guid.NewGuid().ToString("N");
            var rule = new RecurrenceRule(
                Id: ruleId,
                EmployeeId: source.EmployeeId,
                Weekday: _selectedCell.Date.DayOfWeek,
                Start: source.Start,
                End: source.End,
                CustomerName: source.CustomerName,
                IsActive: true,
                IntervalWeeks: interval,
                AnchorDate: _selectedCell.Date
            );

            await _repo.UpsertRecurrenceRuleAsync(rule);

            // Wenn es ein „normaler“ Einzeltermin war: rausnehmen, weil ab jetzt Serie regelt
            if (!source.IsFromRecurrence)
                await _service.DeleteAppointmentAsync(source.Id);

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler:\n" + ex.Message);
        }
    }

    // ===== Termin löschen (Einzel / Serie) =====

    private async void DeleteSelection()
    {
        try
        {
            if (_selectedAppointment is null)
            {
                MessageBox.Show("Bitte zuerst einen Termin anklicken (markieren).");
                return;
            }

            var appt = _selectedAppointment;

            // Serie? -> fragen: nur diesen / ganze Serie
            if (!string.IsNullOrWhiteSpace(appt.RecurrenceRuleId))
            {
                var choice = AskSingleOrSeries("Löschen");
                if (choice == 0) return;

                if (choice == 2)
                {
                    await _repo.DeleteRecurrenceRuleAsync(appt.RecurrenceRuleId!);
                    await RefreshAsync();
                    return;
                }

                // Nur diesen: Ausnahme anlegen
                await CreateCancelExceptionAsync(appt);
                await RefreshAsync();
                return;
            }

            // Einzel
            var ok = MessageBox.Show(
                $"Termin löschen?\n\n{appt.CustomerName} ({appt.Start:HH\\:mm}–{appt.End:HH\\:mm})",
                "Löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (ok != MessageBoxResult.Yes) return;

            await _service.DeleteAppointmentAsync(appt.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler:\n" + ex.Message);
        }
    }

    // ===== Termin ändern (Zeit/Dauer; bei Serien: Einzel oder Serie) =====

    private async void EditSelection()
    {
        try
        {
            if (_selectedAppointment is null || _selectedCell is null)
            {
                MessageBox.Show("Bitte zuerst einen Termin anklicken (markieren).");
                return;
            }

            var appt = _selectedAppointment;

            // Wenn Serie -> vorab fragen: Einzel oder Serie
            int applyTo = 1; // 1=Einzel, 2=Serie
            if (!string.IsNullOrWhiteSpace(appt.RecurrenceRuleId))
            {
                applyTo = AskSingleOrSeries("Ändern");
                if (applyTo == 0) return;
            }

            var edit = AskEditMode(); // 1=Zeit, 2=Dauer, 0=Cancel
            if (edit == 0) return;

            if (edit == 2)
            {
                var slots = AskDurationSlots(defaultSlots: GetSlots(appt));
                if (slots <= 0) return;

                var newEnd = appt.Start.AddMinutes(slots * SlotMinutes);

                if (applyTo == 2 && !string.IsNullOrWhiteSpace(appt.RecurrenceRuleId))
                {
                    var rules = await _repo.GetRecurrenceRulesAsync();
                    var rule = rules.FirstOrDefault(r => r.Id == appt.RecurrenceRuleId);
                    if (rule is null) return;

                    await _repo.UpsertRecurrenceRuleAsync(rule with { End = newEnd });
                    await RefreshAsync();
                    return;
                }

                // Einzel
                if (!string.IsNullOrWhiteSpace(appt.RecurrenceRuleId))
                {
                    // Einzel innerhalb Serie: cancel original + exception mit neuer Dauer (gleiches Datum/Start, anderes End)
                    await CreateCancelExceptionAsync(appt);
                    await CreateMoveOrOverrideExceptionAsync(appt, appt.Date, appt.Start, newEnd);
                    await RefreshAsync();
                    return;
                }

                var updated = appt with { End = newEnd };
                if (HasOverlap(updated, ignoreAppointmentId: updated.Id))
                {
                    MessageBox.Show("Konflikt: Überlappt mit einem anderen Termin.");
                    return;
                }

                await _service.UpsertAppointmentAsync(updated);
                await RefreshAsync();
                return;
            }

            // Zeit ändern:
            if (applyTo == 2 && !string.IsNullOrWhiteSpace(appt.RecurrenceRuleId))
            {
                // Serie verschieben: wir warten auf Zielzelle, aber ändern danach RULE
                _pendingMove = appt;
                _pendingMoveSlots = GetSlots(appt);
                _mode = Mode.Move_WaitTarget;
                IsDistributeModeActive = false;
                return;
            }

            // Einzel verschieben
            _pendingMove = appt;
            _pendingMoveSlots = GetSlots(appt);
            _mode = Mode.Move_WaitTarget;
            IsDistributeModeActive = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler:\n" + ex.Message);
        }
    }

    private async Task ApplyMoveToTargetAsync(CalendarCellRef target)
    {
        if (_pendingMove is null) { _mode = Mode.None; return; }

        var appt = _pendingMove;
        var newStart = target.SlotStart;
        var newEnd = newStart.AddMinutes(_pendingMoveSlots * SlotMinutes);

        // Serie?
        if (!string.IsNullOrWhiteSpace(appt.RecurrenceRuleId))
        {
            var choice = AskSingleOrSeries("Verschieben");
            if (choice == 0) { _mode = Mode.None; _pendingMove = null; return; }

            if (choice == 2)
            {
                // Ganze Serie: Rule anpassen (Wochentag/Start/End/Mitarbeiter)
                var rules = await _repo.GetRecurrenceRulesAsync();
                var rule = rules.FirstOrDefault(r => r.Id == appt.RecurrenceRuleId);
                if (rule is null) return;

                await _repo.UpsertRecurrenceRuleAsync(rule with
                {
                    EmployeeId = target.EmployeeId,
                    Weekday = target.Date.DayOfWeek,
                    Start = newStart,
                    End = newEnd
                });

                _mode = Mode.None;
                _pendingMove = null;
                await RefreshAsync();
                return;
            }

            // Nur diesen: cancel original + moved exception (Serie-Zusammenhang bleibt über RuleId)
            await CreateCancelExceptionAsync(appt);
            await CreateMoveOrOverrideExceptionAsync(appt, target.Date, newStart, newEnd);

            _mode = Mode.None;
            _pendingMove = null;
            await RefreshAsync();
            return;
        }

        // Einzeltermin
        var moved = appt with
        {
            Date = target.Date,
            Start = newStart,
            End = newEnd,
            EmployeeId = target.EmployeeId
        };

        if (HasOverlap(moved, ignoreAppointmentId: moved.Id))
        {
            MessageBox.Show("Konflikt: Überlappt mit einem anderen Termin.");
            return;
        }

        await _service.UpsertAppointmentAsync(moved);
        _mode = Mode.None;
        _pendingMove = null;
        await RefreshAsync();
    }

    // ===== Exceptions (Serie-Zusammenhang bleibt) =====

    private async Task CreateCancelExceptionAsync(Appointment appt)
    {
        // Cancel = Exception auf originalem Slot -> ExpandWeeklyRule skippt damit
        var ex = new RecurrenceException(
            Id: Guid.NewGuid().ToString("N"),
            RecurrenceRuleId: appt.RecurrenceRuleId!,
            EmployeeId: appt.EmployeeId,
            Date: appt.Date,
            Start: appt.Start,
            End: appt.End,
            CustomerName: appt.CustomerName
        );

        await _repo.UpsertRecurrenceExceptionAsync(ex);
    }

    private async Task CreateMoveOrOverrideExceptionAsync(Appointment appt, DateOnly newDate, TimeOnly newStart, TimeOnly newEnd)
    {
        // Moved/Override = zusätzlicher Exception-Termin (wird als Termin angezeigt)
        var ex = new RecurrenceException(
            Id: Guid.NewGuid().ToString("N"),
            RecurrenceRuleId: appt.RecurrenceRuleId!,
            EmployeeId: appt.EmployeeId,
            Date: newDate,
            Start: newStart,
            End: newEnd,
            CustomerName: appt.CustomerName
        );

        await _repo.UpsertRecurrenceExceptionAsync(ex);
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

    private static int GetSlots(Appointment a)
    {
        var mins = (int)(a.End.ToTimeSpan() - a.Start.ToTimeSpan()).TotalMinutes;
        if (mins <= 0) return 1;
        return Math.Max(1, (int)Math.Round(mins / (double)SlotMinutes));
    }

    // ===== Dialoge =====

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
        root.Children.Add(new TextBlock { Text = "Wie lange?", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 12) });

        var grid = new UniformGrid { Columns = 2, Rows = 2 };
        root.Children.Add(grid);

        void Add(string label, int slots)
        {
            var btn = new Button { Content = label, Margin = new Thickness(6), MinWidth = 140, MinHeight = 44, FontWeight = slots == defaultSlots ? FontWeights.SemiBold : FontWeights.Normal };
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
        root.Children.Add(new TextBlock { Text = "Was möchtest du ändern?", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 12) });

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

    private static int AskSeriesWeeklyOrBiWeekly()
    {
        int result = 0;

        var win = new Window
        {
            Title = "Serie",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Wie oft wiederholen?", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 12) });

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        var b1 = new Button { Content = "Jede Woche", Margin = new Thickness(6), MinWidth = 160, MinHeight = 44 };
        b1.Click += (_, __) => { result = 1; win.Close(); };

        var b2 = new Button { Content = "Jede 2. Woche", Margin = new Thickness(6), MinWidth = 160, MinHeight = 44 };
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

    private static int AskSingleOrSeries(string title)
    {
        int result = 0;

        var win = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "Nur diesen Termin oder die ganze Serie?",
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

    // ===== Mitarbeiter Filter =====
    private async Task FilterEmployeesAsync()
    {
        var mv = MonthView;
        if (mv is null) return;

        var selected = new HashSet<string>(_employeeFilterIds ?? mv.Employees.Where(e => e.IsActive).Select(e => e.Id));

        var win = new Window
        {
            Title = "Mitarbeiter filtern",
            Width = 320,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White,
            Owner = Application.Current?.MainWindow
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var list = new ListBox { BorderThickness = new Thickness(1) };
        foreach (var e in mv.Employees.Where(e => e.IsActive))
        {
            var cb = new CheckBox { Content = e.DisplayName, IsChecked = selected.Contains(e.Id), Margin = new Thickness(4) };
            cb.Checked += (_, __) => selected.Add(e.Id);
            cb.Unchecked += (_, __) => selected.Remove(e.Id);
            list.Items.Add(cb);
        }

        DockPanel.SetDock(list, Dock.Top);
        root.Children.Add(list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var allBtn = new Button { Content = "Alle", Margin = new Thickness(4), MinWidth = 70 };
        allBtn.Click += (_, __) =>
        {
            selected.Clear();
            foreach (var e in mv.Employees.Where(e => e.IsActive)) selected.Add(e.Id);
            for (int i = 0; i < list.Items.Count; i++)
                ((CheckBox)list.Items[i]).IsChecked = true;
        };

        var noneBtn = new Button { Content = "Keine", Margin = new Thickness(4), MinWidth = 70 };
        noneBtn.Click += (_, __) =>
        {
            selected.Clear();
            for (int i = 0; i < list.Items.Count; i++)
                ((CheckBox)list.Items[i]).IsChecked = false;
        };

        var okBtn = new Button { Content = "OK", Margin = new Thickness(4), MinWidth = 70 };
        okBtn.Click += (_, __) => win.Close();

        buttons.Children.Add(allBtn);
        buttons.Children.Add(noneBtn);
        buttons.Children.Add(okBtn);

        root.Children.Add(buttons);

        win.Content = root;
        win.ShowDialog();

        _employeeFilterIds = selected.Count == mv.Employees.Count(e => e.IsActive) ? null : selected;
        await RefreshAsync();
    }

    // ===== Init / Refresh =====

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

        // Multi-Filter nur in der Anzeige
        if (_employeeFilterIds is not null)
        {
            var filtered = MonthView.Employees.Where(e => _employeeFilterIds.Contains(e.Id)).ToList();
            MonthView = new MonthView(MonthView.Year, MonthView.Month, filtered, MonthView.Cells);
        }

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
