using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using MultiCardSync.App.ViewModels;
using MultiCardSync.App.Views;

namespace MultiCardSync.App;

public partial class App : Application
{
    private Window? _mainWindow;
    private TrayIcon? _tray;
    private bool _reallyExit;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Oyna yopilganda ilova CHIQMAYDI — tray'da fonda ishlab turadi.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var vm = Program.Services.GetRequiredService<MainWindowViewModel>();
            var icon = LoadIcon();

            _mainWindow = new MainWindow { DataContext = vm };
            if (icon is not null) _mainWindow.Icon = icon;
            _mainWindow.Closing += OnMainWindowClosing;

            SetupTray(desktop, vm, icon);

            // Startup (--tray) da yashirin ishga tushadi; qo'lda ochilganda oyna ko'rinadi.
            var startHidden = desktop.Args is { } args && Array.Exists(args, s => s == "--tray");
            if (!startHidden)
                _mainWindow.Show();

            _ = vm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            return new WindowIcon(AssetLoader.Open(new Uri("avares://MultiCardSync/Assets/tray.png")));
        }
        catch
        {
            return null;
        }
    }

    private void SetupTray(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel vm, WindowIcon? icon)
    {
        var menu = new NativeMenu();

        var showItem = new NativeMenuItem("Ochish");
        showItem.Click += (_, _) => ShowMainWindow();

        var syncItem = new NativeMenuItem("Hozir sinxronla");
        syncItem.Click += (_, _) => vm.SyncNowCommand.Execute(null);

        var exitItem = new NativeMenuItem("Chiqish");
        exitItem.Click += (_, _) =>
        {
            _reallyExit = true;
            desktop.Shutdown();
        };

        menu.Items.Add(showItem);
        menu.Items.Add(syncItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        _tray = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "MultiCard → Sheet Sync",
            Menu = menu,
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_reallyExit) return;
        // Yopish o'rniga tray'ga yashiramiz.
        e.Cancel = true;
        _mainWindow?.Hide();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }
}
