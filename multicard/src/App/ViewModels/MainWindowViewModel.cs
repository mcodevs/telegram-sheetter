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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotBusy))]
    private bool _isBusy;

    [ObservableProperty] private string _previewSummary = "Hali tortib olinmagan";

    public string ToggleButtonText => IsRunning ? "⏸ To'xtatish" : "▶ Boshlash";

    /// <summary>Tugmalar band emasmi (tortib olish jarayonida o'chiriladi).</summary>
    public bool NotBusy => !IsBusy;

    public ObservableCollection<SyncRowViewModel> Transactions { get; } = new();

    /// <summary>"⬇️ Tortib olish" preview jadvali.</summary>
    public ObservableCollection<PreviewRowViewModel> PreviewRows { get; } = new();

    /// <summary>Ilova ichidagi jonli log (eng yangisi tepada).</summary>
    public ObservableCollection<LogEntryViewModel> Logs { get; } = new();

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

        // Barcha ilova loglari (jumladan sync loglari) UI panelida ko'rinsin.
        UiLogSink.Instance.Emitted += OnLog;

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

    /// <summary>"⬇️ Tortib olish" — MultiCard'dagi joriy ma'lumotni oladi (yozmaydi).</summary>
    [RelayCommand]
    private async Task FetchDataAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var preview = await _sync.PreviewAsync(CancellationToken.None);

            // Har satrga o'zining "yozish" tugmasi (WriteRowAsync) ulanadi.
            var rows = preview.Select(p => new PreviewRowViewModel(p, WriteRowAsync)).ToList();
            var newCount = preview.Count(p => p.IsNew);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                PreviewRows.Clear();
                foreach (var r in rows) PreviewRows.Add(r);
                PreviewSummary = preview.Count == 0
                    ? "Ma'lumot topilmadi"
                    : $"{preview.Count} ta olindi · {newCount} ta yangi (📤 tugma bilan yoziladi)";
            });
        }
        catch (MultiCardBlockedException ex)
        {
            _log.LogWarning("Tortib olish bloklandi (IP): {Message}", ex.Message);
            SetError("IP bloklangan — O'zbekiston tarmog'ini tekshiring");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Tortib olish xatosi");
            SetError("Tortib olish xatosi: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Bitta preview qatorining "📤 Yozish" tugmasi — faqat shu yozuvni sheetга yozadi.</summary>
    private async Task WriteRowAsync(PreviewRowViewModel row)
    {
        if (row.IsBusy || row.IsWritten || !row.IsNew) return;
        try
        {
            row.IsBusy = true;
            var ok = await _sync.WriteOneAsync(row.Transaction, CancellationToken.None);
            row.IsWritten = ok;
            await LoadHistoryAsync(); // "Jami yozilgan" + tarix jadvalini yangilaydi
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Qatorni sheetга yozishda xato");
            SetError("Yozishda xato: " + ex.Message);
        }
        finally
        {
            row.IsBusy = false;
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

    private const int MaxLogLines = 500;

    private void OnLog(LogLine line) => Dispatcher.UIThread.Post(() =>
    {
        Logs.Insert(0, new LogEntryViewModel(line));
        while (Logs.Count > MaxLogLines)
            Logs.RemoveAt(Logs.Count - 1);
    });

    private void SetError(string message) => Dispatcher.UIThread.Post(() =>
    {
        StatusText = message;
        StatusBrush = ColorBrush("#C62828");
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
