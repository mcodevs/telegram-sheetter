namespace MultiCardSync.Core.Models;

/// <summary>Ko'rilmagan (yangi) tranzaksiya — dedup kaliti + turi + xom yozuv.</summary>
public sealed record NewTransaction(string Key, string Type, MultiCardTransaction Tx);

/// <summary>Sinxronlangan tranzaksiyaning holati.</summary>
public enum SyncItemStatus
{
    /// <summary>Birinchi ishga tushishda "ko'rilgan" deb belgilangan, sheetga YOZILMAGAN.</summary>
    Baseline,

    /// <summary>Sheetga muvaffaqiyatli yozilgan.</summary>
    Written,

    /// <summary>Sheet xatosi — navbatga qo'yilgan, keyingi urinishda yoziladi.</summary>
    Queued,
}

/// <summary>Lokal xotira + UI uchun sinxronlangan tranzaksiya yozuvi.</summary>
public sealed record SyncItem(
    string Key,
    string Type,
    string? TxDate,
    decimal Amount,
    string? Note,
    string? CardPan,
    SyncItemStatus Status,
    DateTimeOffset SyncedAt);

/// <summary>Worker holati (UI header uchun).</summary>
public enum SyncPhase { Idle, Syncing, Error, Stopped }

public sealed record SyncState(SyncPhase Phase, string Message, DateTimeOffset? LastSync);
