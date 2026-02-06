namespace Mitarbeiter.Kalender.App.Infrastructure.Sqlite;

public static class SqlitePaths
{
    public static string GetDefaultDbPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MitarbeiterKalender");

        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "calendar.db");
    }
}
