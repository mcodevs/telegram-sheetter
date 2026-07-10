using MultiCardSync.Core.Logic;
using MultiCardSync.Core.Models;
using Shouldly;
using Xunit;

namespace MultiCardSync.Core.Tests;

public class DedupKeyTests
{
    [Fact]
    public void Debit_row_uses_uuid_directly()
    {
        var tx = new MultiCardTransaction { Uuid = "abc-123", Date = "2026-07-09", AmountValue = 100m };
        DedupKey.For(tx, "debit").ShouldBe("abc-123");
    }

    [Fact]
    public void Credit_row_without_uuid_uses_stable_hash()
    {
        var tx = new MultiCardTransaction
        {
            Uuid = null,
            Date = "2026-07-09T16:00:16",
            AmountValue = 2000000m,
            Balance = 5000000m,
            Note = "ПОПОЛНЕНИЕ",
        };

        var k1 = DedupKey.For(tx, "credit");
        var k2 = DedupKey.For(tx, "credit");

        k1.ShouldStartWith("h:");
        k1.ShouldBe(k2); // bir xil yozuv → bir xil kalit (barqaror)
    }

    [Fact]
    public void Different_amount_yields_different_key()
    {
        var a = new MultiCardTransaction { Date = "2026-07-09", AmountValue = 100m, Balance = 1m, Note = "x" };
        var b = new MultiCardTransaction { Date = "2026-07-09", AmountValue = 200m, Balance = 1m, Note = "x" };

        DedupKey.For(a, "credit").ShouldNotBe(DedupKey.For(b, "credit"));
    }
}
