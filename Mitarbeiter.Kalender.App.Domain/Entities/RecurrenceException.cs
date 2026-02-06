namespace Mitarbeiter.Kalender.App.Domain.Entities;

public sealed record RecurrenceException(
    string Id,
    string RecurrenceRuleId,
    string EmployeeId,
    DateOnly Date,
    TimeOnly Start,
    TimeOnly End,
    string CustomerName
);
