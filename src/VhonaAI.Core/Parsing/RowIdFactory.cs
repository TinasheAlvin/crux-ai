namespace VhonaAI.Core.Parsing;

public static class RowIdFactory
{
    public static string ForImportLine(Guid importJobId, int sourceRowNumber)
    {
        if (sourceRowNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRowNumber));
        }

        return $"imp_{importJobId:N}_r{sourceRowNumber}";
    }
}
