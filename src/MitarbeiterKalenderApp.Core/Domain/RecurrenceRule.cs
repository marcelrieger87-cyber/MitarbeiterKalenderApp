namespace MitarbeiterKalenderApp.Core.Domain;

public enum RecurrenceFrequency
{
    Daily = 1,
    Weekly = 2,
    Monthly = 3
}

/// <summary>
/// Serienregel (Excel "Serie").
/// Erzeugt Occurrences zwischen StartDate und optional UntilDate.
/// </summary>
public sealed class RecurrenceRule
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid EmployeeId { get; init; }

    public required DateOnly StartDate { get; init; }

    /// <summary>Optionales Ende der Serie (inklusive).</summary>
    public DateOnly? UntilDate { get; set; }

    public required RecurrenceFrequency Frequency { get; set; }

    /// <summary>Intervall: z.B. alle 2 Wochen.</summary>
    public int Interval { get; set; } = 1;

    /// <summary>
    /// Nur für Weekly: Wochentage, an denen die Serie gilt.
    /// Wenn leer -> Wochentag von StartDate.
    /// </summary>
    public HashSet<DayOfWeek> ByWeekDays { get; set; } = new();

    /// <summary>Startzeit jedes Occurrence.</summary>
    public required TimeOnly StartTime { get; set; }

    /// <summary>Endzeit jedes Occurrence.</summary>
    public required TimeOnly EndTime { get; set; }

    public string Title { get; set; } = "";
    public string? Notes { get; set; }

    public void Validate()
    {
        if (Interval <= 0) throw new InvalidOperationException("Interval muss >= 1 sein.");
        if (EndTime <= StartTime) throw new InvalidOperationException("EndTime muss größer als StartTime sein.");
    }
}
