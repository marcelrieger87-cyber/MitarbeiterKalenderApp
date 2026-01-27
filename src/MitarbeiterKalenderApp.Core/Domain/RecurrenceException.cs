namespace MitarbeiterKalenderApp.Core.Domain;

/// <summary>
/// Ausnahme für eine Serie:
/// - Cancelled: Occurrence entfällt
/// - oder: Zeit/Text anpassen für genau einen Tag.
/// </summary>
public sealed class RecurrenceException
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid RecurrenceRuleId { get; init; }

    /// <summary>Welcher Tag der Serie betroffen ist.</summary>
    public required DateOnly Date { get; init; }

    public bool IsCanceled { get; set; } = false;

    public TimeOnly? OverrideStartTime { get; set; }
    public TimeOnly? OverrideEndTime { get; set; }

    public string? OverrideTitle { get; set; }
    public string? OverrideNotes { get; set; }

    public void Validate()
    {
        if (OverrideStartTime.HasValue && OverrideEndTime.HasValue && OverrideEndTime <= OverrideStartTime)
            throw new InvalidOperationException("OverrideEndTime muss größer als OverrideStartTime sein.");
    }
}
