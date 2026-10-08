using VhonaAI.Core.Entities;

namespace VhonaAI.Core.Calling;

/// <summary>
/// The four sample customers from the who-to-call demo, as at 31 March 2026.
/// Names are made up. The latest invoice date is 31 March, which is the books' as-at date.
/// </summary>
public static class WhoToCallSample
{
    public static readonly DateOnly AsAt = new(2026, 3, 31);

    public static WhoToCallBooks Build(string businessName = "Harbour Street Studio")
    {
        var customers = new[]
        {
            Marula(),
            Kopano(),
            Tamboti(),
            Sondela()
        };
        return new WhoToCallBooks
        {
            AsAt = customers.SelectMany(customer => customer.Invoices).Max(invoice => invoice.InvoiceDate),
            BusinessName = businessName,
            Customers = customers
        };
    }

    private static WhoToCallCustomerBook Marula()
    {
        var dates = new[]
        {
            new DateOnly(2025, 8, 28),
            new DateOnly(2025, 9, 28),
            new DateOnly(2025, 10, 28),
            new DateOnly(2025, 11, 28),
            new DateOnly(2025, 12, 28),
            new DateOnly(2026, 1, 28)
        };
        return Customer(
            "Marula Ridge Office Park",
            null,
            null,
            null,
            dates.Select((date, index) => PaidInvoice(
                "marula-" + (index + 1),
                "MR-" + (index + 1),
                date,
                date.AddMonths(1),
                18400m,
                date.AddDays(10),
                [Line("marula-line-" + (index + 1), "Monthly maintenance", 18400m)])).ToList());
    }

    private static WhoToCallCustomerBook Kopano()
    {
        var months = new[]
        {
            new DateOnly(2025, 11, 30),
            new DateOnly(2025, 12, 31),
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 2, 28),
            new DateOnly(2026, 3, 31)
        };
        var invoices = new List<WhoToCallInvoice>();
        for (var index = 0; index < months.Length; index++)
        {
            var date = months[index];
            var linen = index < 3;
            var lines = new List<WhoToCallLine>();
            if (linen)
            {
                lines.Add(Line("kopano-linen-" + (index + 1), "Linen and laundry service", 6550m));
            }

            lines.Add(Line("kopano-pool-" + (index + 1), "Pool and garden", 3150m));
            var amount = lines.Sum(line => line.Amount);
            invoices.Add(PaidInvoice(
                "kopano-" + (index + 1),
                "KG-" + (index + 1),
                date,
                date,
                amount,
                date,
                lines));
        }

        return Customer("Kopano Guest Lodge", null, null, null, invoices);
    }

    private static WhoToCallCustomerBook Tamboti()
    {
        var dates = new[]
        {
            new DateOnly(2025, 12, 25),
            new DateOnly(2026, 1, 8),
            new DateOnly(2026, 1, 22),
            new DateOnly(2026, 2, 5),
            new DateOnly(2026, 2, 19)
        };
        return Customer(
            "Tamboti Clinic Rooms",
            null,
            null,
            null,
            dates.Select((date, index) => PaidInvoice(
                "tamboti-" + (index + 1),
                "TC-" + (index + 1),
                date,
                date.AddDays(14),
                2750m,
                date.AddDays(7),
                [Line("tamboti-line-" + (index + 1), "Aircon service call outs", 2750m)])).ToList());
    }

    private static WhoToCallCustomerBook Sondela()
    {
        return Customer(
            "Sondela Dental Studio",
            "Naledi Khumalo",
            "0820001111",
            "naledi@sondela.example",
            [
                PaidInvoice("sondela-2229", "INV 2229", new DateOnly(2025, 11, 14), new DateOnly(2025, 12, 14), 9850m, new DateOnly(2025, 12, 10),
                    [Line("sondela-line-2229", "Maintenance call out", 9850m)], "30 day terms"),
                PaidInvoice("sondela-2268", "INV 2268", new DateOnly(2025, 12, 8), new DateOnly(2026, 1, 7), 11200m, new DateOnly(2026, 1, 5),
                    [Line("sondela-line-2268", "Maintenance call out", 11200m)], "30 day terms"),
                OpenInvoice("sondela-2295", "INV 2295", new DateOnly(2026, 1, 13), new DateOnly(2026, 2, 12), 12400m,
                    [Line("sondela-line-2295", "Maintenance call out", 12400m)]),
                OpenInvoice("sondela-2318", "INV 2318", new DateOnly(2026, 2, 10), new DateOnly(2026, 3, 12), 8950m,
                    [Line("sondela-line-2318", "Maintenance call out", 8950m)])
            ]);
    }

    private static WhoToCallCustomerBook Customer(
        string name,
        string? contact,
        string? phone,
        string? email,
        IReadOnlyList<WhoToCallInvoice> invoices) =>
        new()
        {
            CustomerId = Guid.NewGuid(),
            Name = name,
            RowId = "sample-" + name.ToLowerInvariant().Replace(' ', '-'),
            ContactPerson = contact,
            Phone = phone,
            Email = email,
            Invoices = invoices
        };

    private static WhoToCallLine Line(string rowId, string description, decimal amount) =>
        new() { RowId = rowId, Description = description, Amount = amount };

    private static WhoToCallInvoice PaidInvoice(
        string rowId,
        string number,
        DateOnly issued,
        DateOnly due,
        decimal amount,
        DateOnly paid,
        IReadOnlyList<WhoToCallLine> lines,
        string? terms = null) =>
        new()
        {
            RowId = rowId,
            Number = number,
            InvoiceDate = issued,
            DueDate = due,
            Amount = amount,
            AmountDue = 0,
            Status = InvoiceStatus.Paid,
            PaidDate = paid,
            Terms = terms,
            ImportedAt = AsAt.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Lines = lines
        };

    private static WhoToCallInvoice OpenInvoice(
        string rowId,
        string number,
        DateOnly issued,
        DateOnly due,
        decimal amount,
        IReadOnlyList<WhoToCallLine> lines) =>
        new()
        {
            RowId = rowId,
            Number = number,
            InvoiceDate = issued,
            DueDate = due,
            Amount = amount,
            AmountDue = amount,
            Status = InvoiceStatus.Open,
            Terms = "30 day terms",
            ImportedAt = AsAt.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Lines = lines
        };
}
