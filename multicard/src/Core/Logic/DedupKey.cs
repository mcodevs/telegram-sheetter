using System.Security.Cryptography;
using System.Text;
using MultiCardSync.Core.Models;

namespace MultiCardSync.Core.Logic;

/// <summary>
/// Barqaror dedup kaliti. Debit qatorlarda <c>uuid</c> bor; credit (Пополнение)
/// qatorlarda <c>uuid</c> null — shuning uchun date+amount+balance+note'dan hash yasaymiz.
/// (multicard.py'dagi _dedup_key porti.)
/// </summary>
public static class DedupKey
{
    public static string For(MultiCardTransaction tx, string type)
    {
        if (!string.IsNullOrEmpty(tx.Uuid))
            return tx.Uuid!;

        var raw = $"{type}|{tx.Date}|{tx.AmountValue}|{tx.Balance}|{tx.Note}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        return "h:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
