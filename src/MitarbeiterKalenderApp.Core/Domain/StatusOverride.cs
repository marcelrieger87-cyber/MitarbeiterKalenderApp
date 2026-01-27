namespace MitarbeiterKalenderApp.Core.Domain;

public enum DayStatus
{
    None = 0,
    Office = 1,
    HomeOffice = 2,
    External = 3,
    NotAvailable = 4
}

/// <summary>
/// Status pro Tag, der den "Standard" im Kalender überschreiben kann (wie Excel-Statusfelder).
/// </summary>
public sealed class StatusOverride
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid EmployeeId { get; init; }

    public required DateOnly Date { get; set; }

    public required DayStatus Status { get; set; }

    public string? Note { get; set; }
}
