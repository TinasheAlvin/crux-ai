using VhonaAI.Core.Entities;

namespace VhonaAI.Core.Calling;

/// <summary>
/// Karoo Kitchen Group, a fictional three branch restaurant group.
/// Till takings and costs follow the demo's published months. Invoices are corporate catering accounts.
/// No transaction is dated in July 2026, so health stays on June. A countable invoice on 13 July 2026 sets who to call.
/// </summary>
public static class KarooKitchenBook
{
    public const string LateCustomer = "Signal Hill Functions";
    public const string StoppedCustomer = "Oranjezicht Boardroom";
    public const string DroppedCustomer = "Stellenbosch Picnic Co";
    public const string LateInvoiceRowId = "karoo-i-signal-20260628";

    public static readonly DateOnly AsAt = new(2026, 7, 13);

    public const decimal June2026Revenue = 741200m;
    public const decimal June2026Food = 252000m;
    public const decimal June2026Staff = 209000m;
    public const decimal June2026Rent = 76500m;
    public const decimal June2026Utilities = 38900m;
    public const decimal June2026Marketing = 21400m;
    public const decimal June2026Other = 57300m;
    public const decimal June2026Expenses = 655100m;
    public const decimal June2026Profit = 86100m;
    public const decimal June2026Cash = 412300m;
    public const decimal May2026Revenue = 812000m;
    public const decimal June2025Revenue = 718900m;
    public const decimal June2025Expenses = 629800m;
    public const decimal GardensJune = 318400m;
    public const decimal StellenboschJune = 245700m;
    public const decimal SeaPointJune = 177100m;

    public static readonly (int Year, int Month, int Revenue)[] RevenueMonths =
    [
        (2025, 6, 718900),
        (2025, 7, 702000),
        (2025, 8, 731000),
        (2025, 9, 748000),
        (2025, 10, 802000),
        (2025, 11, 861000),
        (2025, 12, 1148000),
        (2026, 1, 976000),
        (2026, 2, 838000),
        (2026, 3, 872000),
        (2026, 4, 894000),
        (2026, 5, 812000),
        (2026, 6, 741200)
    ];

    public static SampleLedger Build()
    {
        var gardenInvoices = MonthInvoices("gardens", new DateOnly(2025, 8, 4), new DateOnly(2026, 7, 4), 12000m, "Breakfast platters, Gardens");
        var gardens = Person(
            "Gardens Hotel Breakfast",
            "gardens",
            "Naledi September",
            "0825551804",
            "naledi@gardenshotel.example",
            gardenInvoices);
        var woodstockInvoices = MonthInvoices("woodstock", new DateOnly(2025, 10, 18), new DateOnly(2026, 6, 18), 9000m, "Studio lunch, Woodstock");
        woodstockInvoices.Add(Void(
            "karoo-i-void-wedding",
            "KK VOID 1",
            new DateOnly(2026, 5, 2),
            6400m,
            "Cancelled wedding tasting"));
        var woodstock = Person(
            "Woodstock Studio Lunch",
            "woodstock",
            "James Petersen",
            "0825551818",
            "james@woodstockstudio.example",
            woodstockInvoices);

        var customers = new List<WhoToCallCustomerBook>
        {
            SignalHill(),
            Oranjezicht(),
            StellenboschPicnic(),
            gardens,
            woodstock
        };
        customers.AddRange(MoreAccounts());
        var firstGardens = gardenInvoices[0].RowId;

        var notes = new List<SampleCreditNote>
        {
            new()
            {
                RowId = "karoo-cn-gardens-dessert",
                InvoiceRowId = firstGardens,
                Number = "KK CN 14",
                IssuedDate = new DateOnly(2025, 8, 20),
                Amount = 1200m,
                Reason = "Unused dessert course"
            }
        };

        return new SampleLedger
        {
            BookId = SampleBookCatalog.KarooId,
            DisplayName = "Karoo Kitchen Group",
            AsAt = AsAt,
            Customers = customers,
            CreditNotes = notes,
            Transactions = TillAndCosts()
        };
    }

