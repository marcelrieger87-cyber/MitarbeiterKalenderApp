using Microsoft.Data.Sqlite;
using Mitarbeiter.Kalender.App.Core.Abstractions;
using Mitarbeiter.Kalender.App.Domain.Entities;
using Mitarbeiter.Kalender.App.Domain.Enums;

namespace Mitarbeiter.Kalender.App.Infrastructure.Sqlite;

public sealed class SqliteCalendarRepository : ICalendarRepository
{
    private readonly string _dbPath;

    public SqliteCalendarRepository(string? dbPath = null)
    {
        _dbPath = string.IsNullOrWhiteSpace(dbPath) ? SqlitePaths.GetDefaultDbPath() : dbPath;
    }

    private SqliteConnection Open()
    {
        var con = new SqliteConnection($"Data Source={_dbPath}");
        con.Open();
        return con;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath) ?? ".");

        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = @"
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS employees(
  id TEXT PRIMARY KEY,
  display_name TEXT NOT NULL,
  is_active INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS appointments(
  id TEXT PRIMARY KEY,
  employee_id TEXT NOT NULL,
  date TEXT NOT NULL,
  start TEXT NOT NULL,
  end TEXT NOT NULL,
  customer_name TEXT NOT NULL,
  status INTEGER NOT NULL,
  is_from_recurrence INTEGER NOT NULL,
  recurrence_rule_id TEXT NULL,
  FOREIGN KEY(employee_id) REFERENCES employees(id)
);

CREATE TABLE IF NOT EXISTS recurrence_rules(
  id TEXT PRIMARY KEY,
  employee_id TEXT NOT NULL,
  weekday INTEGER NOT NULL,
  start TEXT NOT NULL,
  end TEXT NOT NULL,
  customer_name TEXT NOT NULL,
  is_active INTEGER NOT NULL,
  interval_weeks INTEGER NOT NULL,
  anchor_date TEXT NOT NULL,
  FOREIGN KEY(employee_id) REFERENCES employees(id)
);

CREATE TABLE IF NOT EXISTS recurrence_exceptions(
  id TEXT PRIMARY KEY,
  rule_id TEXT NOT NULL,
  employee_id TEXT NOT NULL,
  date TEXT NOT NULL,
  start TEXT NOT NULL,
  end TEXT NOT NULL,
  customer_name TEXT NOT NULL,
  FOREIGN KEY(rule_id) REFERENCES recurrence_rules(id) ON DELETE CASCADE,
  FOREIGN KEY(employee_id) REFERENCES employees(id)
);

CREATE TABLE IF NOT EXISTS absences(
  id TEXT PRIMARY KEY,
  employee_id TEXT NOT NULL,
  date TEXT NOT NULL,
  type INTEGER NOT NULL,
  note TEXT NULL,
  FOREIGN KEY(employee_id) REFERENCES employees(id)
);

CREATE TABLE IF NOT EXISTS status_overrides(
  id TEXT PRIMARY KEY,
  employee_id TEXT NOT NULL,
  date TEXT NOT NULL,
  start TEXT NOT NULL,
  end TEXT NOT NULL,
  customer_name TEXT NOT NULL,
  status INTEGER NOT NULL,
  FOREIGN KEY(employee_id) REFERENCES employees(id)
);
";
        await cmd.ExecuteNonQueryAsync(ct);

        // Seed minimal employees if empty
        if ((await GetEmployeesAsync(ct)).Count == 0)
        {
            var seed = new[]
            {
                new Employee("MA1", "Mitarbeiter 1"),
                new Employee("MA2", "Mitarbeiter 2"),
                new Employee("MA3", "Mitarbeiter 3"),
            };
            await SaveEmployeesAsync(seed, ct);
        }
    }

    public async Task<IReadOnlyList<Employee>> GetEmployeesAsync(CancellationToken ct = default)
    {
        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT id, display_name, is_active FROM employees ORDER BY display_name";

        var list = new List<Employee>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new Employee(
                Id: r.GetString(0),
                DisplayName: r.GetString(1),
                IsActive: r.GetInt32(2) == 1
            ));
        }
        return list;
    }

    public async Task SaveEmployeesAsync(IEnumerable<Employee> employees, CancellationToken ct = default)
    {
        await using var con = Open();
        await using var tx = await con.BeginTransactionAsync(ct);
        foreach (var e in employees)
        {
            await using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO employees(id, display_name, is_active)
VALUES($id,$name,$active)
ON CONFLICT(id) DO UPDATE SET display_name=$name, is_active=$active";
            cmd.Parameters.AddWithValue("$id", e.Id);
            cmd.Parameters.AddWithValue("$name", e.DisplayName);
            cmd.Parameters.AddWithValue("$active", e.IsActive ? 1 : 0);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<Appointment>> GetAppointmentsForMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT id, employee_id, date, start, end, customer_name, status, is_from_recurrence, recurrence_rule_id
FROM appointments
WHERE date BETWEEN $start AND $end";
        cmd.Parameters.AddWithValue("$start", start.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$end", end.ToString("yyyy-MM-dd"));

        var list = new List<Appointment>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new Appointment(
                Id: r.GetString(0),
                EmployeeId: r.GetString(1),
                Date: DateOnly.Parse(r.GetString(2)),
                Start: TimeOnly.Parse(r.GetString(3)),
                End: TimeOnly.Parse(r.GetString(4)),
                CustomerName: r.GetString(5),
                Status: (AppointmentStatus)r.GetInt32(6),
                IsFromRecurrence: r.GetInt32(7) == 1,
                RecurrenceRuleId: r.IsDBNull(8) ? null : r.GetString(8)
            ));
        }
        return list;
    }

    public async Task UpsertAppointmentAsync(Appointment appointment, CancellationToken ct = default)
    {
        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO appointments(id, employee_id, date, start, end, customer_name, status, is_from_recurrence, recurrence_rule_id)
VALUES($id,$eid,$date,$start,$end,$cust,$status,$ifr,$rid)
ON CONFLICT(id) DO UPDATE SET
 employee_id=$eid,
 date=$date,
 start=$start,
 end=$end,
 customer_name=$cust,
 status=$status,
 is_from_recurrence=$ifr,
 recurrence_rule_id=$rid";
        BindAppointment(cmd, appointment);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAppointmentAsync(string appointmentId, CancellationToken ct = default)
    {
        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM appointments WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", appointmentId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<RecurrenceRule>> GetRecurrenceRulesAsync(CancellationToken ct = default)
    {
        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT id, employee_id, weekday, start, end, customer_name, is_active, interval_weeks, anchor_date
FROM recurrence_rules";

        var list = new List<RecurrenceRule>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new RecurrenceRule(
                Id: r.GetString(0),
                EmployeeId: r.GetString(1),
                Weekday: (DayOfWeek)r.GetInt32(2),
                Start: TimeOnly.Parse(r.GetString(3)),
                End: TimeOnly.Parse(r.GetString(4)),
                CustomerName: r.GetString(5),
                IsActive: r.GetInt32(6) == 1,
                IntervalWeeks: Math.Max(1, r.GetInt32(7)),
                AnchorDate: DateOnly.Parse(r.GetString(8))
            ));
        }
        return list;
    }

    public async Task UpsertRecurrenceRuleAsync(RecurrenceRule rule, CancellationToken ct = default)
    {
        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO recurrence_rules(id, employee_id, weekday, start, end, customer_name, is_active, interval_weeks, anchor_date)
VALUES($id,$eid,$wd,$start,$end,$cust,$active,$int,$anchor)
ON CONFLICT(id) DO UPDATE SET
 employee_id=$eid,
 weekday=$wd,
 start=$start,
 end=$end,
 customer_name=$cust,
 is_active=$active,
 interval_weeks=$int,
 anchor_date=$anchor";

        cmd.Parameters.AddWithValue("$id", rule.Id);
        cmd.Parameters.AddWithValue("$eid", rule.EmployeeId);
        cmd.Parameters.AddWithValue("$wd", (int)rule.Weekday);
        cmd.Parameters.AddWithValue("$start", rule.Start.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$end", rule.End.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$cust", rule.CustomerName);
        cmd.Parameters.AddWithValue("$active", rule.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$int", Math.Max(1, rule.IntervalWeeks));
        cmd.Parameters.AddWithValue("$anchor", rule.AnchorDate.ToString("yyyy-MM-dd"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteRecurrenceRuleAsync(string ruleId, CancellationToken ct = default)
    {
        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM recurrence_rules WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", ruleId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<RecurrenceException>> GetRecurrenceExceptionsAsync(string ruleId, CancellationToken ct = default)
    {
        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT id, rule_id, employee_id, date, start, end, customer_name
FROM recurrence_exceptions WHERE rule_id=$id";
        cmd.Parameters.AddWithValue("$id", ruleId);

        var list = new List<RecurrenceException>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new RecurrenceException(
                Id: r.GetString(0),
                RecurrenceRuleId: r.GetString(1),
                EmployeeId: r.GetString(2),
                Date: DateOnly.Parse(r.GetString(3)),
                Start: TimeOnly.Parse(r.GetString(4)),
                End: TimeOnly.Parse(r.GetString(5)),
                CustomerName: r.GetString(6)
            ));
        }
        return list;
    }

    public async Task UpsertRecurrenceExceptionAsync(RecurrenceException ex, CancellationToken ct = default)
    {
        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO recurrence_exceptions(id, rule_id, employee_id, date, start, end, customer_name)
VALUES($id,$rid,$eid,$date,$start,$end,$cust)
ON CONFLICT(id) DO UPDATE SET
 rule_id=$rid,
 employee_id=$eid,
 date=$date,
 start=$start,
 end=$end,
 customer_name=$cust";

        cmd.Parameters.AddWithValue("$id", ex.Id);
        cmd.Parameters.AddWithValue("$rid", ex.RecurrenceRuleId);
        cmd.Parameters.AddWithValue("$eid", ex.EmployeeId);
        cmd.Parameters.AddWithValue("$date", ex.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$start", ex.Start.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$end", ex.End.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$cust", ex.CustomerName);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteRecurrenceExceptionAsync(string exceptionId, CancellationToken ct = default)
    {
        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM recurrence_exceptions WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", exceptionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<Absence>> GetAbsencesForMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT id, employee_id, date, type, note FROM absences WHERE date BETWEEN $start AND $end";
        cmd.Parameters.AddWithValue("$start", start.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$end", end.ToString("yyyy-MM-dd"));

        var list = new List<Absence>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new Absence(
                Id: r.GetString(0),
                EmployeeId: r.GetString(1),
                Date: DateOnly.Parse(r.GetString(2)),
                Type: (AbsenceType)r.GetInt32(3),
                Note: r.IsDBNull(4) ? null : r.GetString(4)
            ));
        }
        return list;
    }

    public async Task UpsertAbsenceAsync(Absence absence, CancellationToken ct = default)
    {
        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO absences(id, employee_id, date, type, note)
VALUES($id,$eid,$date,$type,$note)
ON CONFLICT(id) DO UPDATE SET
 employee_id=$eid,
 date=$date,
 type=$type,
 note=$note";

        cmd.Parameters.AddWithValue("$id", absence.Id);
        cmd.Parameters.AddWithValue("$eid", absence.EmployeeId);
        cmd.Parameters.AddWithValue("$date", absence.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$type", (int)absence.Type);
        cmd.Parameters.AddWithValue("$note", (object?)absence.Note ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAbsenceAsync(string absenceId, CancellationToken ct = default)
    {
        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM absences WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", absenceId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<StatusOverride>> GetStatusOverridesForMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT id, employee_id, date, start, end, customer_name, status
FROM status_overrides WHERE date BETWEEN $start AND $end";
        cmd.Parameters.AddWithValue("$start", start.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$end", end.ToString("yyyy-MM-dd"));

        var list = new List<StatusOverride>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new StatusOverride(
                Id: r.GetString(0),
                EmployeeId: r.GetString(1),
                Date: DateOnly.Parse(r.GetString(2)),
                Start: TimeOnly.Parse(r.GetString(3)),
                End: TimeOnly.Parse(r.GetString(4)),
                CustomerName: r.GetString(5),
                Status: (AppointmentStatus)r.GetInt32(6)
            ));
        }
        return list;
    }

    public async Task UpsertStatusOverrideAsync(StatusOverride status, CancellationToken ct = default)
    {
        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO status_overrides(id, employee_id, date, start, end, customer_name, status)
VALUES($id,$eid,$date,$start,$end,$cust,$status)
ON CONFLICT(id) DO UPDATE SET
 employee_id=$eid,
 date=$date,
 start=$start,
 end=$end,
 customer_name=$cust,
 status=$status";

        cmd.Parameters.AddWithValue("$id", status.Id);
        cmd.Parameters.AddWithValue("$eid", status.EmployeeId);
        cmd.Parameters.AddWithValue("$date", status.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$start", status.Start.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$end", status.End.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$cust", status.CustomerName);
        cmd.Parameters.AddWithValue("$status", (int)status.Status);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteStatusOverrideAsync(string statusOverrideId, CancellationToken ct = default)
    {
        await using var con = Open();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM status_overrides WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", statusOverrideId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void BindAppointment(SqliteCommand cmd, Appointment appointment)
    {
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$id", appointment.Id);
        cmd.Parameters.AddWithValue("$eid", appointment.EmployeeId);
        cmd.Parameters.AddWithValue("$date", appointment.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$start", appointment.Start.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$end", appointment.End.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("$cust", appointment.CustomerName);
        cmd.Parameters.AddWithValue("$status", (int)appointment.Status);
        cmd.Parameters.AddWithValue("$ifr", appointment.IsFromRecurrence ? 1 : 0);
        cmd.Parameters.AddWithValue("$rid", (object?)appointment.RecurrenceRuleId ?? DBNull.Value);
    }
}
