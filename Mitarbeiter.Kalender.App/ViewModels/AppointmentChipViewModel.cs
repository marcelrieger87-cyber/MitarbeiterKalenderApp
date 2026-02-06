using Mitarbeiter.Kalender.App.Domain.Enums;

namespace Mitarbeiter.Kalender.App.ViewModels;

public sealed class AppointmentChipViewModel
{
    public string Title { get; }
    public string TimeRange { get; }
    public string EmployeeId { get; }
    public AppointmentStatus Status { get; }

    public AppointmentChipViewModel(string title, string timeRange, string employeeId, AppointmentStatus status)
    {
        Title = title;
        TimeRange = timeRange;
        EmployeeId = employeeId;
        Status = status;
    }
}
