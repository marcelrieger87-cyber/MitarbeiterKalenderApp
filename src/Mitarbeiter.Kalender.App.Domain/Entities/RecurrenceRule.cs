namespace Mitarbeiter.Kalender.App.Domain.Entities;

/// <summary>
/// Weekly recurrence like: Every N weeks on a weekday, starting at AnchorDate.
/// Mirrors the VBA table fields: MA, Weekday, StartSlot, Slots, Kunde, Active, IntervalW, AnchorDate
/// </summary>
public sealed record RecurrenceRule(
    string Id,
    string EmployeeId,
    DayOfWeek Weekday,
    TimeOnly Start,
    TimeOnly End,
    string CustomerName,
    bool IsActive,
    int IntervalWeeks,
    DateOnly AnchorDate
);
