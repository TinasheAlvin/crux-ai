using CruxAI.Core.Mapping;

namespace CruxAI.Tests;

public class ColumnGuesserTests
{
    [Fact]
    public void Guesses_common_south_african_bank_headers()
    {
        var headers = new[] { "Txn Date", "Details", "ZAR Amount", "Category", "Client", "Ref" };

        var mapping = ColumnGuesser.Guess(headers);

        Assert.Equal(TransactionFields.Date, mapping["Txn Date"]);
        Assert.Equal(TransactionFields.Description, mapping["Details"]);
        Assert.Equal(TransactionFields.Amount, mapping["ZAR Amount"]);
        Assert.Equal(TransactionFields.Category, mapping["Category"]);
        Assert.Equal(TransactionFields.Counterparty, mapping["Client"]);
        Assert.Equal(TransactionFields.Reference, mapping["Ref"]);
    }

    [Fact]
    public void Does_not_assign_the_same_field_twice()
    {
        var mapping = ColumnGuesser.Guess(["Date", "Transaction Date", "Narrative"]);

        Assert.Equal(TransactionFields.Date, mapping["Date"]);
        Assert.Equal(TransactionFields.Ignore, mapping["Transaction Date"]);
        Assert.Equal(TransactionFields.Description, mapping["Narrative"]);
    }

    [Fact]
    public void MergeSaved_prefers_owner_overrides_when_headers_match()
    {
        var headers = new[] { "Txn Date", "Details", "ZAR Amount" };
        var guessed = ColumnGuesser.Guess(headers);
        var saved = new Dictionary<string, string>
        {
            ["Details"] = TransactionFields.Reference
        };

        var merged = ColumnGuesser.MergeSaved(headers, guessed, saved);

        Assert.Equal(TransactionFields.Reference, merged["Details"]);
        Assert.Equal(TransactionFields.Date, merged["Txn Date"]);
    }
}
