using Mitarbeiter.Kalender.App.Domain.Enums;

namespace Mitarbeiter.Kalender.App.Domain.Entities;

public sealed record StatusOverride(
    string Id,
    string EmployeeId,
    DateOnly Date,
    TimeOnly Start,
    TimeOnly End,
    string CustomerName,
    AppointmentStatus Status
);
