using System.Text.Json.Serialization;

namespace MultiCardSync.Core.Models;

/// <summary>
/// MultiCard WalletHistory javobidagi bitta tranzaksiya.
/// Debit qatorlarda <see cref="Uuid"/> bor; credit (Пополнение) qatorlarda u null.
/// </summary>
public sealed class MultiCardTransaction
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("amountValue")]
    public decimal? AmountValue { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("balance")]
    public decimal? Balance { get; set; }

    [JsonPropertyName("cardPan")]
    public string? CardPan { get; set; }
}
