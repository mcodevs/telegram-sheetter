namespace MultiCardSync.Core.Logic;

/// <summary>
/// Master varaqdagi ma'lumot jadvali qayerdan boshlanishini aniqlaydi.
///
/// Sheets API'ning "append" amali berilgan diapazondagi jadvalni o'zi topib, uning
/// oxiriga yozadi. Diapazonni aniq ko'rsatmasak, API taxmin qiladi — varaq tepasidagi
/// hisobot bloki (Остатка/Приход/Расход/Баланс) tufayli bu ishonchsiz. Shuning uchun
/// sarlavha qatorini ("Сана") topib, diapazonni aniq bog'laymiz.
///
/// Sarlavha qatori raqami qat'iy yozilmaydi: tepadagi blok o'sishi mumkin, shuning
/// uchun har ishga tushishda qaytadan topiladi.
/// </summary>
public static class TableAnchor
{
    /// <summary>Jadval boshlanishini bildiruvchi sarlavha matni (B ustunida).</summary>
    public const string HeaderText = "Сана";

    /// <summary>Jadvalning birinchi ustuni (2026-07 shablonida A bo'sh, jadval B'dan).</summary>
    public const string FirstColumn = "B";

    /// <summary>Jadvalning oxirgi YOZILADIGAN ustuni (K = Изоҳ; L/M formulali).</summary>
    public const string LastColumn = "K";

    /// <summary>
    /// B ustuni qiymatlaridan sarlavha qatorini topadi. Qator raqami 1'dan boshlanadi;
    /// topilmasa 0 qaytaradi.
    /// </summary>
    public static int FindHeaderRow(IEnumerable<string?> columnValues)
    {
        var row = 0;
        foreach (var value in columnValues)
        {
            row++;
            if (string.Equals(value?.Trim(), HeaderText, StringComparison.Ordinal))
                return row;
        }

        return 0;
    }

    /// <summary>Sarlavha qatoridan append diapazonini yasaydi, masalan "B20:K".</summary>
    public static string RangeFrom(int headerRow)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(headerRow, 1);
        return $"{FirstColumn}{headerRow}:{LastColumn}";
    }
}
