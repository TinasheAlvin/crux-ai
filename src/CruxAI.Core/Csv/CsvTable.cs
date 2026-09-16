namespace CruxAI.Core.Csv;

public sealed class CsvTable
{
    public required IReadOnlyList<string> Headers { get; init; }
    public required IReadOnlyList<CsvRow> Rows { get; init; }
}

public sealed class CsvRow
{
    public required int SourceRowNumber { get; init; }
    public required IReadOnlyDictionary<string, string> Values { get; init; }
}
