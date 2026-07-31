using MultiCardSync.Core.Logic;
using Shouldly;
using Xunit;

namespace MultiCardSync.Core.Tests;

/// <summary>
/// Jadval sarlavhasini topish — append diapazoni shunga bog'lanadi. Noto'g'ri
/// topilsa qatorlar varaq tepasidagi hisobot blokiga tushib ketishi mumkin.
/// </summary>
public class TableAnchorTests
{
    /// <summary>2026-07 shabloni: tepada hisobot bloki, sarlavha 20-qatorda.</summary>
    private static readonly string?[] RealTemplateColumnB =
    [
        "КУНЛИК РАСХОД ва ПРИХОД", null, "Текшириш санаси:", null, null,
        "Ҳисоб тури (Касса / Карта)", "Нақд пул (сум)", "Мультисард", "Карта (3933)",
        "Карта (4962)", "Карта (6386)", "Карта Ислом", "Банк NOVA", "Банк Capital",
        "Банк Кейнги авлод", "Нақд пул (USD)", null, "ЖАМИ",
        "КУНЛИК ОПЕРАЦИЯЛАР КИРИТИШ БАЗАСИ", "Сана", "2026-03-01", "2026-03-01",
    ];

    [Fact]
    public void Finds_header_row_in_the_real_template()
    {
        TableAnchor.FindHeaderRow(RealTemplateColumnB).ShouldBe(20);
    }

    [Fact]
    public void Range_is_anchored_to_the_header_row()
    {
        var row = TableAnchor.FindHeaderRow(RealTemplateColumnB);

        TableAnchor.RangeFrom(row).ShouldBe("B20:K");
    }

    [Fact]
    public void Header_row_moves_when_the_summary_block_grows()
    {
        // Tepaga 3 ta qator qo'shilsa — diapazon o'zi suriladi (qat'iy raqam yo'q).
        string?[] shifted = [null, null, null, .. RealTemplateColumnB];

        var row = TableAnchor.FindHeaderRow(shifted);

        row.ShouldBe(23);
        TableAnchor.RangeFrom(row).ShouldBe("B23:K");
    }

    [Fact]
    public void Ignores_surrounding_whitespace()
    {
        TableAnchor.FindHeaderRow([null, "  Сана  "]).ShouldBe(2);
    }

    [Fact]
    public void Returns_zero_when_header_is_missing()
    {
        // Noto'g'ri varaq (masalan "Инфо") — chaqiruvchi buni aniq xatoga aylantiradi.
        TableAnchor.FindHeaderRow(["Инфо", "01 763 YMA", "01 834 XMA"]).ShouldBe(0);
    }

    [Fact]
    public void Rejects_an_invalid_header_row()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TableAnchor.RangeFrom(0));
    }
}
