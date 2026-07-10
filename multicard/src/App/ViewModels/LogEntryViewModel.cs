using Avalonia.Media;
using MultiCardSync.App.Services;
using Serilog.Events;

namespace MultiCardSync.App.ViewModels;

/// <summary>"Log" panelidagi bitta satr — vaqt + daraja belgisi + xabar.</summary>
public sealed class LogEntryViewModel
{
    private static readonly TimeSpan UzOffset = TimeSpan.FromHours(5);

    public string Time { get; }
    public string Level { get; }
    public string Message { get; }

    /// <summary>Faqat daraja belgisining rangi (xabar matni mavzuga mos qoladi).</summary>
    public IBrush LevelBrush { get; }

    public LogEntryViewModel(LogLine line)
    {
        Time = line.Timestamp.ToOffset(UzOffset).ToString("HH:mm:ss");
        Message = line.Message;
        (Level, LevelBrush) = line.Level switch
        {
            LogEventLevel.Fatal or LogEventLevel.Error => ("ERR", Brush("#C62828")),
            LogEventLevel.Warning => ("WARN", Brush("#E65100")),
            _ => ("INFO", Brush("#2E7D32")),
        };
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
