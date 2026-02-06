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

-- =======================
-- Series (Recurrence)
-- =======================

CREATE TABLE IF NOT EXISTS RecurrenceRules (
    Id TEXT PRIMARY KEY,
    EmployeeId TEXT NOT NULL,
    Weekday INTEGER NOT NULL,      -- 0=Sunday..6=Saturday (DayOfWeek)
    Start TEXT NOT NULL,           -- HH:mm
    End TEXT NOT NULL,             -- HH:mm
    CustomerName TEXT NOT NULL,
    IsActive INTEGER NOT NULL,     -- 0/1
    IntervalWeeks INTEGER NOT NULL,
    AnchorDate TEXT NOT NULL       -- yyyy-MM-dd
);

CREATE TABLE IF NOT EXISTS RecurrenceExceptions (
    Id TEXT PRIMARY KEY,
    RecurrenceRuleId TEXT NOT NULL,
    EmployeeId TEXT NOT NULL,
    Date TEXT NOT NULL,            -- yyyy-MM-dd
    Start TEXT NOT NULL,           -- HH:mm
    End TEXT NOT NULL,             -- HH:mm
    CustomerName TEXT NOT NULL
);

-- =======================
-- Status Overrides
-- =======================

CREATE TABLE IF NOT EXISTS StatusOverrides (
    Id TEXT PRIMARY KEY,
    EmployeeId TEXT NOT NULL,
    Date TEXT NOT NULL,            -- yyyy-MM-dd
    Start TEXT NOT NULL,           -- HH:mm
    End TEXT NOT NULL,             -- HH:mm
    CustomerName TEXT NOT NULL,
    Status INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_Appointments_Date ON Appointments(Date);
CREATE INDEX IF NOT EXISTS IX_Absences_Date ON Absences(Date);

CREATE INDEX IF NOT EXISTS IX_RecurrenceRules_Employee ON RecurrenceRules(EmployeeId);
CREATE INDEX IF NOT EXISTS IX_RecurrenceExceptions_Rule ON RecurrenceExceptions(RecurrenceRuleId);
CREATE INDEX IF NOT EXISTS IX_StatusOverrides_Date ON StatusOverrides(Date);
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
    // Series / Recurrence Rules
    // =======================

    public async Task<IReadOnlyList<RecurrenceRule>> GetRecurrenceRulesAsync(CancellationToken ct = default)
    {
        var list = new List<RecurrenceRule>();

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, EmployeeId, Weekday, Start, End, CustomerName, IsActive, IntervalWeeks, AnchorDate
FROM RecurrenceRules";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new RecurrenceRule(
                Id: reader.GetString(0),
                EmployeeId: reader.GetString(1),
                Weekday: (DayOfWeek)reader.GetInt32(2),
                Start: TimeOnly.Parse(reader.GetString(3)),
                End: TimeOnly.Parse(reader.GetString(4)),
                CustomerName: reader.GetString(5),
                IsActive: reader.GetInt32(6) == 1,
                IntervalWeeks: reader.GetInt32(7),
                AnchorDate: DateOnly.Parse(reader.GetString(8))
            ));
        }

        return list;
    }

    public async Task UpsertRecurrenceRuleAsync(RecurrenceRule rule, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT OR REPLACE INTO RecurrenceRules
(Id, EmployeeId, Weekday, Start, End, CustomerName, IsActive, IntervalWeeks, AnchorDate)
VALUES ($id, $emp, $wd, $start, $end, $cust, $active, $interval, $anchor)";
        cmd.Parameters.AddWithValue("$id", rule.Id);
        cmd.Parameters.AddWithValue("$emp", rule.EmployeeId);
        cmd.Parameters.AddWithValue("$wd", (int)rule.Weekday);
        cmd.Parameters.AddWithValue("$start", rule.Start.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$end", rule.End.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$cust", rule.CustomerName);
        cmd.Parameters.AddWithValue("$active", rule.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$interval", Math.Max(1, rule.IntervalWeeks));
        cmd.Parameters.AddWithValue("$anchor", rule.AnchorDate.ToString("yyyy-MM-dd"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteRecurrenceRuleAsync(string ruleId, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var tx = conn.BeginTransaction();

        // Delete dependent exceptions first
        var delEx = conn.CreateCommand();
        delEx.Transaction = tx;
        delEx.CommandText = "DELETE FROM RecurrenceExceptions WHERE RecurrenceRuleId = $rid";
        delEx.Parameters.AddWithValue("$rid", ruleId);
        await delEx.ExecuteNonQueryAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM RecurrenceRules WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", ruleId);
        await cmd.ExecuteNonQueryAsync(ct);

        tx.Commit();
    }

    // =======================
    // Series / Recurrence Exceptions
    // =======================

    public async Task<IReadOnlyList<RecurrenceException>> GetRecurrenceExceptionsAsync(string ruleId, CancellationToken ct = default)
    {
        var list = new List<RecurrenceException>();

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, RecurrenceRuleId, EmployeeId, Date, Start, End, CustomerName
FROM RecurrenceExceptions
WHERE RecurrenceRuleId = $rid";
        cmd.Parameters.AddWithValue("$rid", ruleId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new RecurrenceException(
                Id: reader.GetString(0),
                RecurrenceRuleId: reader.GetString(1),
                EmployeeId: reader.GetString(2),
                Date: DateOnly.Parse(reader.GetString(3)),
                Start: TimeOnly.Parse(reader.GetString(4)),
                End: TimeOnly.Parse(reader.GetString(5)),
                CustomerName: reader.GetString(6)
            ));
        }

        return list;
    }

    public async Task UpsertRecurrenceExceptionAsync(RecurrenceException ex, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT OR REPLACE INTO RecurrenceExceptions
(Id, RecurrenceRuleId, EmployeeId, Date, Start, End, CustomerName)
VALUES ($id, $rid, $emp, $date, $start, $end, $cust)";
        cmd.Parameters.AddWithValue("$id", ex.Id);
        cmd.Parameters.AddWithValue("$rid", ex.RecurrenceRuleId);
        cmd.Parameters.AddWithValue("$emp", ex.EmployeeId);
        cmd.Parameters.AddWithValue("$date", ex.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$start", ex.Start.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$end", ex.End.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$cust", ex.CustomerName);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteRecurrenceExceptionAsync(string exceptionId, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM RecurrenceExceptions WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", exceptionId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // =======================
    // Status Overrides
    // =======================

    public async Task<IReadOnlyList<StatusOverride>> GetStatusOverridesForMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var list = new List<StatusOverride>();

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var from = new DateOnly(year, month, 1).ToString("yyyy-MM-dd");
        var to = new DateOnly(year, month, DateTime.DaysInMonth(year, month)).ToString("yyyy-MM-dd");

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, EmployeeId, Date, Start, End, CustomerName, Status
FROM StatusOverrides
WHERE Date BETWEEN $from AND $to";
        cmd.Parameters.AddWithValue("$from", from);
        cmd.Parameters.AddWithValue("$to", to);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new StatusOverride(
                Id: reader.GetString(0),
                EmployeeId: reader.GetString(1),
                Date: DateOnly.Parse(reader.GetString(2)),
                Start: TimeOnly.Parse(reader.GetString(3)),
                End: TimeOnly.Parse(reader.GetString(4)),
                CustomerName: reader.GetString(5),
                Status: (AppointmentStatus)reader.GetInt32(6)
            ));
        }

        return list;
    }

    public async Task UpsertStatusOverrideAsync(StatusOverride status, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT OR REPLACE INTO StatusOverrides
(Id, EmployeeId, Date, Start, End, CustomerName, Status)
VALUES ($id, $emp, $date, $start, $end, $cust, $status)";
        cmd.Parameters.AddWithValue("$id", status.Id);
        cmd.Parameters.AddWithValue("$emp", status.EmployeeId);
        cmd.Parameters.AddWithValue("$date", status.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$start", status.Start.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$end", status.End.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$cust", status.CustomerName);
        cmd.Parameters.AddWithValue("$status", (int)status.Status);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteStatusOverrideAsync(string statusOverrideId, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM StatusOverrides WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", statusOverrideId);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
