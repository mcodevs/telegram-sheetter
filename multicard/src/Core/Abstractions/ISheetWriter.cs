namespace MultiCardSync.Core.Abstractions;

/// <summary>Google Sheet'ga yozuvchi.</summary>
public interface ISheetWriter
{
    /// <summary>Qatorlarni varaq oxiriga qo'shadi (USER_ENTERED).</summary>
    Task AppendRowsAsync(IReadOnlyList<object?[]> rows, CancellationToken ct);

    /// <summary>"Oxirgi sinxron" belgisini bitta katakka yozadi (ixtiyoriy).</summary>
    Task WriteHeartbeatAsync(string text, CancellationToken ct);
}
