using Mitarbeiter.Kalender.App.Domain.Enums;

namespace Mitarbeiter.Kalender.App.Domain.Entities;

public sealed record Appointment(
    string Id,
    string EmployeeId,
    DateOnly Date,
    TimeOnly Start,
    TimeOnly End,
    string CustomerName,
    AppointmentStatus Status = AppointmentStatus.Normal,
    bool IsFromRecurrence = false,
    string? RecurrenceRuleId = null
);
