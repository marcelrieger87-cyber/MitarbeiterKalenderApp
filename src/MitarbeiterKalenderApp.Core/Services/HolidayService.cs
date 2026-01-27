namespace MitarbeiterKalenderApp.Core.Services;

/// <summary>
/// Minimaler Feiertags-Service (DE-weit, ohne Bundesland-Sonderfälle).
/// Markiert Feiertage + liefert Namen.
/// </summary>
public sealed class HolidayService
{
    public bool IsHoliday(DateOnly date, out string? name)
    {
        // Fixe Feiertage (DE-weit)
        if (date.Month == 1 && date.Day == 1) { name = "Neujahr"; return true; }
        if (date.Month == 5 && date.Day == 1) { name = "Tag der Arbeit"; return true; }
        if (date.Month == 10 && date.Day == 3) { name = "Tag der Deutschen Einheit"; return true; }
        if (date.Month == 12 && date.Day == 25) { name = "1. Weihnachtstag"; return true; }
        if (date.Month == 12 && date.Day == 26) { name = "2. Weihnachtstag"; return true; }

        // Bewegliche Feiertage (Ostern-basiert, DE-weit)
        var easter = EasterSunday(date.Year);
        var goodFriday = easter.AddDays(-2);
        var easterMonday = easter.AddDays(1);
        var ascension = easter.AddDays(39);
        var whitMonday = easter.AddDays(50);

        if (date == goodFriday) { name = "Karfreitag"; return true; }
        if (date == easterMonday) { name = "Ostermontag"; return true; }
        if (date == ascension) { name = "Christi Himmelfahrt"; return true; }
        if (date == whitMonday) { name = "Pfingstmontag"; return true; }

        name = null;
        return false;
    }

    // Gaußsche Osterformel (Gregorianischer Kalender)
    private static DateOnly EasterSunday(int year)
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
        int month = (h + l - 7 * m + 114) / 31;   // 3=March, 4=April
        int day = ((h + l - 7 * m + 114) % 31) + 1;

        return new DateOnly(year, month, day);
    }
}
