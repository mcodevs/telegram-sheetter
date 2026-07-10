using MultiCardSync.Core.Models;

namespace MultiCardSync.Core.Logic;

/// <summary>
/// MultiCard tranzaksiyasini master jadval ustunlariga (A→J) joylaydi.
/// credit → Приход, debit → Расход. (multicard.py'dagi build_row porti.)
/// </summary>
public static class RowBuilder
{
    /// <summary>Тўлов тури ustuniga — ikkalasida ham shu (karta raqami yozilmaydi).</summary>
    public const string PayMethod = "Мультисард";

    public static object?[] Build(MultiCardTransaction tx, string type)
    {
        var date = (tx.Date ?? "").Split('T')[0];
        decimal amount = tx.AmountValue ?? 0m;

        var info = type == "credit" ? "MultiCard пополнение" : "MultiCard списание";
        var dt = (tx.Date ?? "").Replace("T", " ");
        var note = (tx.Note ?? "").Trim();
        var izoh = note.Length > 0 ? $"{dt} · {note}" : dt;

        object? prixod = "", tolovP = "", rasxod = "", tolovR = "";
        if (type == "credit") { prixod = amount; tolovP = PayMethod; }
        else { rasxod = amount; tolovR = PayMethod; }

        // Сана | Фирма/Филиал | (филиал) | Инфо | Статья | Приход | Тўлов Приход | Расход | Тўлов Расход | Изоҳ
        return [date, "", "", info, "", prixod, tolovP, rasxod, tolovR, izoh];
    }
}
