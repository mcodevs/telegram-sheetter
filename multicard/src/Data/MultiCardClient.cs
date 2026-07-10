using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MultiCardSync.Core.Abstractions;
using MultiCardSync.Core.Models;
using MultiCardSync.Core.Options;

namespace MultiCardSync.Data;

/// <summary>
/// MultiCard API klienti (multicard.py'dagi _login/_get_token/_fetch_page porti).
/// Token ichkarida keshlanadi; 401 yoki muddat tugasa qayta login qilinadi.
/// Geo-blok (JSON o'rniga HTML/403) aniq <see cref="MultiCardBlockedException"/> ko'taradi.
/// </summary>
public sealed class MultiCardClient : IMultiCardClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _http;
    private readonly MultiCardOptions _o;
    private readonly ILogger<MultiCardClient> _log;
    private readonly SemaphoreSlim _loginGate = new(1, 1);

    private string? _token;
    private DateTimeOffset _tokenExp = DateTimeOffset.MinValue;

    public MultiCardClient(HttpClient http, MultiCardOptions o, ILogger<MultiCardClient> log)
    {
        _http = http;
        _o = o;
        _log = log;
    }

    public async Task<IReadOnlyList<MultiCardTransaction>> FetchPageAsync(
        string type, string from, string to, int page, CancellationToken ct)
    {
        var url = $"{_o.ApiBaseUrl.TrimEnd('/')}/api/multicard/WalletHistory" +
                  $"?from={from}&to={to}&page={page}&type={type}";

        var resp = await SendAuthedGetAsync(url, ct);
        try
        {
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                resp.Dispose();
                await ForceLoginAsync(ct);
                resp = await SendAuthedGetAsync(url, ct);
            }

            await EnsureJsonAsync(resp, ct);
            var body = await resp.Content.ReadFromJsonAsync<WalletHistoryResponse>(Json, ct);
            return body?.Data ?? (IReadOnlyList<MultiCardTransaction>)Array.Empty<MultiCardTransaction>();
        }
        finally
        {
            resp.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendAuthedGetAsync(string url, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.TryAddWithoutValidation("Origin", _o.ResolvedTenantUrl);
        req.Headers.TryAddWithoutValidation("Referer", _o.ResolvedTenantUrl + "/");
        return await _http.SendAsync(req, ct);
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExp)
            return _token;
        await LoginAsync(ct);
        return _token!;
    }

    private async Task ForceLoginAsync(CancellationToken ct)
    {
        _token = null;
        _tokenExp = DateTimeOffset.MinValue;
        await LoginAsync(ct);
    }

    private async Task LoginAsync(CancellationToken ct)
    {
        await _loginGate.WaitAsync(ct);
        try
        {
            // Gate ichida qayta tekshirish (boshqa chaqiruv allaqachon login qilgan bo'lishi mumkin).
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExp)
                return;

            var url = $"{_o.ApiBaseUrl.TrimEnd('/')}/api/account/token";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer"); // frontend bo'sh Bearer yuboradi
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            req.Headers.TryAddWithoutValidation("Origin", _o.ResolvedTenantUrl);
            req.Headers.TryAddWithoutValidation("Referer", _o.ResolvedTenantUrl + "/");
            req.Content = JsonContent.Create(new
            {
                Login = _o.Username,
                Password = _o.Password,
                Domain = _o.Domain,
                ConfirmCode = (string?)null,
                RememberMe = false,
            });

            using var resp = await _http.SendAsync(req, ct);
            await EnsureJsonAsync(resp, ct); // geo-blok bo'lsa MultiCardBlockedException

            var tr = await resp.Content.ReadFromJsonAsync<TokenResponse>(Json, ct);
            if (tr is null || !tr.IsSuccess || string.IsNullOrEmpty(tr.Data?.Token))
                throw new InvalidOperationException(
                    $"MultiCard login muvaffaqiyatsiz (HTTP {(int)resp.StatusCode}): {tr?.Message ?? "noma'lum javob"}");

            _token = tr.Data!.Token;
            _tokenExp = (tr.Data.Expiry?.AddMinutes(-2)) ?? DateTimeOffset.UtcNow.AddMinutes(90);
            _log.LogInformation("MultiCard login OK (token muddati {Exp:u})", _tokenExp);
        }
        finally
        {
            _loginGate.Release();
        }
    }

    /// <summary>Javob JSON emas (HTML/403 geo-blok) yoki xato bo'lsa aniq exception ko'taradi.</summary>
    private static async Task EnsureJsonAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var mediaType = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (resp.IsSuccessStatusCode && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return;

        var text = await resp.Content.ReadAsStringAsync(ct);
        var trimmed = text.TrimStart();
        var isHtml = mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
                     || trimmed.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase)
                     || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("ограничен") || text.Contains("cheklangan");

        if (isHtml)
            throw new MultiCardBlockedException(
                $"API JSON o'rniga HTML blok qaytardi (HTTP {(int)resp.StatusCode}). " +
                "So'rov O'zbekiston IP'sidan yuborilyaptimi?");

        var head = text.Length > 200 ? text[..200] : text;
        throw new InvalidOperationException($"MultiCard kutilmagan javob (HTTP {(int)resp.StatusCode}): {head}");
    }
}
