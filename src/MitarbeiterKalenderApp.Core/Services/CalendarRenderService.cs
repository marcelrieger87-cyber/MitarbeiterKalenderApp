using MitarbeiterKalenderApp.Core.Domain;

namespace MitarbeiterKalenderApp.Core.Services;

/// <summary>
/// Herzstück: baut aus Einzelterminen + Serien (+ Ausnahmen) + Abwesenheit/Status
/// einen Monats-Render (Tag -> Termine + Meta).
/// </summary>
public sealed class CalendarRenderService
{
    public MonthCalendar RenderMonth(
        Guid employeeId,
        int year,
        int month,
        IReadOnlyList<Appointment> appointments,
        IReadOnlyList<RecurrenceRule> rules,
        IReadOnlyList<RecurrenceException> exceptions,
        IReadOnlyList<Absence> absences,
        IReadOnlyList<StatusOverride> statuses,
        TimeOnly? dayStart = null,
        TimeOnly? dayEnd = null,
        int slotMinutes = 30)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        var ds = dayStart ?? new TimeOnly(7, 0);
        var de = dayEnd ?? new TimeOnly(20, 0);

        if (slotMinutes <= 0) throw new InvalidOperationException("slotMinutes muss > 0 sein.");
        if (de <= ds) throw new InvalidOperationException("dayEnd muss größer als dayStart sein.");

