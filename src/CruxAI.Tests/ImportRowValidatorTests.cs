using CruxAI.Core.Csv;
using CruxAI.Core.Mapping;
using CruxAI.Core.Validation;

namespace CruxAI.Tests;

public class ImportRowValidatorTests
{
    [Fact]
    public void Flags_fixable_row_errors_and_accepts_valid_rows()
    {
        var table = new CsvTable
        {
            Headers = ["Txn Date", "Details", "ZAR Amount"],
            Rows =
            [
                new CsvRow
                {
                    SourceRowNumber = 2,
                    Values = new Dictionary<string, string>
                    {
                        ["Txn Date"] = "2026-03-02",
                        ["Details"] = "Cut and blow dry",
                        ["ZAR Amount"] = "-450.00"
                    }
                },
                new CsvRow
                {
                    SourceRowNumber = 3,
                    Values = new Dictionary<string, string>
                    {
                        ["Txn Date"] = "not-a-date",
                        ["Details"] = "",
                        ["ZAR Amount"] = "abc"
                    }
                }
            ]
        };

        var mapping = new Dictionary<string, string>
        {
            ["Txn Date"] = TransactionFields.Date,
            ["Details"] = TransactionFields.Description,
            ["ZAR Amount"] = TransactionFields.Amount
        };

        var result = ImportRowValidator.Validate(table, mapping);

        Assert.True(result[0].IsValid);
        Assert.False(result[1].IsValid);
        Assert.Contains("Date", result[1].Errors.Keys);
        Assert.Contains("Description", result[1].Errors.Keys);
        Assert.Contains("Amount", result[1].Errors.Keys);
    }

    [Fact]
    public void Applies_in_place_corrections_without_reloading_source()
    {
        var table = new CsvTable
        {
            Headers = ["Date", "Description", "Amount"],
            Rows =
            [
                new CsvRow
                {
                    SourceRowNumber = 2,
                    Values = new Dictionary<string, string>
                    {
                        ["Date"] = "bad",
                        ["Description"] = "",
                        ["Amount"] = "nope"
                    }
                }
            ]
        };

        var mapping = ColumnGuesser.Guess(table.Headers);
        var corrections = new Dictionary<int, IReadOnlyDictionary<string, string>>
        {
            [2] = new Dictionary<string, string>
            {
                [TransactionFields.Date] = "16/03/2026",
                [TransactionFields.Description] = "Walk-in cut",
                [TransactionFields.Amount] = "-180.00"
            }
        };

        var result = ImportRowValidator.Validate(table, mapping, corrections);

        var parsed = result[0].Parsed;
        Assert.NotNull(parsed);
        Assert.Equal(new DateOnly(2026, 3, 16), parsed!.Date);
        Assert.Equal(-180.00m, parsed.Amount);
    }
}
