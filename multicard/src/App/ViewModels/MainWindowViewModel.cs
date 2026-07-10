using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MultiCardSync.App.Services;
using MultiCardSync.Core;
using MultiCardSync.Core.Abstractions;
using MultiCardSync.Core.Models;
using MultiCardSync.Core.Options;

namespace MultiCardSync.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private static readonly TimeSpan UzOffset = TimeSpan.FromHours(5);

    private readonly SyncService _sync;
    private readonly ISeenStore _store;
    private readonly SyncOptions _opts;
    private readonly GoogleSheetOptions _sheet;
    private readonly IStartupRegistrar _startup;
    private readonly ILogger<MainWindowViewModel> _log;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _statusText = "Ishga tushmoqda…";
    [ObservableProperty] private IBrush _statusBrush = Brushes.Gray;
    [ObservableProperty] private string _lastSyncText = "—";
    [ObservableProperty] private int _totalSynced;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleButtonText))]
    private bool _isRunning;

    [ObservableProperty] private bool _startupRegistered;
    [ObservableProperty] private bool _startupSupported;
    [ObservableProperty] private string _pollIntervalText = "";

    public string ToggleButtonText => IsRunning ? "⏸ To'xtatish" : "▶ Boshlash";

    public ObservableCollection<SyncRowViewModel> Transactions { get; } = new();

    public MainWindowViewModel(
        SyncService sync,
        ISeenStore store,
        SyncOptions opts,
        GoogleSheetOptions sheet,
        IStartupRegistrar startup,
        ILogger<MainWindowViewModel> log)
    {
        _sync = sync;
        _store = store;
        _opts = opts;
        _sheet = sheet;
        _startup = startup;
        _log = log;

        _sync.StateChanged += OnStateChanged;
        _sync.ItemsSynced += OnItemsSynced;
        _sync.Log += msg => _log.LogInformation("{Message}", msg);

        StartupSupported = _startup.IsSupported;
        PollIntervalText = $"Har {_opts.PollIntervalSeconds} soniyada";
    }

    /// <summary>Startup registratsiyasi + tarix + fon halqasi.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            if (_opts.RegisterStartup && _startup.IsSupported && !_startup.IsRegistered())
                _startup.Register();
            StartupRegistered = _startup.IsRegistered();

            await _store.InitializeAsync(CancellationToken.None);
            await LoadHistoryAsync();
            Start();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ishga tushirishda xato");
            StatusText = "Ishga tushirish xatosi: " + ex.Message;
            StatusBrush = ColorBrush("#C62828");
        }
    }

    private async Task LoadHistoryAsync()
    {
        var recent = await _store.GetRecentAsync(200, CancellationToken.None);
        var total = await _store.CountWrittenAsync(CancellationToken.None);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Transactions.Clear();
            foreach (var it in recent)
                Transactions.Add(new SyncRowViewModel(it));
            TotalSynced = total;
        });
    }

    private void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _ = Task.Run(() => _sync.RunLoopAsync(_cts!.Token));
    }

    [RelayCommand]
    private void Toggle()
    {
        if (IsRunning)
        {
            _cts?.Cancel();
            IsRunning = false;
        }
        else
        {
            Start();
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        try
        {
            await _sync.RunOnceAsync(CancellationToken.None);
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Qo'lda sinxron xatosi");
        }
    }

    [RelayCommand]
    private void ToggleStartup()
    {
        if (!_startup.IsSupported) return;
        if (_startup.IsRegistered()) _startup.Unregister();
        else _startup.Register();
        StartupRegistered = _startup.IsRegistered();
    }

    [RelayCommand]
    private void OpenSheet()
    {
        if (string.IsNullOrWhiteSpace(_sheet.SpreadsheetId)) return;
        OpenUrl($"https://docs.google.com/spreadsheets/d/{_sheet.SpreadsheetId}");
    }

    private void OnStateChanged(SyncState s) => Dispatcher.UIThread.Post(() =>
    {
        StatusText = s.Message;
        StatusBrush = s.Phase switch
        {
            SyncPhase.Idle => ColorBrush("#2E7D32"),
            SyncPhase.Syncing => ColorBrush("#1565C0"),
            SyncPhase.Error => ColorBrush("#C62828"),
            _ => ColorBrush("#9E9E9E"),
        };
        if (s.LastSync is { } ls)
            LastSyncText = ls.ToOffset(UzOffset).ToString("yyyy-MM-dd HH:mm");
    });

    private void OnItemsSynced(IReadOnlyList<SyncItem> items) => Dispatcher.UIThread.Post(() =>
    {
        var written = items.Where(i => i.Status == SyncItemStatus.Written).ToList();
        foreach (var it in written)
            Transactions.Insert(0, new SyncRowViewModel(it));
        TotalSynced += written.Count;
    });

    private static IBrush ColorBrush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // brauzer ochilmasa — e'tiborsiz qoldiramiz
        }
    }
}
