using MultiCardSync.Core.Logic;
using MultiCardSync.Core.Models;
using Shouldly;
using Xunit;

namespace MultiCardSync.Core.Tests;

public class RowBuilderTests
{
    [Fact]
    public void Credit_maps_to_prixod_columns()
    {
        var tx = new MultiCardTransaction { Date = "2026-07-09T16:00:16", AmountValue = 2000000m, Note = "ПОПОЛНЕНИЕ" };

        var row = RowBuilder.Build(tx, "credit");

        row.Length.ShouldBe(10);
        row[0].ShouldBe("2026-07-09");           // Сана
        row[3].ShouldBe("MultiCard пополнение");  // Инфо
        row[5].ShouldBe(2000000m);                // Приход (F)
        row[6].ShouldBe("Мультисард");            // Тўлов Приход (G)
        row[7].ShouldBe("");                       // Расход bo'sh
    }

    [Fact]
    public void Debit_maps_to_rasxod_columns()
    {
        var tx = new MultiCardTransaction { Date = "2026-07-09T10:00:00", AmountValue = 50450m, Note = "" };

        var row = RowBuilder.Build(tx, "debit");

        row[5].ShouldBe("");               // Приход bo'sh
        row[7].ShouldBe(50450m);          // Расход (H)
        row[8].ShouldBe("Мультисард");    // Тўлов Расход (I)
    }

    [Fact]
    public void Note_is_appended_to_izoh_with_datetime()
    {
        var tx = new MultiCardTransaction { Date = "2026-07-09T16:00:16", AmountValue = 1m, Note = "  test  " };

        var row = RowBuilder.Build(tx, "credit");

        ((string)row[9]!).ShouldBe("2026-07-09 16:00:16 · test");
    }
}
