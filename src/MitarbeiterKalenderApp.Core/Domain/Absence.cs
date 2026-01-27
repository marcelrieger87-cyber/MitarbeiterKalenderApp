namespace MitarbeiterKalenderApp.Core.Domain;

public enum AbsenceType
{
    Vacation = 1,
    Sick = 2,
    Training = 3,
    Other = 9
}

public sealed class Absence
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid EmployeeId { get; init; }

    /// <summary>Ganztägige Abwesenheit (typisch Excel-Logik).</summary>
    public required DateOnly Date { get; set; }

    public required AbsenceType Type { get; set; }

    public string? Note { get; set; }
}
