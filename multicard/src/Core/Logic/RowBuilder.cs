using MultiCardSync.Core.Models;

namespace MultiCardSync.Core.Logic;

/// <summary>
/// MultiCard tranzaksiyasini master jadval ustunlariga (B→K) joylaydi.
/// credit → Приход, debit → Расход. (multicard.py'dagi build_row porti.)
/// 2026-07 shablonida jadval A emas, B ustunidan boshlanadi — qaysi ustunga
/// tushishini <see cref="TableAnchor"/> belgilaydi.
/// </summary>
public static class RowBuilder
{
    /// <summary>Тўлов тури ustuniga — ikkalasida ham shu (karta raqami yozilmaydi).</summary>
    public const string PayMethod = "Мультисард";

    public static object?[] Build(MultiCardTransaction tx, string type)
    {
        var date = (tx.Date ?? "").Split('T')[0];
        decimal amount = tx.AmountValue ?? 0m;

        object? prixod = "", tolovP = "", rasxod = "", tolovR = "";
        if (type == "credit") { prixod = amount; tolovP = PayMethod; }
        else { rasxod = amount; tolovR = PayMethod; }

        //  B     C              D       E      F        G        H              I        J              K
        // Сана | Фирма/Филиал | Марка | Инфо | Статья | Приход | Тўлов Приход | Расход | Тўлов Расход | Изоҳ
        // Инфо (E) va Изоҳ (K) — hisobchi so'roviga ko'ra bo'sh qoldiriladi
        // (credit ham, debit ham): "MultiCard пополнение/списание" yorlig'i va uzun
        // izoh matni sheetга yozilmaydi.
        return [date, "", "", "", "", prixod, tolovP, rasxod, tolovR, ""];
    }
}
