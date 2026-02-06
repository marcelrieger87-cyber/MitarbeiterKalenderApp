using Mitarbeiter.Kalender.App.Domain.Entities;

namespace Mitarbeiter.Kalender.App.Core.Abstractions;

public interface ICalendarRepository
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Employee>> GetEmployeesAsync(CancellationToken ct = default);
    Task SaveEmployeesAsync(IEnumerable<Employee> employees, CancellationToken ct = default);

    Task<IReadOnlyList<Appointment>> GetAppointmentsForMonthAsync(int year, int month, CancellationToken ct = default);
    Task UpsertAppointmentAsync(Appointment appointment, CancellationToken ct = default);
    Task DeleteAppointmentAsync(string appointmentId, CancellationToken ct = default);

    Task<IReadOnlyList<RecurrenceRule>> GetRecurrenceRulesAsync(CancellationToken ct = default);
    Task UpsertRecurrenceRuleAsync(RecurrenceRule rule, CancellationToken ct = default);
    Task DeleteRecurrenceRuleAsync(string ruleId, CancellationToken ct = default);

    Task<IReadOnlyList<RecurrenceException>> GetRecurrenceExceptionsAsync(string ruleId, CancellationToken ct = default);
    Task UpsertRecurrenceExceptionAsync(RecurrenceException ex, CancellationToken ct = default);
    Task DeleteRecurrenceExceptionAsync(string exceptionId, CancellationToken ct = default);

    Task<IReadOnlyList<Absence>> GetAbsencesForMonthAsync(int year, int month, CancellationToken ct = default);
    Task UpsertAbsenceAsync(Absence absence, CancellationToken ct = default);
    Task DeleteAbsenceAsync(string absenceId, CancellationToken ct = default);

    Task<IReadOnlyList<StatusOverride>> GetStatusOverridesForMonthAsync(int year, int month, CancellationToken ct = default);
    Task UpsertStatusOverrideAsync(StatusOverride status, CancellationToken ct = default);
    Task DeleteStatusOverrideAsync(string statusOverrideId, CancellationToken ct = default);
}
