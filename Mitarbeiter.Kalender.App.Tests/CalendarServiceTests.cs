using Xunit;
using Mitarbeiter.Kalender.App.Core.Services;
using Xunit;
using Mitarbeiter.Kalender.App.Domain.Entities;
using Xunit;
using Mitarbeiter.Kalender.App.Domain.Enums;
using Xunit;
using Mitarbeiter.Kalender.App.Core.Abstractions;

namespace Mitarbeiter.Kalender.App.Tests;

public sealed class CalendarServiceTests
{
    private sealed class InMemoryRepo : ICalendarRepository
    {
        public List<Employee> Employees { get; } = new() { new Employee("MA1", "M1") };
        public List<Appointment> Appointments { get; } = new();
        public List<RecurrenceRule> Rules { get; } = new();
        public Dictionary<string, List<RecurrenceException>> Exceptions { get; } = new();
        public List<Absence> Absences { get; } = new();
        public List<StatusOverride> Overrides { get; } = new();

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Employee>> GetEmployeesAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Employee>)Employees);
        public Task SaveEmployeesAsync(IEnumerable<Employee> employees, CancellationToken ct = default) { Employees.Clear(); Employees.AddRange(employees); return Task.CompletedTask; }
        public Task<IReadOnlyList<Appointment>> GetAppointmentsForMonthAsync(int year, int month, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Appointment>)Appointments);
        public Task UpsertAppointmentAsync(Appointment appointment, CancellationToken ct = default) { Appointments.RemoveAll(a => a.Id == appointment.Id); Appointments.Add(appointment); return Task.CompletedTask; }
        public Task DeleteAppointmentAsync(string appointmentId, CancellationToken ct = default) { Appointments.RemoveAll(a => a.Id == appointmentId); return Task.CompletedTask; }
        public Task<IReadOnlyList<RecurrenceRule>> GetRecurrenceRulesAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<RecurrenceRule>)Rules);
        public Task UpsertRecurrenceRuleAsync(RecurrenceRule rule, CancellationToken ct = default) { Rules.RemoveAll(r => r.Id == rule.Id); Rules.Add(rule); return Task.CompletedTask; }
        public Task DeleteRecurrenceRuleAsync(string ruleId, CancellationToken ct = default) { Rules.RemoveAll(r => r.Id == ruleId); return Task.CompletedTask; }
        public Task<IReadOnlyList<RecurrenceException>> GetRecurrenceExceptionsAsync(string ruleId, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<RecurrenceException>)(Exceptions.TryGetValue(ruleId, out var l) ? l : new List<RecurrenceException>()));
        public Task UpsertRecurrenceExceptionAsync(RecurrenceException ex, CancellationToken ct = default) { if (!Exceptions.ContainsKey(ex.RecurrenceRuleId)) Exceptions[ex.RecurrenceRuleId] = new(); Exceptions[ex.RecurrenceRuleId].RemoveAll(x => x.Id == ex.Id); Exceptions[ex.RecurrenceRuleId].Add(ex); return Task.CompletedTask; }
        public Task DeleteRecurrenceExceptionAsync(string exceptionId, CancellationToken ct = default) { foreach (var k in Exceptions.Keys) Exceptions[k].RemoveAll(x => x.Id == exceptionId); return Task.CompletedTask; }
        public Task<IReadOnlyList<Absence>> GetAbsencesForMonthAsync(int year, int month, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Absence>)Absences);
        public Task UpsertAbsenceAsync(Absence absence, CancellationToken ct = default) { Absences.RemoveAll(a => a.Id == absence.Id); Absences.Add(absence); return Task.CompletedTask; }
        public Task DeleteAbsenceAsync(string absenceId, CancellationToken ct = default) { Absences.RemoveAll(a => a.Id == absenceId); return Task.CompletedTask; }
        public Task<IReadOnlyList<StatusOverride>> GetStatusOverridesForMonthAsync(int year, int month, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<StatusOverride>)Overrides);
        public Task UpsertStatusOverrideAsync(StatusOverride status, CancellationToken ct = default) { Overrides.RemoveAll(o => o.Id == status.Id); Overrides.Add(status); return Task.CompletedTask; }
        public Task DeleteStatusOverrideAsync(string statusOverrideId, CancellationToken ct = default) { Overrides.RemoveAll(o => o.Id == statusOverrideId); return Task.CompletedTask; }
    }

    [Fact]
    public async Task WeeklyRule_GeneratesOccurrences()
    {
        var repo = new InMemoryRepo();
        repo.Rules.Add(new RecurrenceRule(
            Id: "R1",
            EmployeeId: "MA1",
            Weekday: DayOfWeek.Monday,
            Start: new TimeOnly(8,0),
            End: new TimeOnly(9,0),
            CustomerName: "Kunde",
            IsActive: true,
            IntervalWeeks: 1,
            AnchorDate: new DateOnly(2026,2,2)
        ));

        var svc = new CalendarService(repo);
        var view = await svc.BuildMonthViewAsync(2026, 2);

        Assert.Contains(view.Cells.SelectMany(c => c.Appointments), a => a.CustomerName == "Kunde" && a.Date.Month == 2);
    }
}
