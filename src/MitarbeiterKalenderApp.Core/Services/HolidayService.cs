namespace MitarbeiterKalenderApp.Core.Services;

/// <summary>
/// Einfache Feiertage (DE-weit + bewegliche um Ostern).
/// Hinweis: Bundesland-spezifische Feiertage sind hier NICHT getrennt.
/// </summary>
public sealed class HolidayService
{
    public HashSet<DateOnly> GetHolidays(int year)
    {
        var h = new HashSet<DateOnly>();

        // Fixe Feiertage (bundesweit)
        h.Add(new DateOnly(year, 1, 1));   // Neujahr
        h.Add(new DateOnly(year, 5, 1));   // Tag der Arbeit
        h.Add(new DateOnly(year, 10, 3));  // Tag der Deutschen Einheit
        h.Add(new DateOnly(year, 12, 25)); // 1. Weihnachtstag
        h.Add(new DateOnly(year, 12, 26)); // 2. Weihnachtstag

        // Bewegliche (Ostern)
        var easter = GetEasterSunday(year);
        h.Add(easter.AddDays(-2));  // Karfreitag
        h.Add(easter.AddDays(1));   // Ostermontag
        h.Add(easter.AddDays(39));  // Christi Himmelfahrt
        h.Add(easter.AddDays(50));  // Pfingstmontag

        return h;
    }

    // Gaußsche Osterformel (gregorianisch)
    private static DateOnly GetEasterSunday(int year)
    {
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;         // 3=March, 4=April
        int day = ((h + l - 7 * m + 114) % 31) + 1;

        return new DateOnly(year, month, day);
    }
}
