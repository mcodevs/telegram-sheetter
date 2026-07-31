using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Logging;
using MultiCardSync.Core.Abstractions;
using MultiCardSync.Core.Logic;
using MultiCardSync.Core.Options;

namespace MultiCardSync.Data;

/// <summary>
/// Google Sheets'ga service-account orqali yozadi (main.py'dagi gspread'ning C# ekvivalenti).
/// Varaqni NOMI bo'yicha topadi va qatorlarni jadval sarlavhasiga bog'langan
/// diapazonga (B→K) qo'shadi.
/// </summary>
public sealed class GoogleSheetWriter : ISheetWriter, IDisposable
{
    private readonly GoogleSheetOptions _o;
    private readonly ILogger<GoogleSheetWriter> _log;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private SheetsService? _svc;
    private string? _sheetTitle;
    private string? _appendRange;

    public GoogleSheetWriter(GoogleSheetOptions o, ILogger<GoogleSheetWriter> log)
    {
        _o = o;
        _log = log;
    }

    private async Task EnsureReadyAsync(CancellationToken ct)
    {
        if (_svc is not null && _appendRange is not null)
            return;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_svc is null)
            {
                GoogleCredential cred = string.IsNullOrWhiteSpace(_o.CredentialsJson)
                    ? GoogleCredential.FromFile(_o.CredentialsPath)
                    : GoogleCredential.FromJson(_o.CredentialsJson);

                cred = cred.CreateScoped(SheetsService.Scope.Spreadsheets);
                _svc = new SheetsService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = cred,
                    ApplicationName = "MultiCardSync",
                });
            }

            _sheetTitle ??= await ResolveSheetTitleAsync(ct);
            _appendRange ??= await ResolveAppendRangeAsync(_sheetTitle, ct);
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>Varaqni NOM bo'yicha topadi (nom bo'sh bo'lsagina — eski indeks usuli).</summary>
    private async Task<string> ResolveSheetTitleAsync(CancellationToken ct)
    {
        var ss = await _svc!.Spreadsheets.Get(_o.SpreadsheetId).ExecuteAsync(ct);
        var sheets = ss.Sheets;
        if (sheets is null || sheets.Count == 0)
            throw new InvalidOperationException("Jadvalda birorta ham varaq yo'q.");

        if (!string.IsNullOrWhiteSpace(_o.WorksheetTitle))
        {
            var match = sheets.FirstOrDefault(s =>
                string.Equals(s.Properties.Title, _o.WorksheetTitle, StringComparison.Ordinal));

            if (match is null)
                throw new InvalidOperationException(
                    $"'{_o.WorksheetTitle}' varag'i topilmadi. Mavjud varaqlar: " +
                    string.Join(", ", sheets.Select(s => $"'{s.Properties.Title}'")));

            _log.LogInformation("Sheet tayyor: '{Title}' (nom bo'yicha)", match.Properties.Title);
            return match.Properties.Title;
        }

        if (_o.WorksheetIndex < 0 || _o.WorksheetIndex >= sheets.Count)
            throw new InvalidOperationException(
                $"Varaq indeksi {_o.WorksheetIndex} topilmadi (jami {sheets.Count} varaq).");

        var byIndex = sheets[_o.WorksheetIndex].Properties.Title;
        _log.LogWarning(
            "Varaq INDEKS bo'yicha olindi: '{Title}'. Varaqlar tartibi o'zgarsa yozuvlar " +
            "noto'g'ri varaqqa tushadi — GoogleSheet:WorksheetTitle'ni to'ldiring.", byIndex);
        return byIndex;
    }

    /// <summary>
    /// Qatorlar qo'shiladigan diapazon — jadval sarlavhasiga bog'lanadi ("B20:K").
    /// Sarlavha qatori raqami qat'iy yozilmaydi: varaq tepasidagi hisobot bloki
    /// o'sishi mumkin, shuning uchun har ishga tushishda topiladi.
    /// </summary>
    private async Task<string> ResolveAppendRangeAsync(string sheetTitle, CancellationToken ct)
    {
        var probe = $"'{sheetTitle}'!{TableAnchor.FirstColumn}1:{TableAnchor.FirstColumn}200";
        var resp = await _svc!.Spreadsheets.Values.Get(_o.SpreadsheetId, probe).ExecuteAsync(ct);

        var column = (resp.Values ?? new List<IList<object>>())
            .Select(r => r.Count > 0 ? r[0]?.ToString() : null);

        var headerRow = TableAnchor.FindHeaderRow(column);
        if (headerRow == 0)
            throw new InvalidOperationException(
                $"'{sheetTitle}' varag'ining {TableAnchor.FirstColumn} ustunida " +
                $"'{TableAnchor.HeaderText}' sarlavhasi topilmadi — jadval strukturasi o'zgargan bo'lishi mumkin.");

        var range = TableAnchor.RangeFrom(headerRow);
        _log.LogInformation("Jadval diapazoni: {Range} (sarlavha {Row}-qatorda)", range, headerRow);
        return $"'{sheetTitle}'!{range}";
    }

    public async Task AppendRowsAsync(IReadOnlyList<object?[]> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return;

        await EnsureReadyAsync(ct);

        var values = rows
            .Select(r => (IList<object>)r.Select(c => c ?? "").ToList())
            .ToList();

        var body = new ValueRange { Values = values };
        var req = _svc!.Spreadsheets.Values.Append(body, _o.SpreadsheetId, _appendRange);
        req.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        req.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
        await req.ExecuteAsync(ct);
    }

    public async Task WriteHeartbeatAsync(string text, CancellationToken ct)
    {
        if (!_o.WriteHeartbeat)
            return;

        await EnsureReadyAsync(ct);

        var body = new ValueRange { Values = new List<IList<object>> { new List<object> { text } } };
        var req = _svc!.Spreadsheets.Values.Update(body, _o.SpreadsheetId, $"'{_sheetTitle}'!{_o.HeartbeatCell}");
        req.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        await req.ExecuteAsync(ct);
    }

    public void Dispose() => _svc?.Dispose();
}
