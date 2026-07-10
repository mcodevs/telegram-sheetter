using Avalonia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiCardSync.App.Services;
using MultiCardSync.App.ViewModels;
using MultiCardSync.Core;
using MultiCardSync.Core.Abstractions;
using MultiCardSync.Core.Options;
using MultiCardSync.Data;
using Serilog;

namespace MultiCardSync.App;

internal static class Program
{
    /// <summary>DI konteyneri (App.axaml.cs va boshqalar shundan xizmat oladi).</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    [STAThread]
    public static void Main(string[] args)
    {
        Services = BuildServices();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // Avalonia designer/preview shuni chaqiradi.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static IServiceProvider BuildServices()
    {
        var baseDir = AppContext.BaseDirectory;

        // Config exe ichidan (embedded) o'qiladi — appsettings.json + appsettings.Secrets.json.
        var config = LoadEmbeddedConfig();

        var mc = config.GetSection("MultiCard").Get<MultiCardOptions>() ?? new MultiCardOptions();
        var gs = config.GetSection("GoogleSheet").Get<GoogleSheetOptions>() ?? new GoogleSheetOptions();
        var sync = config.GetSection("Sync").Get<SyncOptions>() ?? new SyncOptions();

        // creds.json nisbiy yo'l bo'lsa — exe yoniga bog'laymiz (CredentialsJson bo'lsa ishlatilmaydi).
        if (string.IsNullOrWhiteSpace(gs.CredentialsJson) && !Path.IsPathRooted(gs.CredentialsPath))
            gs.CredentialsPath = Path.Combine(baseDir, gs.CredentialsPath);

        var appData = EnsureAppDataDir();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(appData, "logs", "multicardsync-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .WriteTo.Sink(UiLogSink.Instance) // ilova ichidagi "Log" paneli
            .CreateLogger();

        var services = new ServiceCollection();

        services.AddSingleton(mc);
        services.AddSingleton(gs);
        services.AddSingleton(sync);

        services.AddLogging(b => b.AddSerilog(dispose: true));

        services.AddHttpClient<IMultiCardClient, MultiCardClient>(c => c.Timeout = TimeSpan.FromSeconds(60));

        services.AddSingleton<ISheetWriter, GoogleSheetWriter>();
        services.AddSingleton<ISeenStore>(_ => new SeenStore(Path.Combine(appData, "multicardsync.db")));

        services.AddSingleton<SyncService>();
        services.AddSingleton<IStartupRegistrar, WindowsStartupRegistrar>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    /// <summary>appsettings.json + appsettings.Secrets.json ni exe ichidan (embedded) o'qiydi.</summary>
    private static IConfiguration LoadEmbeddedConfig()
    {
        var asm = typeof(Program).Assembly;
        var names = asm.GetManifestResourceNames();
        var streams = new List<Stream>();
        try
        {
            var cb = new ConfigurationBuilder();
            foreach (var suffix in new[] { ".appsettings.json", ".appsettings.Secrets.json" })
            {
                var name = names.FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                if (name is null) continue;
                var stream = asm.GetManifestResourceStream(name);
                if (stream is null) continue;
                streams.Add(stream);
                cb.AddJsonStream(stream);
            }
            cb.AddEnvironmentVariables("MCS_");
            return cb.Build();
        }
        finally
        {
            foreach (var s in streams) s.Dispose();
        }
    }

    private static string EnsureAppDataDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MultiCardSync");
        Directory.CreateDirectory(Path.Combine(dir, "logs"));
        return dir;
    }
}