    private static IEnumerable<WhoToCallCustomerBook> MoreAccounts()
    {
        yield return Account("Kloof Street Supper Club", "kloof", "Lebo Dlamini", "0825552106", "lebo@kloofstreet.example", new DateOnly(2025, 8, 6), new DateOnly(2026, 7, 6), 6400m, "Staff supper, Gardens");
        yield return Account("Tamboerskloof House", "tambo", "Chris Naidoo", "0825552109", "chris@tamboerskloofhouse.example", new DateOnly(2025, 9, 9), new DateOnly(2026, 7, 9), 8700m, "House dinner, Gardens");
        yield return Account("Vredehoek Residents", "vrede", "Fatima Essop", "0825552121", "fatima@vredehoekresidents.example", new DateOnly(2025, 8, 21), new DateOnly(2026, 6, 21), 5200m, "Residents supper, Gardens");
        yield return Account("Green Point Film Office", "green", "Sam Petersen", "0825552111", "sam@greenpointfilm.example", new DateOnly(2025, 8, 11), new DateOnly(2026, 7, 11), 11200m, "Night shoot catering, Sea Point");
        yield return Account("Mouille Point Yacht Club", "mouille", "Helen Visser", "0825552114", "helen@mouillepoint.example", new DateOnly(2025, 9, 14), new DateOnly(2026, 7, 13), 9800m, "Club lunch, Sea Point");
        yield return Account("Sea Point Bowling Club", "bowling", "Andre Fortuin", "0825552103", "andre@seapointbowling.example", new DateOnly(2025, 10, 3), new DateOnly(2026, 7, 3), 4300m, "Prize lunch, Sea Point");
        yield return Account("Stellenbosch Wine Desk", "wine", "Mia Louw", "0825552117", "mia@stellenboschwine.example", new DateOnly(2025, 8, 17), new DateOnly(2026, 7, 12), 15600m, "Tasting lunch, Stellenbosch");
        yield return Account("Jonkershoek School Fete", "jonker", "Ruth Jacobs", "0825552122", "ruth@jonkershoekschool.example", new DateOnly(2025, 9, 22), new DateOnly(2026, 6, 22), 7600m, "Fete kitchen, Stellenbosch");
        yield return Account("Devon Valley Weddings", "devon", "Liam October", "0825552125", "liam@devonvalley.example", new DateOnly(2025, 8, 25), new DateOnly(2026, 7, 10), 13400m, "Wedding supper, Stellenbosch");
    }

    private static WhoToCallCustomerBook Account(
        string name,
        string slug,
        string contact,
        string phone,
        string email,
        DateOnly first,
        DateOnly last,
        decimal amount,
        string description) =>
        Person(name, slug, contact, phone, email, MonthInvoices(slug, first, last, amount, description));

    private static WhoToCallCustomerBook SignalHill()
    {
        var invoices = new List<WhoToCallInvoice>();
        for (var date = new DateOnly(2025, 12, 12); date <= new DateOnly(2026, 6, 12); date = date.AddMonths(1))
        {
            invoices.Add(Paid($"karoo-i-signal-{date:yyyyMMdd}", $"KK SH {date:yyyyMM}", date, 18400m, "Corporate lunch, Signal Hill"));
        }

        invoices.Add(Open(
            LateInvoiceRowId,
            "KK SH 1842",
            new DateOnly(2026, 6, 28),
            new DateOnly(2026, 7, 5),
            18400m,
            "Corporate lunch, Signal Hill"));
        return Person("Signal Hill Functions", "signal", "Lerato Jacobs", "0825551901", "lerato@signalhill.example", invoices);
    }

    private static WhoToCallCustomerBook Oranjezicht()
    {
        var invoices = new List<WhoToCallInvoice>();
        for (var date = new DateOnly(2025, 8, 10); date <= new DateOnly(2026, 2, 10); date = date.AddMonths(1))
        {
            invoices.Add(Paid($"karoo-i-oranje-{date:yyyyMMdd}", $"KK OR {date:yyyyMM}", date, 15600m, "Boardroom platters, Oranjezicht"));
        }

        return Person("Oranjezicht Boardroom", "oranje", "Pieter Botha", "0825551910", "pieter@oranjezichtboard.example", invoices);
    }

    private static WhoToCallCustomerBook StellenboschPicnic()
    {
        var invoices = new List<WhoToCallInvoice>();
        for (var date = new DateOnly(2025, 9, 8); date <= new DateOnly(2026, 5, 8); date = date.AddMonths(1))
        {
            invoices.Add(Paid($"karoo-i-picnic-{date:yyyyMMdd}", $"KK SP {date:yyyyMM}", date, 20000m, "Picnic hampers, Stellenbosch"));
        }

        invoices.Add(Paid("karoo-i-picnic-20260608", "KK SP 202606", new DateOnly(2026, 6, 8), 8000m, "Picnic hampers, Stellenbosch"));
        invoices.Add(Open(
            "karoo-i-picnic-20260713",
            "KK SP 202607",
            AsAt,
            new DateOnly(2026, 7, 28),
            8000m,
            "Picnic hampers, Stellenbosch"));
        return Person("Stellenbosch Picnic Co", "picnic", "Ayesha Williams", "0825551908", "ayesha@stellenboschpicnic.example", invoices);
    }

