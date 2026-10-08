using VhonaAI.Web;

namespace VhonaAI.Tests;

public class SavedBooksLineTests
{
    [Fact]
    public void Counts_invoices_and_transactions_together()
    {
        Assert.Equal("2 invoices and 0 transactions saved", SavedBooksLine.Format(2, 0));
        Assert.Equal("1 invoice and 1 transaction saved", SavedBooksLine.Format(1, 1));
    }

    [Fact]
    public void Hides_the_line_when_nothing_is_saved()
    {
        Assert.Null(SavedBooksLine.Format(0, 0));
    }
}
