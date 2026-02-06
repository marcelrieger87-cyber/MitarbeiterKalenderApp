using Mitarbeiter.Kalender.App.Core.Models;
using Mitarbeiter.Kalender.App.Domain.Entities;

namespace Mitarbeiter.Kalender.App.Core.Abstractions;

public interface ICalendarService
{
    Task<MonthView> BuildMonthViewAsync(int year, int month, string? employeeIdFilter = null, CancellationToken ct = default);

    Task UpsertAppointmentAsync(Appointment appt, CancellationToken ct = default);
    Task DeleteAppointmentAsync(string appointmentId, CancellationToken ct = default);
}
