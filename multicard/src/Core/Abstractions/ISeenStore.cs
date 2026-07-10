using MultiCardSync.Core.Models;

namespace MultiCardSync.Core.Abstractions;

/// <summary>Ko'rilgan tranzaksiyalar + sinxron tarixi (lokal SQLite).</summary>
public interface ISeenStore
{
    Task InitializeAsync(CancellationToken ct);

    /// <summary>Barcha ko'rilgan dedup kalitlari.</summary>
    Task<IReadOnlySet<string>> LoadSeenKeysAsync(CancellationToken ct);

    /// <summary>Hali hech narsa ko'rilmaganmi (birinchi ishga tushish — baseline).</summary>
    Task<bool> IsFirstRunAsync(CancellationToken ct);

    /// <summary>Sinxron yozuvlarni saqlaydi (dedup + UI tarixi uchun).</summary>
    Task SaveAsync(IReadOnlyList<SyncItem> items, CancellationToken ct);

    Task<DateTimeOffset?> GetLastSyncAsync(CancellationToken ct);
    Task SetLastSyncAsync(DateTimeOffset ts, CancellationToken ct);

    /// <summary>UI uchun oxirgi N ta yozilgan tranzaksiya (eng yangisi birinchi).</summary>
    Task<IReadOnlyList<SyncItem>> GetRecentAsync(int limit, CancellationToken ct);

    /// <summary>Jami yozilgan (Written) tranzaksiyalar soni.</summary>
    Task<int> CountWrittenAsync(CancellationToken ct);

    /// <summary>keepDays'dan eski kalitlarni tozalaydi.</summary>
    Task PruneAsync(int keepDays, CancellationToken ct);
}
