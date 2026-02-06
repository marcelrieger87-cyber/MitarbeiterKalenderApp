using Mitarbeiter.Kalender.App.Core.Abstractions;
using Mitarbeiter.Kalender.App.Core.Models;
using Mitarbeiter.Kalender.App.Domain.Entities;
using Mitarbeiter.Kalender.App.Domain.Enums;

namespace Mitarbeiter.Kalender.App.Core.Services;

public sealed class CalendarService : ICalendarService
{
    private readonly ICalendarRepository _repo;

    public CalendarService(ICalendarRepository repo)
    {
        _repo = repo;
    }

    public Task UpsertAppointmentAsync(Appointment appt, CancellationToken ct = default)
        => _repo.UpsertAppointmentAsync(appt, ct);

    public Task DeleteAppointmentAsync(string appointmentId, CancellationToken ct = default)
        => _repo.DeleteAppointmentAsync(appointmentId, ct);

    public async Task<MonthView> BuildMonthViewAsync(int year, int month, string? employeeIdFilter = null, CancellationToken ct = default)
    {
        await _repo.InitializeAsync(ct);

        var employees = await _repo.GetEmployeesAsync(ct);
        if (!string.IsNullOrWhiteSpace(employeeIdFilter))
            employees = employees.Where(e => e.Id == employeeIdFilter).ToList();

        var appts = await _repo.GetAppointmentsForMonthAsync(year, month, ct);
        var rules = await _repo.GetRecurrenceRulesAsync(ct);
        var absences = await _repo.GetAbsencesForMonthAsync(year, month, ct);
        var overrides = await _repo.GetStatusOverridesForMonthAsync(year, month, ct);

        if (!string.IsNullOrWhiteSpace(employeeIdFilter))
        {
            appts = appts.Where(a => a.EmployeeId == employeeIdFilter).ToList();
            rules = rules.Where(r => r.EmployeeId == employeeIdFilter).ToList();
            absences = absences.Where(a => a.EmployeeId == employeeIdFilter).ToList();
            overrides = overrides.Where(o => o.EmployeeId == employeeIdFilter).ToList();
        }

        // Expand recurring rules into occurrences for this month.
        var recurringOccurrences = new List<Appointment>();
        foreach (var rule in rules.Where(r => r.IsActive))
        {
            var ex = await _repo.GetRecurrenceExceptionsAsync(rule.Id, ct);
            recurringOccurrences.AddRange(ExpandWeeklyRule(year, month, rule, ex));
        }

        // Merge: explicit appointments win over recurrence.
        var merged = MergeAppointments(appts, recurringOccurrences);

        // Apply overrides (e.g., Fixed, Cancelled...)
        merged = ApplyOverrides(merged, overrides);

        // Build 6x7 grid like Excel month view
        var cells = BuildMonthGrid(year, month, merged, absences);

        return new MonthView(year, month, employees, cells);
    }

    private static IReadOnlyList<Appointment> ApplyOverrides(IReadOnlyList<Appointment> appts, IReadOnlyList<StatusOverride> overrides)
    {
        if (overrides.Count == 0) return appts;

        var list = appts.ToList();
        foreach (var ov in overrides)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a.EmployeeId == ov.EmployeeId && a.Date == ov.Date && a.Start == ov.Start && a.End == ov.End && a.CustomerName == ov.CustomerName)
                {
                    list[i] = a with { Status = ov.Status };
                }
            }
        }
        return list;
    }

    private static IReadOnlyList<Appointment> MergeAppointments(IReadOnlyList<Appointment> explicitAppts, IReadOnlyList<Appointment> recurring)
    {
        // explicit appointments override recurring ones with the same key
        var map = new Dictionary<string, Appointment>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in recurring)
            map[Key(a)] = a;

        foreach (var a in explicitAppts)
            map[Key(a)] = a;

        return map.Values
            .OrderBy(a => a.Date)
            .ThenBy(a => a.Start)
            .ToList();

        static string Key(Appointment a)
            => $"{a.EmployeeId}|{a.Date:yyyy-MM-dd}|{a.Start:HH\\:mm}|{a.End:HH\\:mm}|{a.CustomerName}";
    }

    private static IReadOnlyList<Appointment> ExpandWeeklyRule(int year, int month, RecurrenceRule rule, IReadOnlyList<RecurrenceException> exceptions)
    {
        var startOfMonth = new DateOnly(year, month, 1);
        var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

        var result = new List<Appointment>();

        // Find first occurrence on/after startOfMonth that matches weekday
        var first = startOfMonth;
        while (first.DayOfWeek != rule.Weekday)
            first = first.AddDays(1);

        // Align to anchor week interval.
        // We treat weeks starting Monday for parity.
        var anchorWeek = WeekIndex(rule.AnchorDate);
        for (var d = first; d <= endOfMonth; d = d.AddDays(7))
        {
            var week = WeekIndex(d);
            var delta = Math.Abs(week - anchorWeek);
            if (delta % Math.Max(1, rule.IntervalWeeks) != 0)
                continue;

            var isException = exceptions.Any(e => e.Date == d && e.Start == rule.Start && e.End == rule.End && e.EmployeeId == rule.EmployeeId);
            if (isException)
                continue;

            result.Add(new Appointment(
                Id: $"rec:{rule.Id}:{d:yyyyMMdd}:{rule.Start:HHmm}",
                EmployeeId: rule.EmployeeId,
                Date: d,
                Start: rule.Start,
                End: rule.End,
                CustomerName: rule.CustomerName,
                Status: AppointmentStatus.Normal,
                IsFromRecurrence: true,
                RecurrenceRuleId: rule.Id
            ));
        }

        // Add explicit exception entries as real appointments
        foreach (var ex in exceptions.Where(e => e.Date.Year == year && e.Date.Month == month))
        {
            result.Add(new Appointment(
                Id: $"ex:{ex.Id}",
                EmployeeId: ex.EmployeeId,
                Date: ex.Date,
                Start: ex.Start,
                End: ex.End,
                CustomerName: ex.CustomerName,
                Status: AppointmentStatus.Normal,
                IsFromRecurrence: true,
                RecurrenceRuleId: ex.RecurrenceRuleId
            ));
        }

        return result;

        static int WeekIndex(DateOnly date)
        {
            // Monday-based week index (rough, but stable for interval matching)
            var d = date;
            while (d.DayOfWeek != DayOfWeek.Monday)
                d = d.AddDays(-1);
            return d.DayNumber / 7;
        }
    }

    private static IReadOnlyList<DayCell> BuildMonthGrid(int year, int month, IReadOnlyList<Appointment> appts, IReadOnlyList<Absence> absences)
    {
        var first = new DateOnly(year, month, 1);

        // Monday-first like typical German calendars
        var dow = (int)first.DayOfWeek;
        // In .NET: Sunday=0 ... Saturday=6. We want Monday=0.
        var offset = (dow + 6) % 7;

        var gridStart = first.AddDays(-offset);

        var cells = new List<DayCell>(42);
        for (var i = 0; i < 42; i++)
        {
            var d = gridStart.AddDays(i);
            var dayAppts = appts.Where(a => a.Date == d).ToList();
            var dayAbs = absences.Where(a => a.Date == d).ToList();
            cells.Add(new DayCell(
                Date: d,
                IsInCurrentMonth: d.Month == month,
                Appointments: dayAppts,
                Absences: dayAbs
            ));
        }

        return cells;
    }
}
