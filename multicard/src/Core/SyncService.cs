using MultiCardSync.Core.Abstractions;
using MultiCardSync.Core.Logic;
using MultiCardSync.Core.Models;
using MultiCardSync.Core.Options;

namespace MultiCardSync.Core;

/// <summary>
/// Sinxron orkestratori (multicard.py'dagi worker() porti):
///   1. Adaptiv lookback bilan MultiCard'dan tranzaksiya oladi (kompyuter o'chiq
///      turgan davrni quvib yetadi).
///   2. Lokal bazadagi ko'rilganlar bilan solishtiradi (dedup).
///   3. Yangi tranzaksiyalarni Sheet'ga yozadi va lokal bazaga saqlaydi.
///   4. Birinchi ishga tushish = baseline (mavjud tarix "ko'rilgan" deb belgilanadi,
///      YOZILMAYDI — eski qatorlarni to'kib tashlamaslik uchun).
/// </summary>
public sealed class SyncService
{
    // Asia/Tashkent (UTC+5) — Windows/tzdata farqiga bog'lanmaslik uchun qat'iy offset.
    private static readonly TimeSpan UzOffset = TimeSpan.FromHours(5);

    private readonly IMultiCardClient _client;
    private readonly ISheetWriter _sheet;
    private readonly ISeenStore _store;
    private readonly MultiCardOptions _mc;
    private readonly SyncOptions _sync;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SyncService(IMultiCardClient client, ISheetWriter sheet, ISeenStore store,
        MultiCardOptions mc, SyncOptions sync)
    {
        _client = client;
        _sheet = sheet;
        _store = store;
        _mc = mc;
        _sync = sync;
    }

    /// <summary>Holat o'zgarganda (UI header uchun).</summary>
    public event Action<SyncState>? StateChanged;

    /// <summary>Yangi tranzaksiyalar Sheet'ga yozilganda (UI ro'yxati uchun).</summary>
    public event Action<IReadOnlyList<SyncItem>>? ItemsSynced;

    /// <summary>Diagnostika log satri.</summary>
    public event Action<string>? Log;

    private DateTimeOffset NowUz => DateTimeOffset.UtcNow.ToOffset(UzOffset);

