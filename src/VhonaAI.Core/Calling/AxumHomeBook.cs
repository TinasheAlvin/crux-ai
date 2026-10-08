using VhonaAI.Core.Entities;

namespace VhonaAI.Core.Calling;

/// <summary>
/// Axum Home, a fictional mid sized cleaning products distributor.
/// Demo order days stay exactly as stored. Earlier months are filled with generated orders
/// so each complete month has a similar volume. Generated rows say so in the description.
/// The newest countable invoice is 13 April 2026, matching the newest day in the demo.
/// </summary>
public static class AxumHomeBook
{
    public const string LateCustomer = "Kimberley Cash Store";
    public const string StoppedCustomer = "Gaborone Wholesale";
    public const string DroppedCustomer = "Bloemfontein Pantry";
    public const string LateInvoiceRowId = "axum-i-kimberley-20260316";
    public const string GeneratedMark = "generated";

    public static readonly DateOnly AsAt = new(2026, 4, 13);
    public const int NextTwoDayUnits = AxumDemoFigures.NextTwoDayUnits;
    public const decimal OpeningCash = 1_700_000m;
    public const decimal FebruaryCash = 3_555_000m;

    public static readonly IReadOnlyDictionary<string, decimal> WholesalePrices =
        new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["BC170 Bathroom cleaner"] = 8m,
            ["BC290 Bathroom cleaner refill"] = 6m,
            ["CC150 Carpet cleaner"] = 10m,
            ["DS180 Disinfectant liquid"] = 7m,
            ["DS310 Disinfectant spray"] = 8m,
            ["DW190 Dishwashing liquid"] = 6m,
            ["DW320 Dishwashing liquid refill"] = 6m,
            ["FC150 Floor cleaner citrus"] = 7m,
            ["FC330 Floor cleaner"] = 9m,
            ["GC120 Glass cleaner"] = 7m,
            ["GC210 Glass cleaner lemon"] = 8m,
            ["GC260 Glass cleaner refill"] = 6m,
            ["HS160 Hand soap"] = 6m,
            ["HS280 Hand soap refill"] = 6m,
            ["KD140 Kitchen degreaser"] = 14m,
            ["KD260 Kitchen degreaser spray"] = 11m,
            ["LD250 Laundry detergent"] = 6m,
            ["LD410 Laundry detergent pods"] = 18m,
            ["MP130 Multi purpose cleaner"] = 8m,
            ["MP270 Multi purpose spray"] = 10m,
            ["OC210 Oven cleaner"] = 22m,
            ["SC140 Surface cleaner"] = 6m,
            ["SC280 Surface cleaner concentrate"] = 24m,
            ["TC110 Toilet cleaner"] = 8m,
            ["TC240 Toilet cleaner gel"] = 10m
        };

    /// <summary>Mild seasonal shape for May 2025 through March 2026, relative to March.</summary>
    private static readonly decimal[] Seasonal =
    [
        0.88m, 0.93m, 0.97m, 0.95m, 0.90m, 0.92m, 0.96m, 0.98m, 0.86m, 0.94m, 1.00m
    ];

    private static readonly decimal[] MonthEndCash =
    [
        1_880_000m,
        2_050_000m,
        2_240_000m,
        2_415_000m,
        2_610_000m,
        2_790_000m,
        2_990_000m,
        3_165_000m,
        3_355_000m,
        FebruaryCash,
        3_740_000m,
        3_440_000m
    ];

    private static readonly decimal[] StockShares =
    [
        0.58m, 0.60m, 0.57m, 0.61m, 0.59m, 0.62m, 0.56m, 0.60m, 0.63m, 0.59m, 0.61m, 0.58m
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
                    if (history.Count == 0 && units > 0 && monthStart >= new DateOnly(2026, 3, 1))
                    {
                        var saleDate = new DateOnly(monthStart.Year, monthStart.Month, 10);
                        bank.Add(Sale(region, $"axum-s-{slug}-{skuIndex:00}-{saleDate:yyyyMMdd}", saleDate, sku, units, amount));
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

        customers.Add(Kimberley(bank));
        customers.Add(Gaborone(bank));
        customers.Add(Bloemfontein(bank));
        AddGenerated(bank);
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
            Transactions = SampleBank.NumberAndBalance(bank, OpeningCash)
        };
    }

    private static WhoToCallCustomerBook Kimberley(List<SampleBankRow> bank)
    {
        const string sku = "TC110 Toilet cleaner";
        const int units = 800;
        var amount = units * Price(sku);
        var invoices = new List<WhoToCallInvoice>();
        for (var date = new DateOnly(2025, 5, 16); date <= new DateOnly(2026, 2, 16); date = date.AddMonths(1))
        {
            invoices.Add(Paid($"axum-i-kimberley-{date:yyyyMMdd}", $"AX KC {date:yyyyMM}", date, amount, sku, "Northern Cape, Kimberley Area"));
            bank.Add(Sale(LateCustomer, $"axum-s-kimberley-{date:yyyyMMdd}", date, sku, units, amount));
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
        invoices.Add(Open(
            "axum-i-kimberley-20260406",
            "AX KC 0406",
            new DateOnly(2026, 4, 6),
            new DateOnly(2026, 4, 27),
            amount,
            sku,
            "Northern Cape, Kimberley Area"));
        bank.Add(Sale(LateCustomer, "axum-s-kimberley-20260406", new DateOnly(2026, 4, 6), sku, units, amount));
        return Person(LateCustomer, "axum-c-kimberley", invoices, "0825550174", "thabo@kimberleycash.example", "Thabo Molefe");
    }

    private static WhoToCallCustomerBook Gaborone(List<SampleBankRow> bank)
    {
        const string sku = "MP130 Multi purpose cleaner";
        const int units = 400;
        var amount = units * Price(sku);
        var invoices = new List<WhoToCallInvoice>();
        for (var date = new DateOnly(2025, 5, 8); date <= new DateOnly(2026, 1, 8); date = date.AddMonths(1))
        {
            invoices.Add(Paid($"axum-i-gaborone-{date:yyyyMMdd}", $"AX GW {date:yyyyMM}", date, amount, sku, "Botswana"));
            bank.Add(Sale(StoppedCustomer, $"axum-s-gaborone-{date:yyyyMMdd}", date, sku, units, amount));
        }

        return Person(StoppedCustomer, "axum-c-gaborone", invoices, "0825550162", "amo@gaboronewholesale.example", "Amogelang Dube");
    }

    private static WhoToCallCustomerBook Bloemfontein(List<SampleBankRow> bank)
    {
        const string sku = "SC140 Surface cleaner";
        var full = 900 * Price(sku);
        var reduced = 300 * Price(sku);
        var invoices = new List<WhoToCallInvoice>();
        for (var date = new DateOnly(2025, 5, 20); date <= new DateOnly(2026, 2, 20); date = date.AddMonths(1))
        {
            invoices.Add(Paid($"axum-i-bloem-{date:yyyyMMdd}", $"AX BP {date:yyyyMM}", date, full, sku, "Free State, Bloemfontein Area"));
            bank.Add(Sale(DroppedCustomer, $"axum-s-bloem-{date:yyyyMMdd}", date, sku, 900, full));
        }

        invoices.Add(Paid("axum-i-bloem-20260320", "AX BP 202603", new DateOnly(2026, 3, 20), reduced, sku, "Free State, Bloemfontein Area"));
        bank.Add(Sale(DroppedCustomer, "axum-s-bloem-20260320", new DateOnly(2026, 3, 20), sku, 300, reduced));
        invoices.Add(Open(
            "axum-i-bloem-20260413",
            "AX BP 202604",
            AsAt,
            new DateOnly(2026, 4, 28),
            reduced,
            sku,
            "Free State, Bloemfontein Area"));
        bank.Add(Sale(DroppedCustomer, "axum-s-bloem-20260413", AsAt, sku, 300, reduced));
        return Person(DroppedCustomer, "axum-c-bloem", invoices, "0825550198", "elize@bloemfonteinpantry.example", "Elize Venter");
    }

    private static void AddGenerated(List<SampleBankRow> bank)
    {
        var stockists = Stockists();
        var weights = stockists.Select(stockist => 80 + ((stockist.Number * 17) % 40)).ToArray();
        var weightSum = weights.Sum();
        var march = new DateOnly(2026, 3, 1);
        var marchRevenue = Revenue(bank, march);
        for (var month = 0; month < Seasonal.Length - 1; month++)
        {
            var start = new DateOnly(2025, 5, 1).AddMonths(month);
            var target = decimal.Round(marchRevenue * Seasonal[month], 0, MidpointRounding.AwayFromZero);
            var gap = target - Revenue(bank, start);
            if (gap <= 1000m)
            {
                continue;
            }

            decimal placed = 0;
            for (var i = 0; i < stockists.Count; i++)
            {
                var share = i == stockists.Count - 1
                    ? gap - placed
                    : decimal.Round(gap * weights[i] / weightSum, 0, MidpointRounding.AwayFromZero);
                if (share <= 0)
                {
                    continue;
                }

                var stockist = stockists[i];
                var price = Price(stockist.Sku);
                var units = Math.Max(1, (int)decimal.Round(share / price, 0, MidpointRounding.AwayFromZero));
                var amount = units * price;
                var date = new DateOnly(start.Year, start.Month, stockist.Day);
                bank.Add(new SampleBankRow(
                    $"axum-g-{stockist.Number:00}-{date:yyyyMMdd}",
                    date,
                    SampleBank.Booked(date),
                    $"{stockist.Sku}, {units} units, {GeneratedMark}",
                    amount,
                    "Sales",
                    stockist.Region));
                placed += amount;
            }
        }

        ReshapeFebruary(bank);
    }

    /// <summary>
    /// February's generated orders follow March by region, apart from two areas that carry the increase.
    /// The month totals stay the same. Bloemfontein Pantry is left as the real drop.
    /// </summary>
    private static void ReshapeFebruary(List<SampleBankRow> bank)
    {
        var february = new DateOnly(2026, 2, 1);
        var march = new DateOnly(2026, 3, 1);
        var generated = bank
            .Where(row => row.RowId.StartsWith("axum-g-", StringComparison.Ordinal)
                && row.Date.Year == february.Year
                && row.Date.Month == february.Month)
            .ToList();
        if (generated.Count == 0)
        {
            return;
        }

        var drivers = new HashSet<string>(StringComparer.Ordinal) { "Northern Area", "Eastern Cape" };
        var pool = generated.Sum(row => row.Amount);
        var amounts = new Dictionary<string, decimal>(StringComparer.Ordinal);
        decimal used = 0;
        foreach (var row in generated)
        {
            if (drivers.Contains(row.Counterparty))
            {
                continue;
            }

            var price = Price(SkuOf(row));
            var wanted = RevenueFor(bank, march, row.Counterparty) - Floor(bank, february, row.Counterparty, row.RowId);
            var units = wanted <= 0 ? 0 : (int)decimal.Round(wanted / price, 0, MidpointRounding.AwayFromZero);
            var amount = units * price;
            amounts[row.RowId] = amount;
            used += amount;
        }

        var driverPool = pool - used;
        if (driverPool % 2 != 0)
        {
            var nudge = generated.First(row => !drivers.Contains(row.Counterparty) && Price(SkuOf(row)) % 2 == 1);
            var price = Price(SkuOf(nudge));
            amounts[nudge.RowId] += price;
            driverPool -= price;
        }

        var eastern = generated.Single(row => row.Counterparty == "Eastern Cape");
        var northern = generated.Single(row => row.Counterparty == "Northern Area");
        var easternPrice = Price(SkuOf(eastern));
        var northernPrice = Price(SkuOf(northern));
        var easternTarget = RevenueFor(bank, march, eastern.Counterparty) - 137_000m;
        var easternUnits = (int)decimal.Round(easternTarget / easternPrice, 0, MidpointRounding.AwayFromZero);
        decimal easternAmount = 0;
        for (var step = 0; step < 40; step++)
        {
            var shift = step % 2 == 0 ? step / 2 : -(step / 2 + 1);
            var trial = (easternUnits + shift) * easternPrice;
            var rest = driverPool - trial;
            if (trial > 0 && rest > 0 && rest % northernPrice == 0)
            {
                easternAmount = trial;
                break;
            }
        }

        if (easternAmount == 0)
        {
            throw new InvalidOperationException("February could not be reshaped without changing the month total.");
        }

        amounts[eastern.RowId] = easternAmount;
        amounts[northern.RowId] = driverPool - easternAmount;
        var dust = generated
            .Where(row => !drivers.Contains(row.Counterparty))
            .Sum(row => RevenueFor(bank, march, row.Counterparty) - Floor(bank, february, row.Counterparty, row.RowId) - amounts[row.RowId]);
        var donor = generated.FirstOrDefault(row => !drivers.Contains(row.Counterparty) && Price(SkuOf(row)) == 10m && amounts[row.RowId] >= 10m);
        if (donor is not null && dust == -10m)
        {
            amounts[donor.RowId] -= 10m;
            amounts[northern.RowId] += 10m;
        }

        if (amounts.Values.Sum() != pool)
        {
            throw new InvalidOperationException("February reshape changed the month total.");
        }

        for (var i = 0; i < bank.Count; i++)
        {
            if (!amounts.TryGetValue(bank[i].RowId, out var amount))
            {
                continue;
            }

            var sku = SkuOf(bank[i]);
            var units = (int)(amount / Price(sku));
            bank[i] = bank[i] with
            {
                Amount = amount,
                Description = $"{sku}, {units} units, {GeneratedMark}"
            };
        }
    }

    private static string SkuOf(SampleBankRow row)
    {
        var comma = row.Description.IndexOf(',');
        return comma < 0 ? row.Description : row.Description[..comma];
    }

    private static decimal RevenueFor(List<SampleBankRow> bank, DateOnly month, string counterparty) =>
        bank.Where(row => row.Date.Year == month.Year && row.Date.Month == month.Month && row.Amount > 0 && row.Counterparty == counterparty)
            .Sum(row => row.Amount);

    private static decimal Floor(List<SampleBankRow> bank, DateOnly month, string counterparty, string generatedRowId) =>
        bank.Where(row => row.Date.Year == month.Year
                && row.Date.Month == month.Month
                && row.Amount > 0
                && row.Counterparty == counterparty
                && row.RowId != generatedRowId)
            .Sum(row => row.Amount);

    private readonly record struct Stockist(int Number, string Region, string Sku, int Day);

    private static List<Stockist> Stockists()
    {
        var list = new List<Stockist>();
        var number = 0;
        foreach (var region in AxumDemoFigures.Regions)
        {
            number++;
            if (SpecialRegions.Contains(region))
            {
                continue;
            }

            list.Add(new Stockist(number, region, RegionSkus(region, number)[0], 8 + (number % 18)));
        }

        return list;
    }

    private static decimal Revenue(List<SampleBankRow> bank, DateOnly month) =>
        bank.Where(row => row.Date.Year == month.Year && row.Date.Month == month.Month && row.Amount > 0).Sum(row => row.Amount);

    private static void AddCosts(List<SampleBankRow> bank)
    {
        var previous = OpeningCash;
        for (var month = 0; month < 12; month++)
        {
            var start = new DateOnly(2025, 5, 1).AddMonths(month);
            var revenue = Revenue(bank, start);
            var day = start.Year == 2026 && start.Month == 4 ? 13 : 28;
            var date = new DateOnly(start.Year, start.Month, day);
            var stock = decimal.Round(revenue * StockShares[month], 0, MidpointRounding.AwayFromZero);
            var wages = start.Year < 2026 ? 860_000m : 890_000m;
            if (start.Month == 12)
            {
                wages += 120_000m;
            }

            var rent = 198_000m;
            if (start.Year == 2026 && start.Month == 4)
            {
                wages = decimal.Round(wages * 13m / 30m, 0, MidpointRounding.AwayFromZero);
                rent = decimal.Round(rent * 13m / 30m, 0, MidpointRounding.AwayFromZero);
            }

            var profit = MonthEndCash[month] - previous;
            var delivery = revenue - stock - wages - rent - profit;
            if (delivery <= 0)
            {
                throw new InvalidOperationException($"Delivery for {start:yyyy-MM} is {delivery}.");
            }

            AddCost(bank, date, "stock", "Stock purchases", stock, "Packaging plant");
            AddCost(bank, date, "delivery", "Delivery", delivery, "Route delivery");
            AddCost(bank, date, "wages", "Wages", wages, "Payroll");
            AddCost(bank, date, "rent", "Rent", rent, "Landlord");
            previous = MonthEndCash[month];
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
