using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using MultiCardSync.Core.Abstractions;
using MultiCardSync.Core.Models;

namespace MultiCardSync.Data;

/// <summary>
/// Ko'rilgan tranzaksiyalar + sinxron tarixi (SQLite, Dapper orqali).
/// Vaqtlar UTC "O" formatida saqlanadi (leksikografik taqqoslash to'g'ri ishlashi uchun).
/// </summary>
public sealed class SeenStore : ISeenStore
{
    private readonly string _connString;

    public SeenStore(string dbPath)
        => _connString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqliteConnection(_connString);
        await c.OpenAsync(ct);
        return c;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        await c.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS seen (
                key       TEXT PRIMARY KEY,
                type      TEXT,
                tx_date   TEXT,
                amount    TEXT,
                note      TEXT,
                card_pan  TEXT,
                status    TEXT,
                synced_at TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_seen_synced ON seen(synced_at);
            CREATE TABLE IF NOT EXISTS meta (
                key   TEXT PRIMARY KEY,
                value TEXT
            );
            """);
    }

    public async Task<IReadOnlySet<string>> LoadSeenKeysAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var keys = await c.QueryAsync<string>("SELECT key FROM seen");
        return keys.ToHashSet();
    }

    public async Task<bool> IsFirstRunAsync(CancellationToken ct)
        => await GetLastSyncAsync(ct) is null;

    public async Task SaveAsync(IReadOnlyList<SyncItem> items, CancellationToken ct)
    {
        if (items.Count == 0)
            return;

        await using var c = await OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        foreach (var it in items)
        {
            await c.ExecuteAsync(
                """
                INSERT INTO seen(key,type,tx_date,amount,note,card_pan,status,synced_at)
                VALUES(@Key,@Type,@TxDate,@Amount,@Note,@CardPan,@Status,@SyncedAt)
                ON CONFLICT(key) DO UPDATE SET status=@Status, synced_at=@SyncedAt
                """,
                new
                {
                    it.Key,
                    it.Type,
                    it.TxDate,
                    Amount = it.Amount.ToString(CultureInfo.InvariantCulture),
                    it.Note,
                    it.CardPan,
                    Status = it.Status.ToString(),
                    SyncedAt = it.SyncedAt.ToUniversalTime().ToString("O"),
                },
                tx);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<DateTimeOffset?> GetLastSyncAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var v = await c.ExecuteScalarAsync<string?>("SELECT value FROM meta WHERE key='last_sync'");
        return v is null
            ? null
            : DateTimeOffset.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public async Task SetLastSyncAsync(DateTimeOffset ts, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        await c.ExecuteAsync(
            "INSERT INTO meta(key,value) VALUES('last_sync',@v) ON CONFLICT(key) DO UPDATE SET value=@v",
            new { v = ts.ToUniversalTime().ToString("O") });
    }

    public async Task<IReadOnlyList<SyncItem>> GetRecentAsync(int limit, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var rows = await c.QueryAsync(
            """
            SELECT key, type, tx_date, amount, note, card_pan, status, synced_at
            FROM seen
            WHERE status IN ('Written','Queued')
            ORDER BY synced_at DESC
            LIMIT @limit
            """,
            new { limit });

        return rows.Select(MapRow).ToList();
    }

    public async Task<int> CountWrittenAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM seen WHERE status='Written'");
    }

    public async Task PruneAsync(int keepDays, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Abs(keepDays)).ToString("O");
        await c.ExecuteAsync("DELETE FROM seen WHERE synced_at < @cutoff", new { cutoff });
    }

    private static SyncItem MapRow(dynamic r)
    {
        string amount = (string)r.amount;
        return new SyncItem(
            (string)r.key,
            (string)r.type,
            (string?)r.tx_date,
            decimal.Parse(amount, CultureInfo.InvariantCulture),
            (string?)r.note,
            (string?)r.card_pan,
            Enum.Parse<SyncItemStatus>((string)r.status),
            DateTimeOffset.Parse((string)r.synced_at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }
}
