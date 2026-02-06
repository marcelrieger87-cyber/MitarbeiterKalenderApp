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
    // ✅ Markierung (Excel-like)
    private string? _selectedEmployeeId;
    private DateOnly? _selectedDate;
    private TimeOnly? _selectedSlotStart;

    public ScheduleMonthControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty MonthViewProperty =
        DependencyProperty.Register(
            nameof(MonthView),
            typeof(MonthView),
            typeof(ScheduleMonthControl),
            new PropertyMetadata(null, OnMonthViewChanged));

    public MonthView? MonthView
    {
        get => (MonthView?)GetValue(MonthViewProperty);
        set => SetValue(MonthViewProperty, value);
    }

    // ✅ Command für Klick in Kalenderzelle
    public static readonly DependencyProperty CellClickCommandProperty =
        DependencyProperty.Register(
            nameof(CellClickCommand),
            typeof(ICommand),
            typeof(ScheduleMonthControl),
            new PropertyMetadata(null));

    public ICommand? CellClickCommand
    {
        get => (ICommand?)GetValue(CellClickCommandProperty);
        set => SetValue(CellClickCommandProperty, value);
    }

    private static void OnMonthViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScheduleMonthControl c)
            c.Rebuild();
    }

    // ===== Excel-like layout constants =====
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

        var days = GetDaysOfMonth(mv.Year, mv.Month);
        var slots = GetSlots(StartTime, EndTime, SlotMinutes);

        // Columns: [Time] + [Day1..DayN]
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimeColWidth) });
        for (int i = 0; i < days.Count; i++)
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DayColWidth) });

        int row = 0;

        // Header row
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderRowHeight) });

        AddCell(row, 0, "Zeit", isHeader: true, bg: TryFindBrush("HeaderBlue"), fg: Brushes.White, fontWeight: FontWeights.SemiBold);

        for (int d = 0; d < days.Count; d++)
        {
            var date = days[d];
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            var headerText = $"{GetGermanShortDow(date.DayOfWeek)}\n{date:dd.MM.}";
            AddCell(row, d + 1, headerText,
                isHeader: true,
                bg: isWeekend ? TryFindBrush("WeekendGreen") : TryFindBrush("HeaderBlue"),
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

                    var cellBg = (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                        ? TryFindBrush("WeekendFill")
                        : Brushes.Transparent;

                    var cellItems = FindItemsForSlot(mv, emp.Id, date, slotStart, SlotMinutes);

                    Border cell;
                    UIElement? content = null;

                    if (cellItems.Count > 0)
                    {
                        var first = cellItems[0];
                        content = MakeChip(first.Text, first.StatusBrush);
                    }

                    var isSelected =
                        _selectedEmployeeId == emp.Id &&
                        _selectedDate == date &&
                        _selectedSlotStart == slotStart;

                    cell = MakeClickableCell(emp.Id, date, slotStart, cellBg, content, isSelected);

                    Grid.SetRow(cell, row);
                    Grid.SetColumn(cell, d + 1);
                    RootGrid.Children.Add(cell);
                }

                row++;
            }

            // Spacer
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            row++;
        }
    }

    private Border MakeClickableCell(string employeeId, DateOnly date, TimeOnly slotStart, Brush bg, UIElement? content, bool isSelected)
    {
        var border = new Border
        {
            Background = bg,
            BorderBrush = isSelected ? TryFindBrush("HeaderBlue") : TryFindBrush("GridLine"),
            BorderThickness = isSelected ? new Thickness(2) : new Thickness(0.5),
            Padding = new Thickness(2, 1, 2, 1),
            Child = content
        };

        border.Cursor = Cursors.Hand;
        border.MouseLeftButtonUp += (_, __) =>
        {
            // ✅ Immer markieren (egal welcher Modus)
            _selectedEmployeeId = employeeId;
            _selectedDate = date;
            _selectedSlotStart = slotStart;

            // Rebuild für saubere Markierung
            Rebuild();

            // Command feuern
            var cmd = CellClickCommand;
            if (cmd is null) return;

            var payload = new CalendarCellRef(employeeId, date, slotStart);

            if (cmd.CanExecute(payload))
                cmd.Execute(payload);
        };

        return border;
    }

    private sealed record CellItem(string Text, Brush StatusBrush);

    private List<CellItem> FindItemsForSlot(MonthView mv, string employeeId, DateOnly date, TimeOnly slotStart, int slotMinutes)
    {
        var list = new List<CellItem>();
        var slotEnd = slotStart.AddMinutes(slotMinutes);

        var dayCell = mv.Cells.FirstOrDefault(c => c.Date == date);
        if (dayCell is null) return list;

        foreach (var a in dayCell.Appointments.Where(a => a.EmployeeId == employeeId))
        {
            if (a.End <= slotStart || a.Start >= slotEnd) continue;
            list.Add(new CellItem(a.CustomerName, StatusToBrush(a.Status)));
        }

        foreach (var ab in dayCell.Absences.Where(x => x.EmployeeId == employeeId))
        {
            list.Add(new CellItem("ABW", TryFindBrush("AbsenceBrush")));
        }

        return list;
    }

    private UIElement MakeChip(string text, Brush brush)
    {
        return new Border
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
    }

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
}