    /// <summary>Fon halqasi: darrov bir marta, so'ng har PollInterval'da.</summary>
    public async Task RunLoopAsync(CancellationToken ct)
    {
        await _store.InitializeAsync(ct);
        Emit(SyncPhase.Idle, "Tayyor", await _store.GetLastSyncAsync(ct));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (MultiCardBlockedException ex)
            {
                Log?.Invoke("BLOK (IP): " + ex.Message);
                Emit(SyncPhase.Error, "IP bloklangan — O'zbekiston tarmog'ini tekshiring");
            }
            catch (Exception ex)
            {
                Log?.Invoke("Sync xatosi: " + ex);
                Emit(SyncPhase.Error, "Xato: " + ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(30, _sync.PollIntervalSeconds)), ct);
            }
            catch (OperationCanceledException) { break; }
        }

        Emit(SyncPhase.Stopped, "To'xtatildi");
    }

    /// <summary>Bitta sinxron sikli. UI "Hozir sinxronla" tugmasi ham shuni chaqiradi.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var lastSync = await _store.GetLastSyncAsync(ct);
            Emit(SyncPhase.Syncing, "Tekshirilmoqda…", lastSync);

            var (from, to) = ComputeWindow(lastSync);
            var seen = await _store.LoadSeenKeysAsync(ct);
            var firstRun = await _store.IsFirstRunAsync(ct);
            var found = await CollectNewAsync(seen, from, to, ct);
            var now = NowUz;

            if (firstRun)
            {
                var baseline = found
                    .Select(f => ToItem(f.Key, f.Type, f.Tx, SyncItemStatus.Baseline, now))
                    .ToList();
                await _store.SaveAsync(baseline, ct);
                await _store.SetLastSyncAsync(now, ct);
                Log?.Invoke($"Baseline: {baseline.Count} ta mavjud tranzaksiya belgilandi (yozilmadi).");
                Emit(SyncPhase.Idle, $"Baseline tayyor — {baseline.Count} ta belgilandi", now);
                return 0;
            }

            var written = 0;
            if (found.Count > 0)
            {
                // Eskidan yangiga: sana bo'yicha o'sish tartibida yozamiz.
                var ordered = found.OrderBy(f => f.Tx.Date ?? "", StringComparer.Ordinal).ToList();
                var rows = ordered.Select(f => RowBuilder.Build(f.Tx, f.Type)).ToList();

                SyncItemStatus status;
                try
                {
                    await _sheet.AppendRowsAsync(rows, ct);
                    status = SyncItemStatus.Written;
                    written = rows.Count;
                }
                catch (Exception ex)
                {
                    status = SyncItemStatus.Queued;
                    Log?.Invoke($"Sheet xato — {rows.Count} qator navbatga saqlandi: {ex.Message}");
                }

                var items = ordered.Select(f => ToItem(f.Key, f.Type, f.Tx, status, now)).ToList();
                await _store.SaveAsync(items, ct);

                if (status == SyncItemStatus.Written)
                {
                    Log?.Invoke($"{written} yangi tranzaksiya yozildi.");
                    ItemsSynced?.Invoke(items);
                }
            }

            await _store.SetLastSyncAsync(now, ct);
            try { await _sheet.WriteHeartbeatAsync($"Oxirgi sinxron: {now:yyyy-MM-dd HH:mm}", ct); }
            catch (Exception ex) { Log?.Invoke("Heartbeat xato: " + ex.Message); }
            await _store.PruneAsync(_sync.SeenPruneDays, ct);

            Emit(SyncPhase.Idle, written > 0 ? $"{written} ta yangi yozildi" : "Yangi tranzaksiya yo'q", now);
            return written;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Joriy oynadagi MultiCard tranzaksiyalarini oladi (SHEETGA YOZMAYDI, ko'rilgan
    /// deb belgilamaydi). UI "⬇️ Tortib olish" tugmasi uchun — har bir satr yangi
    /// (yoziladigan) yoki allaqachon sinxronlangan ekanini belgilaydi.
    /// Fon halqasi bilan navbatlashadi (<see cref="_gate"/>).
    /// </summary>
    public async Task<IReadOnlyList<PreviewTransaction>> PreviewAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var lastSync = await _store.GetLastSyncAsync(ct);
            Emit(SyncPhase.Syncing, "Ma'lumot olinmoqda…", lastSync);

            var (from, to) = ComputeWindow(lastSync);
            var seen = await _store.LoadSeenKeysAsync(ct);

            var result = new List<PreviewTransaction>();
            var seenNow = new HashSet<string>();

            foreach (var type in _mc.Types)
            {
                for (var page = 1; page <= _mc.MaxPages; page++)
                {
                    var rows = await _client.FetchPageAsync(type, from, to, page, ct);
                    if (rows.Count == 0) break;

                    foreach (var tx in rows)
                    {
                        var key = DedupKey.For(tx, type);
                        if (!seenNow.Add(key)) continue; // shu fetch ichidagi takror
                        result.Add(new PreviewTransaction(key, type, tx, IsNew: !seen.Contains(key)));
                    }

                    if (rows.Count < _mc.PageSize) break;
                }
            }

            // Eng yangisi tepada ko'rinsin.
            result.Sort((a, b) => string.CompareOrdinal(b.Tx.Date ?? "", a.Tx.Date ?? ""));

            var newCount = result.Count(r => r.IsNew);
            Log?.Invoke($"Tortib olindi: {result.Count} ta tranzaksiya ({newCount} ta yangi).");
            Emit(SyncPhase.Idle, $"{result.Count} ta olindi — {newCount} ta yangi", lastSync);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// BITTA preview tranzaksiyasini Google Sheet'ga yozadi va ko'rilgan deb belgilaydi.
    /// UI'dagi har qatordagi "📤 Yozish" tugmasi uchun. Idempotent — allaqachon ko'rilgan
    /// (yozilgan) yozuvni qayta yozmaydi. Muvaffaqiyatli yozilsa <c>true</c> qaytadi.
    ///
    /// Baseline-xavfsizlik: agar baseline hali o'rnatilmagan bo'lsa (lastSync == null,
    /// birinchi ishga tushish) — bu yozuvni yozadi, biroq lastSync'ni ILGARILATMAYDI.
    /// Shunda fon halqasi (<see cref="RunOnceAsync"/>) hamon "birinchi ishga tushish"
    /// deb qolib, qolgan mavjud tarixni sheetга to'kib yubormaydi (baseline qiladi) —
    /// bu yozuv esa allaqachon ko'rilgan bo'lgani uchun qайta yozilmaydi.
    /// </summary>
    public async Task<bool> WriteOneAsync(PreviewTransaction p, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var seen = await _store.LoadSeenKeysAsync(ct);
            if (seen.Contains(p.Key))
            {
                Emit(SyncPhase.Idle, "Bu yozuv allaqachon sheetда", await _store.GetLastSyncAsync(ct));
                return false;
            }

            var now = NowUz;
            Emit(SyncPhase.Syncing, "1 ta yozilmoqda…", await _store.GetLastSyncAsync(ct));

            var rows = new List<object?[]> { RowBuilder.Build(p.Tx, p.Type) };

            SyncItemStatus status;
            try
            {
                await _sheet.AppendRowsAsync(rows, ct);
                status = SyncItemStatus.Written;
            }
            catch (Exception ex)
            {
                status = SyncItemStatus.Queued;
                Log?.Invoke($"Sheet xato — 1 qator navbatga saqlandi: {ex.Message}");
            }

            var item = ToItem(p.Key, p.Type, p.Tx, status, now);
            await _store.SaveAsync(new[] { item }, ct);

            // lastSync'ni faqat baseline allaqachon bor bo'lsa ilgarilatamiz (yuqoridagi izoh).
            var lastSync = await _store.GetLastSyncAsync(ct);
            if (lastSync is not null)
                await _store.SetLastSyncAsync(now, ct);

            if (status == SyncItemStatus.Written)
            {
                var when = (p.Tx.Date ?? "").Replace("T", " ");
                Log?.Invoke($"Qo'lda yozildi: {when} · {p.Tx.AmountValue} ({p.Type}).");
                ItemsSynced?.Invoke(new[] { item });
                try { await _sheet.WriteHeartbeatAsync($"Oxirgi sinxron: {now:yyyy-MM-dd HH:mm}", ct); }
                catch (Exception ex) { Log?.Invoke("Heartbeat xato: " + ex.Message); }
            }

            Emit(SyncPhase.Idle, status == SyncItemStatus.Written ? "1 ta yozildi" : "Navbatga saqlandi",
                await _store.GetLastSyncAsync(ct));
            return status == SyncItemStatus.Written;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Adaptiv oyna: oddiy holatda LookbackDays; kompyuter uzoq o'chiq turgan bo'lsa,
    /// oxirgi sinxrondan beri o'tgan davrni (MaxLookbackDays'gacha) qamrab oladi.
    /// Ortiqcha olingan tranzaksiyalar dedup bilan tashlanadi — hech narsa yo'qolmaydi.
    /// </summary>
    private (string From, string To) ComputeWindow(DateTimeOffset? lastSync)
    {
        var today = NowUz.Date;
        var daysBack = _sync.LookbackDays;

        if (lastSync is { } ls)
        {
            var gap = (today - ls.ToOffset(UzOffset).Date).Days + 2; // +2 kun zaxira
            daysBack = Math.Max(daysBack, gap);
        }

        daysBack = Math.Clamp(daysBack, 1, Math.Max(1, _sync.MaxLookbackDays));

        var from = today.AddDays(-daysBack).ToString("ddMMyy");
        var to = today.AddDays(1).ToString("ddMMyy");
        return (from, to);
    }

    private async Task<IReadOnlyList<NewTransaction>> CollectNewAsync(
        IReadOnlySet<string> seen, string from, string to, CancellationToken ct)
    {
        var found = new List<NewTransaction>();
        var seenNow = new HashSet<string>();

        foreach (var type in _mc.Types)
        {
            for (var page = 1; page <= _mc.MaxPages; page++)
            {
                var rows = await _client.FetchPageAsync(type, from, to, page, ct);
                if (rows.Count == 0) break;

                foreach (var tx in rows)
                {
                    var key = DedupKey.For(tx, type);
                    if (seen.Contains(key) || !seenNow.Add(key)) continue;
                    found.Add(new NewTransaction(key, type, tx));
                }

                if (rows.Count < _mc.PageSize) break;
            }
        }

        return found;
    }

    private static SyncItem ToItem(string key, string type, MultiCardTransaction tx, SyncItemStatus status, DateTimeOffset now)
    {
        var date = (tx.Date ?? "").Split('T')[0];
        return new SyncItem(key, type, date, tx.AmountValue ?? 0m, tx.Note, tx.CardPan, status, now);
    }

    private void Emit(SyncPhase phase, string message, DateTimeOffset? lastSync = null)
        => StateChanged?.Invoke(new SyncState(phase, message, lastSync));
}
