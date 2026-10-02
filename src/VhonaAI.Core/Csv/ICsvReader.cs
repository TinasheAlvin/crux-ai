namespace VhonaAI.Core.Csv;

public interface ICsvReader
{
    Task<CsvTable> ReadAsync(Stream stream, CancellationToken cancellationToken = default);
}
