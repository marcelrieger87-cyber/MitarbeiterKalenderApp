using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mitarbeiter.Kalender.App.Core.Models;
using Mitarbeiter.Kalender.App.Domain.Enums;
using Mitarbeiter.Kalender.App.ViewModels;

namespace Mitarbeiter.Kalender.App.Controls;

public partial class ScheduleMonthControl : UserControl
{
    // Auswahl
    private string? _selectedAppointmentId;

    public ScheduleMonthControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty MonthViewProperty =
        DependencyProperty.Register(nameof(MonthView), typeof(MonthView), typeof(ScheduleMonthControl),
            new PropertyMetadata(null, OnMonthViewChanged));

    public MonthView? MonthView
    {
        get => (MonthView?)GetValue(MonthViewProperty);
        set => SetValue(MonthViewProperty, value);
    }

    public static readonly DependencyProperty CellClickCommandProperty =
        DependencyProperty.Register(nameof(CellClickCommand), typeof(ICommand), typeof(ScheduleMonthControl),
            new PropertyMetadata(null));

    public ICommand? CellClickCommand
    {
        get => (ICommand?)GetValue(CellClickCommandProperty);
        set => SetValue(CellClickCommandProperty, value);
    }

    private static void OnMonthViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScheduleMonthControl c) c.Rebuild();
    }

    private const double TimeColWidth = 70;
    private const double DayColWidth = 85;
    private const double HeaderRowHeight = 28;
    private const double EmployeeTitleHeight = 26;
    private const double SlotRowHeight = 22;

    private static readonly TimeOnly StartTime = new(7, 0);
    private static readonly TimeOnly EndTime = new(20, 0);
    private const int SlotMinutes = 30;

    private void Rebuild()
    {
        RootGrid.Children.Clear();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();

        var mv = MonthView;
        if (mv is null) return;

        var holidays = GetHolidaysLowerSaxony(mv.Year);

        var days = GetDaysOfMonth(mv.Year, mv.Month);
        var slots = GetSlots(StartTime, EndTime, SlotMinutes);

        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimeColWidth) });
        for (int i = 0; i < days.Count; i++)
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DayColWidth) });

        int row = 0;

        // Header
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderRowHeight) });
        AddCell(row, 0, "Zeit", isHeader: true, bg: TryFindBrush("HeaderBlue"), fg: Brushes.White, fontWeight: FontWeights.SemiBold);

        for (int d = 0; d < days.Count; d++)
        {
            var date = days[d];
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var isHoliday = holidays.Contains(date);

            var headerText = $"{GetGermanShortDow(date.DayOfWeek)}\n{date:dd.MM.}";
            AddCell(row, d + 1, headerText,
                isHeader: true,
                bg: isHoliday ? TryFindBrush("HolidayGreen") : (isWeekend ? TryFindBrush("WeekendGreen") : TryFindBrush("HeaderBlue")),
                fg: Brushes.White,
                fontWeight: FontWeights.SemiBold,
                textAlign: TextAlignment.Center);
        }

        row++;

        foreach (var emp in mv.Employees.Where(e => e.IsActive))
        {
            // Employee title row
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(EmployeeTitleHeight) });
            AddCell(row, 0, "Mitarbeiter:", isHeader: true, bg: TryFindBrush("HeaderBlue"), fg: Brushes.White, fontWeight: FontWeights.SemiBold);

            var title = new Border
            {
                Background = TryFindBrush("HeaderBlue"),
                BorderBrush = TryFindBrush("Border"),
                BorderThickness = new Thickness(0.5),
                Child = new Grid
                {
                    Margin = new Thickness(8, 0, 8, 0),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    }
                }
            };

            var g = (Grid)title.Child;
            var t1 = new TextBlock { Text = emp.DisplayName, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            var t2 = new TextBlock { Text = emp.DisplayName, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var t3 = new TextBlock { Text = emp.DisplayName, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(t1, 0); Grid.SetColumn(t2, 1); Grid.SetColumn(t3, 2);
            g.Children.Add(t1); g.Children.Add(t2); g.Children.Add(t3);

            Grid.SetRow(title, row);
            Grid.SetColumn(title, 1);
            Grid.SetColumnSpan(title, days.Count);
            RootGrid.Children.Add(title);

            row++;

            // Slot rows
            for (int s = 0; s < slots.Count; s++)
            {
                var slotStart = slots[s];
                RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SlotRowHeight) });

                AddCell(row, 0, slotStart.ToString("HH:mm", CultureInfo.InvariantCulture),
                    isHeader: false,
                    bg: TryFindBrush("SurfaceAlt"),
                    fg: TryFindBrush("Text"),
                    fontWeight: FontWeights.SemiBold,
                    textAlign: TextAlignment.Right,
                    padding: new Thickness(0, 0, 6, 0));

                for (int d = 0; d < days.Count; d++)
                {
                    var date = days[d];
                    var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                    var isHoliday = holidays.Contains(date);

                    var cellBg = isHoliday ? TryFindBrush("HolidayFill")
                        : (isWeekend ? TryFindBrush("WeekendFill") : Brushes.Transparent);

                    var apptAtSlot = FindAppointmentAt(mv, emp.Id, date, slotStart);
                    var isSelectedBlock = apptAtSlot is not null && apptAtSlot.Id == _selectedAppointmentId;

                    UIElement? content = null;
                    if (apptAtSlot is not null)
                        content = MakeChip(apptAtSlot.CustomerName, StatusToBrush(apptAtSlot.Status));

                    var border = MakeClickableCell(mv, emp.Id, date, slotStart, cellBg, content, apptAtSlot?.Id, isSelectedBlock);

                    Grid.SetRow(border, row);
                    Grid.SetColumn(border, d + 1);
                    RootGrid.Children.Add(border);
                }

                row++;
            }

            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            row++;
        }
    }

    private Border MakeClickableCell(MonthView mv, string employeeId, DateOnly date, TimeOnly slotStart, Brush bg, UIElement? content, string? apptId, bool isSelectedBlock)
    {
        var border = new Border
        {
            Background = bg,
            BorderBrush = isSelectedBlock ? TryFindBrush("HeaderBlue") : TryFindBrush("GridLine"),
            BorderThickness = isSelectedBlock ? new Thickness(2) : new Thickness(0.5),
            Padding = new Thickness(2, 1, 2, 1),
            Child = content,
            Cursor = Cursors.Hand
        };

        border.MouseLeftButtonUp += (_, __) =>
        {
            // ✅ Wenn Termin angeklickt: ganzer Block markiert
            _selectedAppointmentId = apptId;

            // UI neu zeichnen
            Rebuild();

            // ViewModel informieren
            var cmd = CellClickCommand;
            if (cmd is null) return;

            var payload = new CalendarCellRef(employeeId, date, slotStart);
            if (cmd.CanExecute(payload))
                cmd.Execute(payload);
        };

        return border;
    }

    private Appointment? FindAppointmentAt(MonthView mv, string employeeId, DateOnly date, TimeOnly slotStart)
    {
        var dayCell = mv.Cells.FirstOrDefault(c => c.Date == date);
        if (dayCell is null) return null;

        return dayCell.Appointments
            .Where(a => a.EmployeeId == employeeId)
            .FirstOrDefault(a => a.Start <= slotStart && a.End > slotStart);
    }

    private UIElement MakeChip(string text, Brush brush)
        => new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

    private void AddCell(int row, int col, string text, bool isHeader, Brush? bg, Brush? fg,
        FontWeight? fontWeight = null, TextAlignment textAlign = TextAlignment.Left, Thickness? padding = null)
    {
        var border = new Border
        {
            Background = bg ?? Brushes.Transparent,
            BorderBrush = TryFindBrush(isHeader ? "Border" : "GridLine"),
            BorderThickness = new Thickness(0.5),
            Padding = padding ?? new Thickness(6, 2, 6, 2)
        };

        var tb = new TextBlock
        {
            Text = text,
            Foreground = fg ?? TryFindBrush("Text"),
            FontWeight = fontWeight ?? FontWeights.Normal,
            TextAlignment = textAlign,
            VerticalAlignment = VerticalAlignment.Center
        };

        border.Child = tb;
        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        RootGrid.Children.Add(border);
    }

    private static List<DateOnly> GetDaysOfMonth(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var list = new List<DateOnly>(daysInMonth);
        for (int i = 0; i < daysInMonth; i++)
            list.Add(first.AddDays(i));
        return list;
    }

    private static List<TimeOnly> GetSlots(TimeOnly start, TimeOnly end, int stepMinutes)
    {
        var list = new List<TimeOnly>();
        var t = start;
        while (t <= end)
        {
            list.Add(t);
            t = t.AddMinutes(stepMinutes);
        }
        return list;
    }

    private Brush TryFindBrush(string key)
    {
        if (TryFindResource(key) is Brush b) return b;
        return new SolidColorBrush(Color.FromRgb(60, 60, 60));
    }

    private static Brush StatusToBrush(AppointmentStatus s)
        => s switch
        {
            AppointmentStatus.Fixed => new SolidColorBrush(Color.FromRgb(46, 125, 50)),
            AppointmentStatus.Tentative => new SolidColorBrush(Color.FromRgb(245, 124, 0)),
            AppointmentStatus.Cancelled => new SolidColorBrush(Color.FromRgb(211, 47, 47)),
            _ => new SolidColorBrush(Color.FromRgb(25, 118, 210)),
        };

    private static string GetGermanShortDow(DayOfWeek dow)
        => dow switch
        {
            DayOfWeek.Monday => "Mo",
            DayOfWeek.Tuesday => "Di",
            DayOfWeek.Wednesday => "Mi",
            DayOfWeek.Thursday => "Do",
            DayOfWeek.Friday => "Fr",
            DayOfWeek.Saturday => "Sa",
            DayOfWeek.Sunday => "So",
            _ => "?"
        };

    // ✅ Feiertage Niedersachsen (bundesweit + Reformationstag)
    private static HashSet<DateOnly> GetHolidaysLowerSaxony(int year)
    {
        var set = new HashSet<DateOnly>();

        // Fixe Feiertage
        set.Add(new DateOnly(year, 1, 1));   // Neujahr
        set.Add(new DateOnly(year, 5, 1));   // Tag der Arbeit
        set.Add(new DateOnly(year, 10, 3));  // Tag der Deutschen Einheit
        set.Add(new DateOnly(year, 10, 31)); // Reformationstag (NDS)
        set.Add(new DateOnly(year, 12, 25)); // 1. Weihnachtstag
        set.Add(new DateOnly(year, 12, 26)); // 2. Weihnachtstag

        // Bewegliche (Easter-based)
        var easter = EasterSunday(year);
        set.Add(easter.AddDays(-2)); // Karfreitag
        set.Add(easter.AddDays(1));  // Ostermontag
        set.Add(easter.AddDays(39)); // Christi Himmelfahrt
        set.Add(easter.AddDays(50)); // Pfingstmontag

        return set;
    }

    private static DateOnly EasterSunday(int year)
    {
        // Anonymous Gregorian algorithm (Meeus/Jones/Butcher)
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
