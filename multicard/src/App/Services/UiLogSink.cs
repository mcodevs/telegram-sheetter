using Serilog.Core;
using Serilog.Events;

namespace MultiCardSync.App.Services;

/// <summary>
/// Serilog sink — har bir log yozuvini UI'ga uzatadi (ilova ichidagi "Log" paneli uchun).
/// <see cref="Program"/>da fayl sink'i yonida ro'yxatdan o'tadi; ViewModel shu
/// <see cref="Instance"/>ga obuna bo'lib, satrlarni jonli ko'rsatadi.
/// </summary>
public sealed class UiLogSink : ILogEventSink
{
    /// <summary>Yagona umumiy nusxa (Serilog config ham, VM ham shundan foydalanadi).</summary>
    public static UiLogSink Instance { get; } = new();

    private UiLogSink() { }

    /// <summary>Yangi log satri chiqqanda (istalgan ip'da chaqiriladi).</summary>
    public event Action<LogLine>? Emitted;

    public void Emit(LogEvent logEvent)
        => Emitted?.Invoke(new LogLine(logEvent.Timestamp, logEvent.Level, logEvent.RenderMessage()));
}

/// <summary>UI'ga uzatiladigan bitta log satri.</summary>
public readonly record struct LogLine(DateTimeOffset Timestamp, LogEventLevel Level, string Message);
