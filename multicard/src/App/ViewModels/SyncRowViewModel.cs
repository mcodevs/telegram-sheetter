using System.Globalization;
using MultiCardSync.Core.Models;

namespace MultiCardSync.App.ViewModels;

/// <summary>Jadvaldagi bitta sinxronlangan tranzaksiya (faqat ko'rsatish uchun).</summary>
public sealed class SyncRowViewModel
{
    private static readonly TimeSpan UzOffset = TimeSpan.FromHours(5);

    public string Date { get; }
    public string Direction { get; }
    public string Amount { get; }
    public string Note { get; }
    public string Status { get; }
    public string SyncedAt { get; }

    public SyncRowViewModel(SyncItem it)
    {
        Date = it.TxDate ?? "";
        Direction = it.Type == "credit" ? "Приход" : "Расход";
        Amount = it.Amount.ToString("#,##0", CultureInfo.InvariantCulture);
        Note = (it.Note ?? "").Trim();
        Status = it.Status switch
        {
            SyncItemStatus.Written => "✅ Yozildi",
            SyncItemStatus.Queued => "⏳ Navbatda",
            SyncItemStatus.Baseline => "• Baseline",
            _ => "",
        };
        SyncedAt = it.SyncedAt.ToOffset(UzOffset).ToString("MM-dd HH:mm");
    }
}
