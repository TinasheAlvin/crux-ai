namespace VhonaAI.Core.Calling;

/// <summary>
/// Days overdue are measured to a reference date. The product uses the end of the
/// imported books. <see cref="Today"/> is here so that choice can change later
/// without rewriting the rules.
/// </summary>
public enum AsAtSource
{
    EndOfImportedBooks = 0,
    Today = 1
}

public static class AsAtDates
{
    public static readonly AsAtSource Current = AsAtSource.EndOfImportedBooks;

    public static DateOnly Resolve(AsAtSource source, IEnumerable<DateOnly> invoiceDates, DateOnly today)
    {
        if (source == AsAtSource.Today)
        {
            return today;
        }

        DateOnly? latest = null;
        foreach (var date in invoiceDates)
        {
            if (latest is null || date > latest)
            {
                latest = date;
            }
        }

        return latest ?? today;
    }
}
