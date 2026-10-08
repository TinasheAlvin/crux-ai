using VhonaAI.Core.Entities;

namespace VhonaAI.Core.Calling;

/// <summary>
/// Axum Home, a fictional cleaning products supplier.
/// Order history units are unchanged. Invoices and till rows bill those units at a wholesale price.
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
    public const decimal FebruaryCash = 820_000m;

    public static readonly IReadOnlyDictionary<string, decimal> WholesalePrices =
        new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["BC170 Bathroom cleaner"] = 34m,
            ["BC290 Bathroom cleaner refill"] = 28m,
            ["CC150 Carpet cleaner"] = 68m,
            ["DS180 Disinfectant liquid"] = 48m,
            ["DS310 Disinfectant spray"] = 55m,
            ["DW190 Dishwashing liquid"] = 42m,
            ["DW320 Dishwashing liquid refill"] = 38m,
            ["FC150 Floor cleaner citrus"] = 40m,
            ["FC330 Floor cleaner"] = 46m,
            ["GC120 Glass cleaner"] = 32m,
            ["GC210 Glass cleaner lemon"] = 36m,
            ["GC260 Glass cleaner refill"] = 24m,
            ["HS160 Hand soap"] = 22m,
            ["HS280 Hand soap refill"] = 18m,
            ["KD140 Kitchen degreaser"] = 52m,
            ["KD260 Kitchen degreaser spray"] = 44m,
            ["LD250 Laundry detergent"] = 85m,
            ["LD410 Laundry detergent pods"] = 120m,
            ["MP130 Multi purpose cleaner"] = 30m,
            ["MP270 Multi purpose spray"] = 38m,
            ["OC210 Oven cleaner"] = 64m,
            ["SC140 Surface cleaner"] = 36m,
            ["SC280 Surface cleaner concentrate"] = 95m,
            ["TC110 Toilet cleaner"] = 28m,
            ["TC240 Toilet cleaner gel"] = 33m
        };

    private static readonly decimal[] StockShares =
    [
        0.58m, 0.62m, 0.55m, 0.64m, 0.60m, 0.57m, 0.65m, 0.59m, 0.63m, 0.56m, 0.61m, 0.58m
    ];

    private static readonly HashSet<string> SpecialRegions = new(StringComparer.Ordinal)
    {
        "Botswana",
        "Free State, Bloemfontein Area",
        "Northern Cape, Kimberley Area"
    };

    public static decimal Price(string sku) => WholesalePrices[sku];

    public static SampleLedger Build()
    {
        var bank = new List<SampleBankRow>();
        var monthUnits = new Dictionary<DateOnly, int>();
        var index = 0;
        foreach (var row in AxumDemoFigures.History)
        {
            var amount = row.Units * Price(row.Sku);
            bank.Add(new SampleBankRow(
                $"axum-h-{index:0000}",
                row.Day,
                SampleBank.Booked(row.Day),
                $"{row.Sku}, {row.Units} units",
                amount,
                "Sales",
                row.Region));
            AddUnits(monthUnits, row.Day, row.Units);
            index++;
        }

        var customers = new List<WhoToCallCustomerBook>();
        string? creditInvoice = null;
        var regionNumber = 0;
        foreach (var region in AxumDemoFigures.Regions)
        {
            regionNumber++;
            if (SpecialRegions.Contains(region))
            {
                continue;
            }

            var skus = RegionSkus(region, regionNumber);
            var slug = regionNumber.ToString("00");
            var invoices = new List<WhoToCallInvoice>();
            for (var month = 0; month < 12; month++)
            {
                var monthStart = new DateOnly(2025, 5, 1).AddMonths(month);
                var history = AxumDemoFigures.History
                    .Where(row => row.Region == region && row.Day.Year == monthStart.Year && row.Day.Month == monthStart.Month)
                    .ToList();
                var lines = new List<WhoToCallLine>();
                decimal total = 0;
                var skuIndex = 0;
                foreach (var sku in skus)
                {
                    var units = history.Count > 0
                        ? history.Where(row => row.Sku == sku).Sum(row => row.Units)
                        : SteadyUnits(regionNumber, skuIndex);
                    var amount = units * Price(sku);
                    total += amount;
                    var rowId = $"axum-i-{slug}-{monthStart:yyyyMM}l{skuIndex}";
                    lines.Add(new WhoToCallLine { RowId = rowId, Description = $"{sku}, {region}", Amount = amount });
                    if (history.Count == 0 && units > 0)
                    {
                        var saleDate = new DateOnly(monthStart.Year, monthStart.Month, 10);
                        bank.Add(Sale(region, $"axum-s-{slug}-{skuIndex:00}-{saleDate:yyyyMMdd}", saleDate, sku, units, amount));
                        AddUnits(monthUnits, saleDate, units);
                    }

                    skuIndex++;
                }

                var invoiceDate = history.Count > 0
                    ? history.Max(row => row.Day)
                    : new DateOnly(monthStart.Year, monthStart.Month, 10);
                var invoiceId = $"axum-i-{slug}-{invoiceDate:yyyyMMdd}";
                invoices.Add(Paid(invoiceId, $"AX {regionNumber:00} {invoiceDate:yyyyMM}", invoiceDate, total, lines));
            }

            if (creditInvoice is null)
            {
                creditInvoice = invoices[0].RowId;
                invoices.Add(Void(
                    "axum-i-void-bottles",
                    "AX VOID 1",
                    new DateOnly(2026, 3, 2),
                    2400m,
                    $"Damaged delivery, {AxumDemoFigures.Regions[regionNumber - 1]}"));
            }

            customers.Add(Person(region, "axum-c-" + slug, invoices, $"082600{regionNumber:0000}", $"orders{regionNumber}@axumhome.example"));
        }

        customers.Add(Kimberley(bank, monthUnits));
        customers.Add(Gaborone(bank, monthUnits));
        customers.Add(Bloemfontein(bank, monthUnits));
        AddCosts(bank, monthUnits);

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

        var throughFebruary = bank.Where(row => row.Date < new DateOnly(2026, 3, 1)).Sum(row => row.Amount);
        var opening = FebruaryCash - throughFebruary;

        return new SampleLedger
        {
            BookId = SampleBookCatalog.AxumId,
            DisplayName = "Axum Home",
            AsAt = AsAt,
            Customers = customers,
            CreditNotes = notes,
            Transactions = SampleBank.NumberAndBalance(bank, opening)
        };
    }

    private static WhoToCallCustomerBook Kimberley(List<SampleBankRow> bank, Dictionary<DateOnly, int> monthUnits)
    {
        const string sku = "TC110 Toilet cleaner";
        const int units = 800;
        var amount = units * Price(sku);
        var invoices = new List<WhoToCallInvoice>();
        for (var date = new DateOnly(2025, 5, 16); date <= new DateOnly(2026, 2, 16); date = date.AddMonths(1))
        {
            invoices.Add(Paid($"axum-i-kimberley-{date:yyyyMMdd}", $"AX KC {date:yyyyMM}", date, amount, sku, "Northern Cape, Kimberley Area"));
            bank.Add(Sale(LateCustomer, $"axum-s-kimberley-{date:yyyyMMdd}", date, sku, units, amount));
            AddUnits(monthUnits, date, units);
        }

        invoices.Add(Open(
            LateInvoiceRowId,
            "AX KC 0316",
            new DateOnly(2026, 3, 16),
            new DateOnly(2026, 3, 30),
            amount,
            sku,
            "Northern Cape, Kimberley Area"));
        bank.Add(Sale(LateCustomer, "axum-s-kimberley-20260316", new DateOnly(2026, 3, 16), sku, units, amount));
        AddUnits(monthUnits, new DateOnly(2026, 3, 16), units);
        invoices.Add(Open(
            "axum-i-kimberley-20260406",
            "AX KC 0406",
            new DateOnly(2026, 4, 6),
            new DateOnly(2026, 4, 27),
            amount,
            sku,
            "Northern Cape, Kimberley Area"));
        bank.Add(Sale(LateCustomer, "axum-s-kimberley-20260406", new DateOnly(2026, 4, 6), sku, units, amount));
        AddUnits(monthUnits, new DateOnly(2026, 4, 6), units);
        return Person(LateCustomer, "axum-c-kimberley", invoices, "0825550174", "thabo@kimberleycash.example", "Thabo Molefe");
    }

    private static WhoToCallCustomerBook Gaborone(List<SampleBankRow> bank, Dictionary<DateOnly, int> monthUnits)
    {
        const string sku = "MP130 Multi purpose cleaner";
        const int units = 400;
        var amount = units * Price(sku);
        var invoices = new List<WhoToCallInvoice>();
        for (var date = new DateOnly(2025, 5, 8); date <= new DateOnly(2026, 1, 8); date = date.AddMonths(1))
        {
            invoices.Add(Paid($"axum-i-gaborone-{date:yyyyMMdd}", $"AX GW {date:yyyyMM}", date, amount, sku, "Botswana"));
            bank.Add(Sale(StoppedCustomer, $"axum-s-gaborone-{date:yyyyMMdd}", date, sku, units, amount));
            AddUnits(monthUnits, date, units);
        }

        return Person(StoppedCustomer, "axum-c-gaborone", invoices, "0825550162", "amo@gaboronewholesale.example", "Amogelang Dube");
    }

    private static WhoToCallCustomerBook Bloemfontein(List<SampleBankRow> bank, Dictionary<DateOnly, int> monthUnits)
    {
        const string sku = "SC140 Surface cleaner";
        var full = 900 * Price(sku);
        var reduced = 300 * Price(sku);
        var invoices = new List<WhoToCallInvoice>();
        for (var date = new DateOnly(2025, 5, 20); date <= new DateOnly(2026, 2, 20); date = date.AddMonths(1))
        {
            invoices.Add(Paid($"axum-i-bloem-{date:yyyyMMdd}", $"AX BP {date:yyyyMM}", date, full, sku, "Free State, Bloemfontein Area"));
            bank.Add(Sale(DroppedCustomer, $"axum-s-bloem-{date:yyyyMMdd}", date, sku, 900, full));
            AddUnits(monthUnits, date, 900);
        }

        invoices.Add(Paid("axum-i-bloem-20260320", "AX BP 202603", new DateOnly(2026, 3, 20), reduced, sku, "Free State, Bloemfontein Area"));
        bank.Add(Sale(DroppedCustomer, "axum-s-bloem-20260320", new DateOnly(2026, 3, 20), sku, 300, reduced));
        AddUnits(monthUnits, new DateOnly(2026, 3, 20), 300);
        invoices.Add(Open(
            "axum-i-bloem-20260413",
            "AX BP 202604",
            AsAt,
            new DateOnly(2026, 4, 28),
            reduced,
            sku,
            "Free State, Bloemfontein Area"));
        bank.Add(Sale(DroppedCustomer, "axum-s-bloem-20260413", AsAt, sku, 300, reduced));
        AddUnits(monthUnits, AsAt, 300);
        return Person(DroppedCustomer, "axum-c-bloem", invoices, "0825550198", "elize@bloemfonteinpantry.example", "Elize Venter");
    }

    private static void AddCosts(List<SampleBankRow> bank, Dictionary<DateOnly, int> monthUnits)
    {
        for (var month = 0; month < 12; month++)
        {
            var start = new DateOnly(2025, 5, 1).AddMonths(month);
            var revenue = bank
                .Where(row => row.Date.Year == start.Year && row.Date.Month == start.Month && row.Amount > 0)
                .Sum(row => row.Amount);
            var units = monthUnits.TryGetValue(start, out var counted) ? counted : 0;
            var day = start.Year == 2026 && start.Month == 4
                ? 13
                : Math.Min(28, DateTime.DaysInMonth(start.Year, start.Month));
            var date = new DateOnly(start.Year, start.Month, day);
            var stock = decimal.Round(revenue * StockShares[month], 0, MidpointRounding.AwayFromZero);
            var delivery = 24_000m + decimal.Round(units * 1.40m, 0, MidpointRounding.AwayFromZero);
            var wages = start.Year < 2026 ? 168_000m : 176_000m;
            const decimal rent = 92_500m;
            AddCost(bank, date, "stock", "Stock purchases", stock, "Packaging plant");
            AddCost(bank, date, "delivery", "Delivery", delivery, "Route delivery");
            AddCost(bank, date, "wages", "Wages", wages, "Payroll");
            AddCost(bank, date, "rent", "Rent", rent, "Landlord");
        }
    }

    private static void AddCost(List<SampleBankRow> bank, DateOnly date, string kind, string category, decimal amount, string counterparty)
    {
        if (amount <= 0)
        {
            return;
        }

        bank.Add(new SampleBankRow(
            $"axum-{kind}-{date:yyyyMM}",
            date,
            date,
            category,
            -amount,
            category,
            counterparty));
    }

    private static List<string> RegionSkus(string region, int regionNumber)
    {
        var skus = AxumDemoFigures.History
            .Where(row => row.Region == region)
            .Select(row => row.Sku)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(sku => sku, StringComparer.Ordinal)
            .ToList();
        if (skus.Count == 0)
        {
            skus.Add(AxumDemoFigures.Skus[(regionNumber - 1) % AxumDemoFigures.Skus.Length]);
        }

        return skus;
    }

    private static int SteadyUnits(int regionNumber, int skuIndex) => 180 + ((regionNumber * 17 + skuIndex * 13) % 400);

    private static void AddUnits(Dictionary<DateOnly, int> monthUnits, DateOnly date, int units)
    {
        var key = new DateOnly(date.Year, date.Month, 1);
        monthUnits[key] = monthUnits.TryGetValue(key, out var existing) ? existing + units : units;
    }

    private static SampleBankRow Sale(string counterparty, string rowId, DateOnly date, string sku, int units, decimal amount) => new(
        rowId,
        date,
        SampleBank.Booked(date),
        $"{sku}, {units} units",
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

    private static WhoToCallInvoice Paid(string rowId, string number, DateOnly date, decimal amount, string sku, string region) =>
        Paid(rowId, number, date, amount, [Line(rowId, $"{sku}, {region}", amount)]);

    private static WhoToCallInvoice Paid(string rowId, string number, DateOnly date, decimal amount, IReadOnlyList<WhoToCallLine> lines) => new()
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
        Lines = lines
    };

    private static WhoToCallInvoice Open(string rowId, string number, DateOnly date, DateOnly due, decimal amount, string sku, string region) => new()
    {
        RowId = rowId,
        Number = number,
        InvoiceDate = date,
        DueDate = due,
        Amount = amount,
        AmountDue = amount,
        Status = InvoiceStatus.Open,
        Terms = "21 days",
        Lines = [Line(rowId, $"{sku}, {region}", amount)]
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
