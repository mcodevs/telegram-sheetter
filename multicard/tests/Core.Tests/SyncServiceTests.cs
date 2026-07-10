using MultiCardSync.Core;
using MultiCardSync.Core.Abstractions;
using MultiCardSync.Core.Models;
using MultiCardSync.Core.Options;
using Shouldly;
using Xunit;

namespace MultiCardSync.Core.Tests;

/// <summary>
/// UI "⬇️ Tortib olish" (PreviewAsync) va har qatordagi "📤 Yozish" (WriteOneAsync)
/// oqimlari — soxta (in-memory) klient/sheet/store bilan.
/// </summary>
public class SyncServiceTests
{
    private static SyncService Build(StubClient client, RecordingSheet sheet, InMemoryStore store)
        => new(client, sheet, store,
            new MultiCardOptions(),               // Types = credit + debit
            new SyncOptions());

    private static MultiCardTransaction Debit(string uuid, decimal amount, string note = "")
        => new() { Uuid = uuid, Date = "2026-07-09T10:00:00", AmountValue = amount, Note = note };

    [Fact]
    public async Task WriteOneAsync_writes_single_record_and_marks_seen()
    {
        var store = new InMemoryStore();
        await store.SetLastSyncAsync(DateTimeOffset.UtcNow, default); // baseline bor → first-run emas
        var sheet = new RecordingSheet();
        var sync = Build(new StubClient(), sheet, store);

        var ok = await sync.WriteOneAsync(
            new PreviewTransaction("new1", "debit", Debit("new1", 100m), IsNew: true), default);

        ok.ShouldBeTrue();
        sheet.AppendCalls.ShouldBe(1);
        sheet.LastRows!.Count.ShouldBe(1);                       // faqat shu bitta yozuv
        (await store.LoadSeenKeysAsync(default)).ShouldContain("new1");
    }

    [Fact]
    public async Task WriteOneAsync_leaves_info_and_izoh_columns_empty()
    {
        var store = new InMemoryStore();
        await store.SetLastSyncAsync(DateTimeOffset.UtcNow, default);
        var sheet = new RecordingSheet();
        var sync = Build(new StubClient(), sheet, store);

        await sync.WriteOneAsync(
            new PreviewTransaction("x", "debit", Debit("x", 50450m, "ПОПОЛНЕНИЕ ДЕПОЗИТА"), IsNew: true), default);

        var row = sheet.LastRows!.Single();
        row[3].ShouldBe(""); // Инфо (D) — bo'sh
        row[9].ShouldBe(""); // Изоҳ (J) — bo'sh
    }

    [Fact]
    public async Task WriteOneAsync_is_idempotent_for_already_written_record()
    {
        var store = new InMemoryStore();
        await store.SetLastSyncAsync(DateTimeOffset.UtcNow, default);
        await store.SaveAsync(
            new[] { new SyncItem("dup", "debit", "2026-07-09", 1m, null, null, SyncItemStatus.Written, DateTimeOffset.UtcNow) },
            default);
        var sheet = new RecordingSheet();
        var sync = Build(new StubClient(), sheet, store);

        var ok = await sync.WriteOneAsync(
            new PreviewTransaction("dup", "debit", Debit("dup", 1m), IsNew: false), default);

        ok.ShouldBeFalse();          // qayta yozilmaydi
        sheet.AppendCalls.ShouldBe(0);
    }

