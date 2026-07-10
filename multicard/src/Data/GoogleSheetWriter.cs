using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Logging;
using MultiCardSync.Core.Abstractions;
using MultiCardSync.Core.Options;

namespace MultiCardSync.Data;

/// <summary>
/// Google Sheets'ga service-account orqali yozadi (main.py'dagi gspread'ning C# ekvivalenti).
/// Varaqni indeks bo'yicha topib, uning nomiga (A:J) qatorlar qo'shadi.
/// </summary>
public sealed class GoogleSheetWriter : ISheetWriter, IDisposable
{
    private readonly GoogleSheetOptions _o;
    private readonly ILogger<GoogleSheetWriter> _log;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private SheetsService? _svc;
    private string? _sheetTitle;

    public GoogleSheetWriter(GoogleSheetOptions o, ILogger<GoogleSheetWriter> log)
    {
        _o = o;
        _log = log;
    }

    private async Task EnsureReadyAsync(CancellationToken ct)
    {
        if (_svc is not null && _sheetTitle is not null)
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

            if (_sheetTitle is null)
            {
                var ss = await _svc.Spreadsheets.Get(_o.SpreadsheetId).ExecuteAsync(ct);
                var sheets = ss.Sheets;
                if (sheets is null || _o.WorksheetIndex < 0 || _o.WorksheetIndex >= sheets.Count)
                    throw new InvalidOperationException(
                        $"Varaq indeksi {_o.WorksheetIndex} topilmadi (jami {sheets?.Count ?? 0} varaq).");

                _sheetTitle = sheets[_o.WorksheetIndex].Properties.Title;
                _log.LogInformation("Sheet tayyor: '{Title}'", _sheetTitle);
            }
        }
        finally
        {
            _initGate.Release();
        }
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
        var req = _svc!.Spreadsheets.Values.Append(body, _o.SpreadsheetId, $"'{_sheetTitle}'!A:J");
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
