using CruxAI.Core.Csv;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace CruxAI.Infrastructure.Csv;

public sealed class CsvHelperReader : ICsvReader
{
    public async Task<CsvTable> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var headerLine = await reader.ReadLineAsync(cancellationToken)
            ?? throw new InvalidDataException("The CSV file is empty.");

        var delimiter = DetectDelimiter(headerLine);
        stream.Position = 0;
        reader.DiscardBufferedData();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = null,
            DetectDelimiter = false,
            Delimiter = delimiter
        };

        using var csv = new CsvReader(reader, config);
        await csv.ReadAsync();
        csv.ReadHeader();

        var headers = (csv.HeaderRecord ?? [])
            .Select(h => (h ?? string.Empty).Trim())
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .ToList();

        if (headers.Count == 0)
        {
            throw new InvalidDataException("The CSV file has no header row.");
        }

        if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Count)
        {
            throw new InvalidDataException("The CSV file has duplicate column headers.");
        }

        var rows = new List<CsvRow>();
        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var anyValue = false;
            foreach (var header in headers)
            {
                var value = csv.GetField(header)?.Trim() ?? string.Empty;
                values[header] = value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    anyValue = true;
                }
            }

            if (!anyValue)
            {
                continue;
            }

            rows.Add(new CsvRow
            {
                SourceRowNumber = csv.Context.Parser?.Row ?? rows.Count + 2,
                Values = values
            });
        }

        if (rows.Count == 0)
        {
            throw new InvalidDataException("The CSV file has headers but no data rows.");
        }

        return new CsvTable { Headers = headers, Rows = rows };
    }

    internal static string DetectDelimiter(string headerLine)
    {
        var commas = headerLine.Count(c => c == ',');
        var semicolons = headerLine.Count(c => c == ';');
        var tabs = headerLine.Count(c => c == '\t');

        if (semicolons > commas && semicolons >= tabs)
        {
            return ";";
        }

        if (tabs > commas && tabs >= semicolons)
        {
            return "\t";
        }

        return ",";
    }
}
