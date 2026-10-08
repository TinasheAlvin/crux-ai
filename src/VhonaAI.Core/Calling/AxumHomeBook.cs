using VhonaAI.Core.Entities;

namespace VhonaAI.Core.Calling;

/// <summary>
/// Axum Home, a fictional cleaning products supplier.
/// Daily order history is copied onto bank rows at one rand per unit. Stockists are invoiced monthly.
/// The newest countable invoice is 13 April 2026, matching the newest day in the demo.
/// </summary>
public static class AxumHomeBook
{
    public const string LateCustomer = "Kimberley Cash Store";
    public const string StoppedCustomer = "Gaborone Wholesale";
    public const string DroppedCustomer = "Bloemfontein Pantry";
    public const string LateInvoiceRowId = "axum-i-kimberley-20260316";

    public static readonly DateOnly AsAt = new(2026, 4, 13);
    public const int NextTwoDayUnits = AxumDemoFigures.NextTwoDayUnits;

    private static readonly HashSet<string> SpecialRegions = new(StringComparer.Ordinal)
    {
        "Botswana",
        "Free State, Bloemfontein Area",
        "Northern Cape, Kimberley Area"
    };

    public static SampleLedger Build()
    {
        var bank = new List<SampleBankRow>();
        var index = 0;
        foreach (var row in AxumDemoFigures.History)
        {
            bank.Add(new SampleBankRow(
                $"axum-h-{index:0000}",
                row.Day,
                SampleBank.Booked(row.Day),
                $"{row.Sku}, {row.Units} units",
                row.Units,
                "Sales",
                row.Region));
            index++;
        }

        var customers = new List<WhoToCallCustomerBook>();
        string? creditInvoice = null;
        var skuIndex = 0;
        var regionNumber = 0;
        foreach (var region in AxumDemoFigures.Regions)
        {
            regionNumber++;
            if (SpecialRegions.Contains(region))
            {
                continue;
            }

            var sku = AxumDemoFigures.Skus[skuIndex % AxumDemoFigures.Skus.Length];
            skuIndex++;
            var slug = regionNumber.ToString("00");
            var amount = Steady(region);
            var invoices = new List<WhoToCallInvoice>();
            for (var month = 0; month < 12; month++)
            {
                var date = new DateOnly(2025, 5, 10).AddMonths(month);
                var rowId = $"axum-i-{slug}-{date:yyyyMMdd}";
                invoices.Add(Paid(rowId, $"AX {skuIndex:00} {date:yyyyMM}", date, amount, $"{sku}, {region}"));
                if (!HasHistory(region, date.Year, date.Month))
                {
                    bank.Add(Sale(region, slug, date, amount));
                }
            }

            if (creditInvoice is null)
            {
                creditInvoice = invoices[0].RowId;
                invoices.Add(Void(
                    "axum-i-void-bottles",
                    "AX VOID 1",
                    new DateOnly(2026, 3, 2),
                    2400m,
                    $"Damaged delivery, {region}"));
            }

            customers.Add(Person(region, "axum-c-" + slug, invoices, $"082600{skuIndex:0000}", $"orders{skuIndex}@axumhome.example"));
        }

        customers.Add(Kimberley(bank));
        customers.Add(Gaborone(bank));
        customers.Add(Bloemfontein(bank));
        AddCosts(bank);

        var notes = new List<SampleCreditNote>();
        if (creditInvoice is not null)
        {
            notes.Add(new SampleCreditNote
            {
                RowId = "axum-cn-bottles",
                InvoiceRowId = creditInvoice,
                Number = "AX CN 3",
                IssuedDate = new DateOnly(2025, 5, 28),
                Amount = 450m,
                Reason = "Damaged bottles"
            });
        }

        return new SampleLedger
        {
            BookId = SampleBookCatalog.AxumId,
            DisplayName = "Axum Home",
            AsAt = AsAt,
            Customers = customers,
            CreditNotes = notes,
            Transactions = SampleBank.NumberAndBalance(bank, 100000m)
        };
    }

    private static WhoToCallCustomerBook Kimberley(List<SampleBankRow> bank)
    {
        var invoices = new List<WhoToCallInvoice>();
        for (var date = new DateOnly(2025, 5, 16); date <= new DateOnly(2026, 2, 16); date = date.AddMonths(1))
        {
            var rowId = $"axum-i-kimberley-{date:yyyyMMdd}";
            invoices.Add(Paid(rowId, $"AX KC {date:yyyyMM}", date, 22000m, "TC110 Toilet cleaner, Northern Cape, Kimberley Area"));
            bank.Add(Sale("Kimberley Cash Store", "kimberley", date, 22000m));
        }

        invoices.Add(Open(
            LateInvoiceRowId,
            "AX KC 0316",
            new DateOnly(2026, 3, 16),
            new DateOnly(2026, 3, 30),
            22000m,
            "TC110 Toilet cleaner, Northern Cape, Kimberley Area"));
        bank.Add(Sale("Kimberley Cash Store", "kimberley", new DateOnly(2026, 3, 16), 22000m));
        invoices.Add(Open(
            "axum-i-kimberley-20260406",
            "AX KC 0406",
            new DateOnly(2026, 4, 6),
            new DateOnly(2026, 4, 27),
            22000m,
            "TC110 Toilet cleaner, Northern Cape, Kimberley Area"));
        bank.Add(Sale("Kimberley Cash Store", "kimberley", new DateOnly(2026, 4, 6), 22000m));
        return Person(LateCustomer, "axum-c-kimberley", invoices, "0825550174", "thabo@kimberleycash.example", "Thabo Molefe");
    }

