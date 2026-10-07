namespace VhonaAI.Core.Entities;

/// <summary>Where a batch of rows came from. Connector kinds are added in a later phase.</summary>
public enum DataSourceKind
{
    TransactionCsv = 0,
    InvoiceCsv = 1,
    InvoiceExcel = 2,
    TransactionExcel = 3
}
