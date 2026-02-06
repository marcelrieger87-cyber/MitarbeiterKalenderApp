using Microsoft.Data.Sqlite;
using Mitarbeiter.Kalender.App.Core.Abstractions;
using Mitarbeiter.Kalender.App.Domain.Entities;
using Mitarbeiter.Kalender.App.Domain.Enums;

namespace Mitarbeiter.Kalender.App.Infrastructure.Sqlite;

public sealed class SqliteCalendarRepository : ICalendarRepository
{
    private readonly string _connectionString;

    public SqliteCalendarRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Employees (
    Id TEXT PRIMARY KEY,
    DisplayName TEXT NOT NULL,
    IsActive INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS Appointments (
    Id TEXT PRIMARY KEY,
    EmployeeId TEXT NOT NULL,
    Date TEXT NOT NULL,
    Start TEXT NOT NULL,
    End TEXT NOT NULL,
    CustomerName TEXT NOT NULL,
    Status INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS Absences (
    Id TEXT PRIMARY KEY,
    EmployeeId TEXT NOT NULL,
    Date TEXT NOT NULL,
    Type INTEGER NOT NULL,
    Note TEXT
);
";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // =======================
    // Employees
    // =======================

    public async Task<IReadOnlyList<Employee>> GetEmployeesAsync(CancellationToken ct = default)
    {
        var list = new List<Employee>();

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, DisplayName, IsActive FROM Employees";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new Employee(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2) == 1
            ));
        }

        return list;
    }

    public async Task SaveEmployeesAsync(IEnumerable<Employee> employees, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var tx = conn.BeginTransaction();

        var clear = conn.CreateCommand();
        clear.CommandText = "DELETE FROM Employees";
        clear.Transaction = tx;
        await clear.ExecuteNonQueryAsync(ct);

        foreach (var e in employees)
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO Employees (Id, DisplayName, IsActive)
VALUES ($id, $name, $active)";
            cmd.Parameters.AddWithValue("$id", e.Id);
            cmd.Parameters.AddWithValue("$name", e.DisplayName);
            cmd.Parameters.AddWithValue("$active", e.IsActive ? 1 : 0);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        tx.Commit();
    }

    // =======================
    // Appointments
    // =======================

    public async Task<IReadOnlyList<Appointment>> GetAppointmentsForMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var list = new List<Appointment>();

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var from = new DateOnly(year, month, 1).ToString("yyyy-MM-dd");
        var to = new DateOnly(year, month, DateTime.DaysInMonth(year, month)).ToString("yyyy-MM-dd");

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, EmployeeId, Date, Start, End, CustomerName, Status
FROM Appointments
WHERE Date BETWEEN $from AND $to";
        cmd.Parameters.AddWithValue("$from", from);
        cmd.Parameters.AddWithValue("$to", to);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new Appointment(
                reader.GetString(0),
                reader.GetString(1),
                DateOnly.Parse(reader.GetString(2)),
                TimeOnly.Parse(reader.GetString(3)),
                TimeOnly.Parse(reader.GetString(4)),
                reader.GetString(5),
                (AppointmentStatus)reader.GetInt32(6)
            ));
        }

        return list;
    }

    public async Task UpsertAppointmentAsync(Appointment appointment, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT OR REPLACE INTO Appointments
(Id, EmployeeId, Date, Start, End, CustomerName, Status)
VALUES ($id, $emp, $date, $start, $end, $cust, $status)";
        cmd.Parameters.AddWithValue("$id", appointment.Id);
        cmd.Parameters.AddWithValue("$emp", appointment.EmployeeId);
        cmd.Parameters.AddWithValue("$date", appointment.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$start", appointment.Start.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$end", appointment.End.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$cust", appointment.CustomerName);
        cmd.Parameters.AddWithValue("$status", (int)appointment.Status);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAppointmentAsync(string appointmentId, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Appointments WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", appointmentId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // =======================
    // Absences
    // =======================

    public async Task<IReadOnlyList<Absence>> GetAbsencesForMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var list = new List<Absence>();

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var from = new DateOnly(year, month, 1).ToString("yyyy-MM-dd");
        var to = new DateOnly(year, month, DateTime.DaysInMonth(year, month)).ToString("yyyy-MM-dd");

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, EmployeeId, Date, Type, Note
FROM Absences
WHERE Date BETWEEN $from AND $to";
        cmd.Parameters.AddWithValue("$from", from);
        cmd.Parameters.AddWithValue("$to", to);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new Absence(
                reader.GetString(0),
                reader.GetString(1),
                DateOnly.Parse(reader.GetString(2)),
                (AbsenceType)reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)
            ));
        }

        return list;
    }

    public async Task UpsertAbsenceAsync(Absence absence, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT OR REPLACE INTO Absences
(Id, EmployeeId, Date, Type, Note)
VALUES ($id, $emp, $date, $type, $note)";
        cmd.Parameters.AddWithValue("$id", absence.Id);
        cmd.Parameters.AddWithValue("$emp", absence.EmployeeId);
        cmd.Parameters.AddWithValue("$date", absence.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$type", (int)absence.Type);
        cmd.Parameters.AddWithValue("$note", absence.Note ?? "");

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAbsenceAsync(string absenceId, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Absences WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", absenceId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // =======================
    // Not used yet (Series / Status Overrides)
    // =======================

    public Task<IReadOnlyList<RecurrenceRule>> GetRecurrenceRulesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RecurrenceRule>>(Array.Empty<RecurrenceRule>());

    public Task UpsertRecurrenceRuleAsync(RecurrenceRule rule, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteRecurrenceRuleAsync(string ruleId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<RecurrenceException>> GetRecurrenceExceptionsAsync(string ruleId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RecurrenceException>>(Array.Empty<RecurrenceException>());

    public Task UpsertRecurrenceExceptionAsync(RecurrenceException ex, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteRecurrenceExceptionAsync(string exceptionId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<StatusOverride>> GetStatusOverridesForMonthAsync(int year, int month, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StatusOverride>>(Array.Empty<StatusOverride>());

    public Task UpsertStatusOverrideAsync(StatusOverride status, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteStatusOverrideAsync(string statusOverrideId, CancellationToken ct = default)
        => Task.CompletedTask;
}
