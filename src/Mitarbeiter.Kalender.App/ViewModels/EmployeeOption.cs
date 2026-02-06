namespace Mitarbeiter.Kalender.App.ViewModels;

public sealed record EmployeeOption(string? Id, string DisplayName)
{
    public override string ToString() => DisplayName;

    public static EmployeeOption All { get; } = new(null, "Alle Mitarbeiter");
}
