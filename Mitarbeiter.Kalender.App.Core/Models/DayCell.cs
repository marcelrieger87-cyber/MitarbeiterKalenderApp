using Mitarbeiter.Kalender.App.Domain.Entities;

namespace Mitarbeiter.Kalender.App.Core.Models;

public sealed record DayCell(
    DateOnly Date,
    bool IsInCurrentMonth,
    IReadOnlyList<Appointment> Appointments,
    IReadOnlyList<Absence> Absences
);
