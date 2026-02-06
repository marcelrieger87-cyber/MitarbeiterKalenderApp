using Mitarbeiter.Kalender.App.Domain.Entities;

namespace Mitarbeiter.Kalender.App.ViewModels;

/// <summary>
/// Referenz auf eine konkrete Kalender-Zelle
/// (Excel-Äquivalent: Mitarbeiter + Datum + Uhrzeit-Slot)
/// </summary>
public sealed class CalendarCellRef
{
    /// <summary> Mitarbeiter-ID (Zeile) </summary>
    public string EmployeeId { get; init; } = string.Empty;

    /// <summary> Datum (Spalte / Tag) </summary>
    public DateOnly Date { get; init; }

    /// <summary> Startzeit des Slots (z.B. 08:30) </summary>
    public TimeOnly SlotStart { get; init; }

    public CalendarCellRef() { }

    public CalendarCellRef(string employeeId, DateOnly date, TimeOnly slotStart)
    {
        EmployeeId = employeeId;
        Date = date;
        SlotStart = slotStart;
    }
}
