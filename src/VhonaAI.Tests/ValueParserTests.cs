using VhonaAI.Core.Parsing;

namespace VhonaAI.Tests;

public class ValueParserTests
{
    [Theory]
    [InlineData("2026-03-16")]
    [InlineData("16/03/2026")]
    [InlineData("16-03-2026")]
    public void Parses_common_date_formats(string raw)
    {
        Assert.True(ValueParsers.TryParseDate(raw, out var date));
        Assert.Equal(new DateOnly(2026, 3, 16), date);
    }

    [Theory]
    [InlineData("-450.00", -450.00)]
    [InlineData("R 2,450.00", 2450.00)]
    [InlineData("(89.50)", -89.50)]
    [InlineData("1 250,75", 1250.75)]
    public void Parses_amounts(string raw, double expected)
    {
        Assert.True(ValueParsers.TryParseAmount(raw, out var amount));
        Assert.Equal((decimal)expected, amount);
    }

    [Fact]
    public void Combines_debit_and_credit()
    {
        var net = ValueParsers.CombineDebitCredit("100.00", "250.00");
        Assert.Equal(150.00m, net);
    }
}

public class RowIdFactoryTests
{
    [Fact]
    public void Is_stable_for_the_same_import_line()
    {
        var importId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var first = RowIdFactory.ForImportLine(importId, 4);
        var second = RowIdFactory.ForImportLine(importId, 4);

        Assert.Equal(first, second);
        Assert.Equal("imp_aaaaaaaabbbbccccddddeeeeeeeeeeee_r4", first);
    }
}