    private static List<WhoToCallInvoice> MonthInvoices(string slug, DateOnly first, DateOnly last, decimal amount, string description)
    {
        var invoices = new List<WhoToCallInvoice>();
        for (var date = first; date <= last; date = date.AddMonths(1))
        {
            invoices.Add(Paid($"karoo-i-{slug}-{date:yyyyMMdd}", $"KK {slug[..2].ToUpperInvariant()} {date:yyyyMM}", date, amount, description));
        }

        return invoices;
    }

    private static WhoToCallCustomerBook Person(
        string name,
        string slug,
        string contact,
        string phone,
        string email,
        IReadOnlyList<WhoToCallInvoice> invoices) => new()
    {
        CustomerId = SampleIds.For("karoo-customer-" + slug),
        Name = name,
        RowId = "karoo-c-" + slug,
        ContactPerson = contact,
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
        Terms = "21 days",
        Lines = [Line(rowId, description, amount)]
    };

    private static WhoToCallLine Line(string rowId, string description, decimal amount) => new()
    {
        RowId = rowId + "l",
        Description = description,
        Amount = amount
    };

    private static IReadOnlyList<SampleBankRow> TillAndCosts()
    {
        var rows = new List<SampleBankRow>();
        foreach (var (year, month, revenue) in RevenueMonths)
        {
            var branches = Branches(year, month, revenue);
            var week = 0;
            foreach (var day in new[] { 5, 12, 19, 26 })
            {
                foreach (var (name, total) in branches)
                {
                    var parts = Split(total, 4);
                    var date = new DateOnly(year, month, day);
                    var slug = name.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
                    rows.Add(new SampleBankRow(
                        $"karoo-till-{year}{month:00}-{slug}-{week}",
                        date,
                        SampleBank.Booked(date),
                        $"{name} till takings",
                        parts[week],
                        "Till takings",
                        name));
                }

                week++;
            }

            var costDay = new DateOnly(year, month, 28);
            foreach (var (category, counterparty, amount) in Costs(year, month, revenue))
            {
                var slug = category.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
                rows.Add(new SampleBankRow(
                    $"karoo-cost-{year}{month:00}-{slug}",
                    costDay,
                    costDay,
                    category,
                    -amount,
                    category,
                    counterparty));
            }
        }

        return SampleBank.NumberAndBalance(rows, 250000m, new DateOnly(2026, 6, 1), June2026Cash);
    }

    private static (string Name, int Amount)[] Branches(int year, int month, int revenue)
    {
        if (year == 2026 && month == 6)
        {
            return [("Gardens", 318400), ("Stellenbosch", 245700), ("Sea Point", 177100)];
        }

        var stellenbosch = (int)((long)revenue * 3315 / 10000);
        var seaPoint = (int)((long)revenue * 2389 / 10000);
        var gardens = revenue - stellenbosch - seaPoint;
        return [("Gardens", gardens), ("Stellenbosch", stellenbosch), ("Sea Point", seaPoint)];
    }

    private static (string Category, string Counterparty, int Amount)[] Costs(int year, int month, int revenue)
    {
        if (year == 2026 && month == 6)
        {
            return Lines(252000, 209000, 76500, 38900, 21400, 57300);
        }

        if (year == 2025 && month == 6)
        {
            return Lines(224300, 201800, 72900, 35200, 19600, 76000);
        }

        if (year == 2026 && month == 5)
        {
            return Lines(255780, 209000, 76500, 38900, 21400, 57300);
        }

        return Lines(
            Round(revenue * 0.315m),
            Round(revenue * (209000m / 741200m)),
            Round(revenue * (76500m / 741200m)),
            Round(revenue * (38900m / 741200m)),
            Round(revenue * (21400m / 741200m)),
            Round(revenue * (57300m / 741200m)));
    }

    private static (string Category, string Counterparty, int Amount)[] Lines(
        int food,
        int staff,
        int rent,
        int utilities,
        int marketing,
        int other) =>
    [
        ("Food and beverage", "Freshy Produce", food),
        ("Staff", "Payroll", staff),
        ("Rent and occupancy", "Landlords", rent),
        ("Utilities", "City services", utilities),
        ("Marketing", "Neighbourhood guide", marketing),
        ("Other operating", "Operating costs", other)
    ];

    private static int Round(decimal value) => (int)decimal.Round(value, 0, MidpointRounding.AwayFromZero);

    private static int[] Split(int total, int parts)
    {
        var result = new int[parts];
        var each = total / parts;
        var remainder = total % parts;
        for (var i = 0; i < parts; i++)
        {
            result[i] = each + (i < remainder ? 1 : 0);
        }

        return result;
    }
}
