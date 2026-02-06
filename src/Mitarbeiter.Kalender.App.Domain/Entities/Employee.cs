namespace Mitarbeiter.Kalender.App.Domain.Entities;

public sealed record Employee(
    string Id,
    string DisplayName,
    bool IsActive = true
);
