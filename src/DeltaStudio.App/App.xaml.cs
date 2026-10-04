using DeltaStudio.App.Services;
using DeltaStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using DeltaStudio.Infrastructure;
using Microsoft.Extensions.Logging;
using System.IO;

namespace DeltaStudio.App;

/// <summary>
/// WinUI 3 host. The architecture rule in practice: this app owns NO engineering logic —
/// it resolves the same <see cref="DeltaStudioEngine"/> the MCP server uses and binds views to it.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;
    private Window? _window;

    /// <summary>App entry.</summary>
    public App()
    {
        var uiLog = new UiLogService();
        string dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeltaStudio");
        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging((_, lb) =>
            {
                lb.AddUiLogSink(uiLog);
                lb.SetMinimumLevel(LogLevel.Debug);
            })
            .ConfigureServices(services =>
            {
                services.AddDeltaStudioEngine(dataDir);
                services.AddSingleton(uiLog);
                services.AddSingleton<LocalizationService>();
                services.AddSingleton<ThemeService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    /// <summary>Root service provider (set during launch).</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <inheritdoc/>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await _host.StartAsync();
        Services = _host.Services;
        _window = Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }
}
