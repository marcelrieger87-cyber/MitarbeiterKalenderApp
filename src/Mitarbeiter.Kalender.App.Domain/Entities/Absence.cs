using Mitarbeiter.Kalender.App.Domain.Enums;

namespace Mitarbeiter.Kalender.App.Domain.Entities;

public sealed record Absence(
    string Id,
    string EmployeeId,
    DateOnly Date,
    AbsenceType Type,
    string? Note = null
);
