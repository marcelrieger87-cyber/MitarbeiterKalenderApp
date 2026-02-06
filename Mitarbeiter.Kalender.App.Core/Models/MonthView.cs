using Mitarbeiter.Kalender.App.Domain.Entities;

namespace Mitarbeiter.Kalender.App.Core.Models;

public sealed record MonthView(
    int Year,
    int Month,
    IReadOnlyList<Employee> Employees,
    IReadOnlyList<DayCell> Cells
);
