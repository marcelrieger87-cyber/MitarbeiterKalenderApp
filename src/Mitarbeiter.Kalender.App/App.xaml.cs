using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mitarbeiter.Kalender.App.Core.Abstractions;
using Mitarbeiter.Kalender.App.Core.Services;
using Mitarbeiter.Kalender.App.Infrastructure.Sqlite;
using Mitarbeiter.Kalender.App.ViewModels;

namespace Mitarbeiter.Kalender.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<ICalendarRepository>(_ => new SqliteCalendarRepository(SqlitePaths.GetDefaultDbPath()));
                services.AddSingleton<ICalendarService, CalendarService>();

                services.AddSingleton<MainViewModel>();
            })
            .Build();

        _host.Start();

        // StartupUri instantiates window; we set DataContext afterwards
        this.Startup += (_, _) =>
        {
            if (Current.MainWindow is not null)
                Current.MainWindow.DataContext = _host!.Services.GetRequiredService<MainViewModel>();
        };
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
