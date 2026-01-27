namespace MitarbeiterKalenderApp.Core.Domain;

public sealed class Employee
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Anzeige-/Ordnername (wird für Exportpfad genutzt).</summary>
    public required string Name { get; init; }

    /// <summary>Optional: Kürzel wie in Excel.</summary>
    public string? ShortCode { get; init; }

    public bool IsActive { get; set; } = true;

    public override string ToString() => Name;
}