        // 1) Tages-Container vorbereiten
        var days = new Dictionary<DateOnly, DayCalendar>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            days[d] = new DayCalendar
            {
                Date = d,
                EmployeeId = employeeId,
                DayStart = ds,
                DayEnd = de,
                SlotMinutes = slotMinutes
            };
        }

        // 2) Index: Abwesenheit/Status
        var absenceByDay = absences
            .Where(a => a.EmployeeId == employeeId && a.Date >= start && a.Date <= end)
            .GroupBy(a => a.Date)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Type).ToList());

        var statusByDay = statuses
            .Where(s => s.EmployeeId == employeeId && s.Date >= start && s.Date <= end)
            .GroupBy(s => s.Date)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Status).ToList());

        foreach (var kv in days)
        {
            if (absenceByDay.TryGetValue(kv.Key, out var abs))
                kv.Value.Absences.AddRange(abs);

            if (statusByDay.TryGetValue(kv.Key, out var st))
                kv.Value.StatusOverrides.AddRange(st);
        }

        // 3) Einzeltermine (keine Serie)
        foreach (var ap in appointments.Where(a =>
                     a.EmployeeId == employeeId &&
                     a.RecurrenceRuleId is null &&
                     a.Date >= start && a.Date <= end &&
                     !a.IsCanceled))
        {
            ap.Validate();

            var item = new RenderedAppointment
            {
                Source = RenderSource.SingleAppointment,
                SourceId = ap.Id,
                EmployeeId = ap.EmployeeId,
                Date = ap.Date,
                StartTime = ap.StartTime,
                EndTime = ap.EndTime,
                Title = ap.Title,
                Notes = ap.Notes,
                CustomerId = ap.CustomerId
            };

            AddToDay(days, item);
        }

        // 4) Serien expandieren
        var ruleById = rules.Where(r => r.EmployeeId == employeeId).ToDictionary(r => r.Id, r => r);

        // Exceptions indexen
        var exByRuleAndDay = exceptions
            .Where(e => ruleById.ContainsKey(e.RecurrenceRuleId))
            .GroupBy(e => (e.RecurrenceRuleId, e.Date))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).First()); // 1 pro Tag (falls doppelt)

        foreach (var rule in rules.Where(r => r.EmployeeId == employeeId))
        {
            rule.Validate();

            var until = rule.UntilDate ?? end;
            if (until < start) continue;

            // Occurrence Window = Schnitt aus [rule.StartDate..until] und [start..end]
            var occStart = rule.StartDate > start ? rule.StartDate : start;
            var occEnd = until < end ? until : end;
            if (occEnd < occStart) continue;

            // Expand je Frequenz
            switch (rule.Frequency)
            {
                case RecurrenceFrequency.Daily:
                    ExpandDaily(rule, occStart, occEnd, exByRuleAndDay, days);
                    break;

                case RecurrenceFrequency.Weekly:
                    ExpandWeekly(rule, occStart, occEnd, exByRuleAndDay, days);
                    break;

                case RecurrenceFrequency.Monthly:
                    ExpandMonthly(rule, occStart, occEnd, exByRuleAndDay, days);
                    break;

                default:
                    throw new InvalidOperationException($"Unbekannte Frequency: {rule.Frequency}");
            }
        }

        // 5) Sortieren pro Tag (wie Excel: chronologisch)
        foreach (var day in days.Values)
        {
            day.Appointments.Sort((a, b) =>
            {
                var c = a.StartTime.CompareTo(b.StartTime);
                if (c != 0) return c;
                c = a.EndTime.CompareTo(b.EndTime);
                if (c != 0) return c;
                return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });
        }

        return new MonthCalendar
        {
            EmployeeId = employeeId,
            Year = year,
            Month = month,
            DayStart = ds,
            DayEnd = de,
            SlotMinutes = slotMinutes,
            Days = days.Values.OrderBy(d => d.Date).ToList()
        };
    }

    private static void AddToDay(Dictionary<DateOnly, DayCalendar> days, RenderedAppointment item)
    {
        if (!days.TryGetValue(item.Date, out var day)) return;

        // Clip optional (Excel-like: außerhalb Tagesfenster ignorieren)
        if (item.EndTime <= day.DayStart) return;
        if (item.StartTime >= day.DayEnd) return;

        day.Appointments.Add(item);
    }

    private static void ExpandDaily(
        RecurrenceRule rule,
        DateOnly occStart,
        DateOnly occEnd,
        Dictionary<(Guid RuleId, DateOnly Day), RecurrenceException> exByRuleAndDay,
        Dictionary<DateOnly, DayCalendar> days)
    {
        // Intervall ab rule.StartDate zählen
        for (var d = occStart; d <= occEnd; d = d.AddDays(1))
        {
            var deltaDays = DaysBetween(rule.StartDate, d);
            if (deltaDays < 0) continue;
            if (deltaDays % rule.Interval != 0) continue;

            EmitOccurrence(rule, d, exByRuleAndDay, days);
        }
    }

    private static void ExpandWeekly(
        RecurrenceRule rule,
        DateOnly occStart,
        DateOnly occEnd,
        Dictionary<(Guid RuleId, DateOnly Day), RecurrenceException> exByRuleAndDay,
        Dictionary<DateOnly, DayCalendar> days)
    {
        var weekDays = rule.ByWeekDays.Count > 0
            ? rule.ByWeekDays
            : new HashSet<DayOfWeek> { rule.StartDate.DayOfWeek };

        for (var d = occStart; d <= occEnd; d = d.AddDays(1))
        {
            if (!weekDays.Contains(d.DayOfWeek)) continue;

            // Wochenabstand ab StartDate (auf Wochenbasis)
            var weeks = WeeksBetween(rule.StartDate, d);
            if (weeks < 0) continue;
            if (weeks % rule.Interval != 0) continue;

            EmitOccurrence(rule, d, exByRuleAndDay, days);
        }
    }

    private static void ExpandMonthly(
        RecurrenceRule rule,
        DateOnly occStart,
        DateOnly occEnd,
        Dictionary<(Guid RuleId, DateOnly Day), RecurrenceException> exByRuleAndDay,
        Dictionary<DateOnly, DayCalendar> days)
    {
        // Monatlich: gleicher Tag-im-Monat wie StartDate (Excel-typisch)
        var anchorDay = rule.StartDate.Day;

        // Iteriere Monate im Window
        var cursor = new DateOnly(occStart.Year, occStart.Month, 1);
        var endMonth = new DateOnly(occEnd.Year, occEnd.Month, 1);

        while (cursor <= endMonth)
        {
            var months = MonthsBetween(new DateOnly(rule.StartDate.Year, rule.StartDate.Month, 1), cursor);
            if (months >= 0 && months % rule.Interval == 0)
            {
                var day = SafeDay(cursor.Year, cursor.Month, anchorDay);
                if (day >= occStart && day <= occEnd)
                {
                    EmitOccurrence(rule, day, exByRuleAndDay, days);
                }
            }

            cursor = cursor.AddMonths(1);
        }
    }

    private static void EmitOccurrence(
        RecurrenceRule rule,
        DateOnly day,
        Dictionary<(Guid RuleId, DateOnly Day), RecurrenceException> exByRuleAndDay,
        Dictionary<DateOnly, DayCalendar> days)
    {
        // Exception?
        if (exByRuleAndDay.TryGetValue((rule.Id, day), out var ex))
        {
            ex.Validate();
            if (ex.IsCanceled) return;

            var start = ex.OverrideStartTime ?? rule.StartTime;
            var end = ex.OverrideEndTime ?? rule.EndTime;
            if (end <= start) return;

            var itemEx = new RenderedAppointment
            {
                Source = RenderSource.Recurrence,
                SourceId = rule.Id,
                EmployeeId = rule.EmployeeId,
                Date = day,
                StartTime = start,
                EndTime = end,
                Title = ex.OverrideTitle ?? rule.Title,
                Notes = ex.OverrideNotes ?? rule.Notes,
                RecurrenceRuleId = rule.Id,
                RecurrenceExceptionId = ex.Id
            };

            AddToDay(days, itemEx);
            return;
        }

        // Normaler Occurrence
        var item = new RenderedAppointment
        {
            Source = RenderSource.Recurrence,
            SourceId = rule.Id,
            EmployeeId = rule.EmployeeId,
            Date = day,
            StartTime = rule.StartTime,
            EndTime = rule.EndTime,
            Title = rule.Title,
            Notes = rule.Notes,
            RecurrenceRuleId = rule.Id
        };

        AddToDay(days, item);
    }

    private static int DaysBetween(DateOnly a, DateOnly b)
        => b.DayNumber - a.DayNumber;

    private static int WeeksBetween(DateOnly a, DateOnly b)
        => (b.DayNumber - a.DayNumber) / 7;

    private static int MonthsBetween(DateOnly aMonth, DateOnly bMonth)
        => (bMonth.Year - aMonth.Year) * 12 + (bMonth.Month - aMonth.Month);

    private static DateOnly SafeDay(int year, int month, int desiredDay)
    {
        var dim = DateTime.DaysInMonth(year, month);
        var d = desiredDay <= dim ? desiredDay : dim;
        return new DateOnly(year, month, d);
    }
}

