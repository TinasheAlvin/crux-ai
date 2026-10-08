using System.Globalization;
using VhonaAI.Core.Csv;
using ClosedXML.Excel;

namespace VhonaAI.Infrastructure.Imports;

public static class ExcelSheetReader
{
    public static CsvTable Read(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("The Excel file has no worksheets.");
        var first = sheet.FirstRowUsed()
            ?? throw new InvalidDataException("The Excel file is empty.");
        var last = sheet.LastRowUsed()
            ?? throw new InvalidDataException("The Excel file is empty.");

        var headerRowNumber = first.RowNumber();
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber()
            ?? throw new InvalidDataException("The Excel file has no header row.");

        var headers = new List<string>();
        for (var column = 1; column <= lastColumn; column++)
        {
            var header = CellText(sheet.Cell(headerRowNumber, column));
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            headers.Add(header);
        }

        if (headers.Count == 0)
        {
            throw new InvalidDataException("The Excel file has no header row.");
        }

        if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Count)
        {
            throw new InvalidDataException("The Excel file has duplicate column headers.");
        }

        var headerColumns = new List<(string Header, int Column)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var column = 1; column <= lastColumn; column++)
        {
            var header = CellText(sheet.Cell(headerRowNumber, column));
            if (string.IsNullOrWhiteSpace(header) || !seen.Add(header))
            {
                continue;
            }

            headerColumns.Add((header, column));
        }

        var rows = new List<CsvRow>();
        for (var rowNumber = headerRowNumber + 1; rowNumber <= last.RowNumber(); rowNumber++)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var any = false;
            foreach (var (header, column) in headerColumns)
            {
                var value = CellText(sheet.Cell(rowNumber, column));
                values[header] = value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    any = true;
                }
            }

            if (!any)
            {
                continue;
            }

            rows.Add(new CsvRow
            {
                SourceRowNumber = rowNumber,
                Values = values
            });
        }

        if (rows.Count == 0)
        {
            throw new InvalidDataException("The Excel file has headers but no data rows.");
        }

        return new CsvTable
        {
            Headers = headers,
            Rows = rows
        };
    }

    private static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        if (cell.DataType == XLDataType.DateTime)
        {
            return DateOnly.FromDateTime(cell.GetDateTime()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (cell.DataType == XLDataType.Number)
        {
            return cell.GetValue<decimal>().ToString(CultureInfo.InvariantCulture);
        }

        return cell.GetFormattedString().Trim();
    }
}
