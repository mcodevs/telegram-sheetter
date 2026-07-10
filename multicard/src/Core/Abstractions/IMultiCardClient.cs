using MultiCardSync.Core.Models;

namespace MultiCardSync.Core.Abstractions;

/// <summary>MultiCard API klienti — login/token boshqaruvi ichkarida.</summary>
public interface IMultiCardClient
{
    /// <summary>
    /// WalletHistory'dan bitta sahifani oladi. Token muddati tugagan/401 bo'lsa
    /// ichkarida qayta login qiladi. Geo-blok (403 HTML) bo'lsa aniq
    /// <see cref="MultiCardBlockedException"/> ko'taradi.
    /// </summary>
    Task<IReadOnlyList<MultiCardTransaction>> FetchPageAsync(
        string type, string from, string to, int page, CancellationToken ct);
}

/// <summary>MultiCard API JSON o'rniga HTML "kirish cheklangan" (geo-blok) qaytarganda.</summary>
public sealed class MultiCardBlockedException(string message) : Exception(message);