/// <summary>Gerenderter Monat: Liste Tage inkl. Termine + Status/Abwesenheit.</summary>
public sealed class MonthCalendar
{
    public required Guid EmployeeId { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }

    public required TimeOnly DayStart { get; init; }
    public required TimeOnly DayEnd { get; init; }
    public required int SlotMinutes { get; init; }

    public List<DayCalendar> Days { get; init; } = new();
}

public sealed class DayCalendar
{
    public required Guid EmployeeId { get; init; }
    public required DateOnly Date { get; init; }

    public required TimeOnly DayStart { get; init; }
    public required TimeOnly DayEnd { get; init; }
    public required int SlotMinutes { get; init; }

    public List<RenderedAppointment> Appointments { get; } = new();
    public List<Absence> Absences { get; } = new();
    public List<StatusOverride> StatusOverrides { get; } = new();
}

public enum RenderSource
{
    SingleAppointment = 1,
    Recurrence = 2
}

/// <summary>Ein Eintrag, der im Kalender angezeigt/ exportiert wird.</summary>
public sealed class RenderedAppointment
{
    public required RenderSource Source { get; init; }

    /// <summary>Bei Single: Appointment.Id, bei Serie: RecurrenceRule.Id</summary>
    public required Guid SourceId { get; init; }

    public required Guid EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required TimeOnly StartTime { get; init; }
    public required TimeOnly EndTime { get; init; }

    public string Title { get; init; } = "";
    public string? Notes { get; init; }

    public Guid? CustomerId { get; init; }

    public Guid? RecurrenceRuleId { get; init; }
    public Guid? RecurrenceExceptionId { get; init; }
}