    [Fact]
    public async Task WriteOneAsync_on_first_run_writes_but_keeps_baseline_pending()
    {
        // Baseline hali yo'q (lastSync == null). Bitta qatorni yozamiz, LEKIN lastSync
        // ilgarilamaydi → fon halqasi hamon "birinchi ishga tushish" deb qolib, qolgan
        // tarixni to'kib yubormaydi (baseline qiladi). Bu — asosiy xavfsizlik invarianti.
        var store = new InMemoryStore(); // IsFirstRun == true
        var sheet = new RecordingSheet();
        var sync = Build(new StubClient(), sheet, store);

        var ok = await sync.WriteOneAsync(
            new PreviewTransaction("k1", "debit", Debit("k1", 100m), IsNew: true), default);

        ok.ShouldBeTrue();
        sheet.AppendCalls.ShouldBe(1);                          // yozuv sheetga tushdi
        (await store.LoadSeenKeysAsync(default)).ShouldContain("k1");
        (await store.IsFirstRunAsync(default)).ShouldBeTrue();  // lastSync ILGARILAMADI
    }

    [Fact]
    public async Task PreviewAsync_flags_already_seen_items_as_not_new()
    {
        var store = new InMemoryStore();
        await store.SetLastSyncAsync(DateTimeOffset.UtcNow, default);
        // "u1" allaqachon ko'rilgan deb belgilaymiz.
        await store.SaveAsync(
            new[] { new SyncItem("u1", "debit", "2026-07-09", 100m, null, null, SyncItemStatus.Written, DateTimeOffset.UtcNow) },
            default);

        var client = new StubClient();
        client.Pages["debit"] = new() { Debit("u1", 100m), Debit("u2", 200m) };
        var sync = Build(client, new RecordingSheet(), store);

        var preview = await sync.PreviewAsync(default);

        preview.Count.ShouldBe(2);
        preview.Single(p => p.Key == "u1").IsNew.ShouldBeFalse();
        preview.Single(p => p.Key == "u2").IsNew.ShouldBeTrue();
    }

    // --- Fakes -------------------------------------------------------------

    private sealed class StubClient : IMultiCardClient
    {
        public Dictionary<string, List<MultiCardTransaction>> Pages { get; } = new();

        // 1-sahifada butun ro'yxat, keyin bo'sh (loop to'xtaydi).
        public Task<IReadOnlyList<MultiCardTransaction>> FetchPageAsync(
            string type, string from, string to, int page, CancellationToken ct)
        {
            IReadOnlyList<MultiCardTransaction> result =
                page == 1 && Pages.TryGetValue(type, out var list) ? list : new List<MultiCardTransaction>();
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingSheet : ISheetWriter
    {
        public int AppendCalls { get; private set; }
        public IReadOnlyList<object?[]>? LastRows { get; private set; }

        public Task AppendRowsAsync(IReadOnlyList<object?[]> rows, CancellationToken ct)
        {
            AppendCalls++;
            LastRows = rows;
            return Task.CompletedTask;
        }

        public Task WriteHeartbeatAsync(string text, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class InMemoryStore : ISeenStore
    {
        private readonly Dictionary<string, SyncItem> _items = new();
        private DateTimeOffset? _lastSync;

        public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlySet<string>> LoadSeenKeysAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlySet<string>>(_items.Keys.ToHashSet());

        public Task<bool> IsFirstRunAsync(CancellationToken ct) => Task.FromResult(_lastSync is null);

        public Task SaveAsync(IReadOnlyList<SyncItem> items, CancellationToken ct)
        {
            foreach (var it in items) _items[it.Key] = it;
            return Task.CompletedTask;
        }

        public Task<DateTimeOffset?> GetLastSyncAsync(CancellationToken ct) => Task.FromResult(_lastSync);
        public Task SetLastSyncAsync(DateTimeOffset ts, CancellationToken ct) { _lastSync = ts; return Task.CompletedTask; }

        public Task<IReadOnlyList<SyncItem>> GetRecentAsync(int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SyncItem>>(
                _items.Values.Where(i => i.Status is SyncItemStatus.Written or SyncItemStatus.Queued).ToList());

        public Task<int> CountWrittenAsync(CancellationToken ct)
            => Task.FromResult(_items.Values.Count(i => i.Status == SyncItemStatus.Written));

        public Task PruneAsync(int keepDays, CancellationToken ct) => Task.CompletedTask;
    }
}
