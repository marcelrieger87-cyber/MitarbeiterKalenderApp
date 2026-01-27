using System.Text;
using System.Windows;
using MitarbeiterKalenderApp.Core.Domain;
using MitarbeiterKalenderApp.Core.Services;

namespace MitarbeiterKalenderApp.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        OutputBox.Text = "Bereit. Klicke auf 'Render Testmonat'.";
    }

    private void OnRenderTestClick(object sender, RoutedEventArgs e)
    {
        // --- Testdaten (später ersetzt durch DB/Datei) ---
        var employeeId = Guid.NewGuid();

        var appointments = new List<Appointment>
        {
            new()
            {
                EmployeeId = employeeId,
                Date = new DateOnly(2026, 1, 6),
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(10, 30),
                Title = "Einzeltermin: Kunde A"
            },
            new()
            {
                EmployeeId = employeeId,
                Date = new DateOnly(2026, 1, 6),
                StartTime = new TimeOnly(14, 0),
                EndTime = new TimeOnly(15, 0),
                Title = "Einzeltermin: Kunde B"
            }
        };

        var ruleId = Guid.NewGuid();

        var rules = new List<RecurrenceRule>
        {
            new()
            {
                Id = ruleId,
                EmployeeId = employeeId,
                StartDate = new DateOnly(2026, 1, 1),
                UntilDate = new DateOnly(2026, 1, 31),
                Frequency = RecurrenceFrequency.Weekly,
                Interval = 1,
                ByWeekDays = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday },
                StartTime = new TimeOnly(11, 0),
                EndTime = new TimeOnly(12, 0),
                Title = "Serie: Weekly Check"
            }
        };

        var exceptions = new List<RecurrenceException>
        {
            new()
            {
                RecurrenceRuleId = ruleId,
                Date = new DateOnly(2026, 1, 7), // Mittwoch
                IsCanceled = true // fällt aus
            },
            new()
            {
                RecurrenceRuleId = ruleId,
                Date = new DateOnly(2026, 1, 12), // Montag
                OverrideStartTime = new TimeOnly(13, 0),
                OverrideEndTime = new TimeOnly(14, 0),
                OverrideTitle = "Serie (verschoben): Weekly Check"
            }
        };

        var absences = new List<Absence>
        {
            new()
            {
                EmployeeId = employeeId,
                Date = new DateOnly(2026, 1, 15),
                Type = AbsenceType.Vacation,
                Note = "Urlaub"
            }
        };

        var statuses = new List<StatusOverride>
        {
            new()
            {
                EmployeeId = employeeId,
                Date = new DateOnly(2026, 1, 20),
                Status = DayStatus.HomeOffice,
                Note = "HO"
            }
        };

        // --- Render ---
        var renderer = new CalendarRenderService();
        var month = renderer.RenderMonth(
            employeeId: employeeId,
            year: 2026,
            month: 1,
            appointments: appointments,
            rules: rules,
            exceptions: exceptions,
            absences: absences,
            statuses: statuses);

        // --- Ausgabe ---
        var sb = new StringBuilder();
        sb.AppendLine($"Rendered: {month.Year}-{month.Month:00} | Slots: {month.DayStart:HH\\:mm}-{month.DayEnd:HH\\:mm} / {month.SlotMinutes}min");
        sb.AppendLine(new string('-', 90));

        foreach (var day in month.Days)
        {
            var header = $"{day.Date:yyyy-MM-dd} ({day.Date.DayOfWeek})";
            var meta = "";

            if (day.Absences.Count > 0)
                meta += $" | Abwesenheit: {string.Join(", ", day.Absences.Select(a => $"{a.Type}"))}";
            if (day.StatusOverrides.Count > 0)
                meta += $" | Status: {string.Join(", ", day.StatusOverrides.Select(s => $"{s.Status}"))}";

            sb.AppendLine(header + meta);

            if (day.Appointments.Count == 0)
            {
                sb.AppendLine("  (keine Termine)");
            }
            else
            {
                foreach (var a in day.Appointments)
                {
                    var src = a.Source == RenderSource.SingleAppointment ? "Single" : "Serie";
                    sb.AppendLine($"  [{src}] {a.StartTime:HH\\:mm}-{a.EndTime:HH\\:mm}  {a.Title}");
                }
            }

            sb.AppendLine();
        }

        OutputBox.Text = sb.ToString();
    }
}
