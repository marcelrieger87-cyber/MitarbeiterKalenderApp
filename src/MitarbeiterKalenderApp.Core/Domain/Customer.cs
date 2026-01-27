namespace MitarbeiterKalenderApp.Core.Domain;

public sealed class Customer
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string DisplayName { get; init; }

    public string? CustomerNumber { get; init; }
    public string? AddressLine { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }

    public bool IsActive { get; set; } = true;

    public override string ToString() => DisplayName;
}
