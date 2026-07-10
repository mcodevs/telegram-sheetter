using System.Text.Json.Serialization;

namespace MultiCardSync.Core.Models;

/// <summary>POST /api/account/token javobi.</summary>
public sealed class TokenResponse
{
    [JsonPropertyName("isSuccess")]
    public bool IsSuccess { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public TokenData? Data { get; set; }
}

public sealed class TokenData
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    /// <summary>Token amal qilish muddati (masalan 2026-07-09T22:31:59+05:00).</summary>
    [JsonPropertyName("expiry")]
    public DateTimeOffset? Expiry { get; set; }
}

/// <summary>GET /api/multicard/WalletHistory javobi.</summary>
public sealed class WalletHistoryResponse
{
    [JsonPropertyName("data")]
    public List<MultiCardTransaction>? Data { get; set; }
}