    private static WhoToCallCustomerBook Gaborone(List<SampleBankRow> bank)
    {
        var invoices = new List<WhoToCallInvoice>();
        for (var date = new DateOnly(2025, 5, 8); date <= new DateOnly(2026, 1, 8); date = date.AddMonths(1))
        {
            var rowId = $"axum-i-gaborone-{date:yyyyMMdd}";
            invoices.Add(Paid(rowId, $"AX GW {date:yyyyMM}", date, 15000m, "MP130 Multi purpose cleaner, Botswana"));
            bank.Add(Sale("Gaborone Wholesale", "gaborone", date, 15000m));
        }

        return Person(StoppedCustomer, "axum-c-gaborone", invoices, "0825550162", "amo@gaboronewholesale.example", "Amogelang Dube");
    }

    private static WhoToCallCustomerBook Bloemfontein(List<SampleBankRow> bank)
    {
        var invoices = new List<WhoToCallInvoice>();
        for (var date = new DateOnly(2025, 5, 20); date <= new DateOnly(2026, 2, 20); date = date.AddMonths(1))
        {
            var rowId = $"axum-i-bloem-{date:yyyyMMdd}";
            invoices.Add(Paid(rowId, $"AX BP {date:yyyyMM}", date, 36000m, "SC140 Surface cleaner, Free State, Bloemfontein Area"));
            bank.Add(Sale("Bloemfontein Pantry", "bloem", date, 36000m));
        }

        invoices.Add(Paid("axum-i-bloem-20260320", "AX BP 202603", new DateOnly(2026, 3, 20), 12000m, "SC140 Surface cleaner, Free State, Bloemfontein Area"));
        bank.Add(Sale("Bloemfontein Pantry", "bloem", new DateOnly(2026, 3, 20), 12000m));
        invoices.Add(Open(
            "axum-i-bloem-20260413",
            "AX BP 202604",
            AsAt,
            new DateOnly(2026, 4, 28),
            12000m,
            "SC140 Surface cleaner, Free State, Bloemfontein Area"));
        bank.Add(Sale("Bloemfontein Pantry", "bloem", AsAt, 12000m));
        return Person(DroppedCustomer, "axum-c-bloem", invoices, "0825550198", "elize@bloemfonteinpantry.example", "Elize Venter");
    }

    private static int Steady(string region)
    {
        var months = AxumDemoFigures.History
            .Where(row => row.Region == region)
            .GroupBy(row => new DateOnly(row.Day.Year, row.Day.Month, 1))
            .Select(group => group.Sum(row => row.Units))
            .ToList();
        return months.Count == 0 ? 8000 : Math.Max(8000, months.Max());
    }

    private static bool HasHistory(string region, int year, int month) =>
        AxumDemoFigures.History.Any(row => row.Region == region && row.Day.Year == year && row.Day.Month == month);

    private static void AddCosts(List<SampleBankRow> bank)
    {
        for (var month = 0; month < 12; month++)
        {
            var start = new DateOnly(2025, 5, 1).AddMonths(month);
            var revenue = bank
                .Where(row => row.Date.Year == start.Year && row.Date.Month == start.Month && row.Amount > 0)
                .Sum(row => row.Amount);
            var cost = decimal.Round(revenue * 0.4m, 0, MidpointRounding.AwayFromZero);
            if (cost <= 0)
            {
                continue;
            }

            var date = new DateOnly(start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month));
            bank.Add(new SampleBankRow(
                $"axum-cost-{date:yyyyMM}",
                date,
                date,
                "Goods and freight",
                -cost,
                "Goods and freight",
                "Suppliers"));
        }
    }

    private static SampleBankRow Sale(string counterparty, string slug, DateOnly date, decimal amount) => new(
        $"axum-s-{slug}-{date:yyyyMMdd}",
        date,
        SampleBank.Booked(date),
        "Stockist order",
        amount,
        "Sales",
        counterparty);

    private static WhoToCallCustomerBook Person(
        string name,
        string rowId,
        IReadOnlyList<WhoToCallInvoice> invoices,
        string phone,
        string email,
        string? contact = null) => new()
    {
        CustomerId = SampleIds.For(rowId),
        Name = name,
        RowId = rowId,
        ContactPerson = contact ?? "Accounts",
        Phone = phone,
        Email = email,
        IsActive = true,
        Invoices = invoices
    };

    private static WhoToCallInvoice Paid(string rowId, string number, DateOnly date, decimal amount, string description) => new()
    {
        RowId = rowId,
        Number = number,
        InvoiceDate = date,
        DueDate = date.AddDays(21),
        Amount = amount,
        AmountDue = 0,
        Status = InvoiceStatus.Paid,
        PaidDate = date,
        Terms = "21 days",
        Lines = [Line(rowId, description, amount)]
    };

    private static WhoToCallInvoice Open(string rowId, string number, DateOnly date, DateOnly due, decimal amount, string description) => new()
    {
        RowId = rowId,
        Number = number,
        InvoiceDate = date,
        DueDate = due,
        Amount = amount,
        AmountDue = amount,
        Status = InvoiceStatus.Open,
        Terms = "21 days",
        Lines = [Line(rowId, description, amount)]
    };

    private static WhoToCallInvoice Void(string rowId, string number, DateOnly date, decimal amount, string description) => new()
    {
        RowId = rowId,
        Number = number,
        InvoiceDate = date,
        DueDate = date.AddDays(21),
        Amount = amount,
        AmountDue = 0,
        Status = InvoiceStatus.Void,
        Lines = [Line(rowId, description, amount)]
    };

    private static WhoToCallLine Line(string rowId, string description, decimal amount) => new()
    {
        RowId = rowId + "l",
        Description = description,
        Amount = amount
    };
}
