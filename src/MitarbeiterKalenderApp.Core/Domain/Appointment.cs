namespace MitarbeiterKalenderApp.Core.Domain;

public sealed class Appointment
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid EmployeeId { get; init; }

    /// <summary>Optional: Zuordnung zu Kundendaten.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Datum (Tag) des Termins.</summary>
    public required DateOnly Date { get; set; }

    /// <summary>Startzeit (z.B. 07:00).</summary>
    public required TimeOnly StartTime { get; set; }

    /// <summary>Endzeit (z.B. 08:30). Muss > StartTime sein.</summary>
    public required TimeOnly EndTime { get; set; }

    /// <summary>Text wie in Excel-Zelle (Titel/Notiz).</summary>
    public string Title { get; set; } = "";

    public string? Notes { get; set; }

    /// <summary>Wenn gesetzt: Termin gehört zu einer Serie.</summary>
    public Guid? RecurrenceRuleId { get; set; }

    public bool IsCanceled { get; set; } = false;

    public void Validate()
    {
        if (EndTime <= StartTime)
            throw new InvalidOperationException("EndTime muss größer als StartTime sein.");
    }
}
