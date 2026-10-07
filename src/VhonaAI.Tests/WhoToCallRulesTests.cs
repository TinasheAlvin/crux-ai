using VhonaAI.Core.Calling;
using VhonaAI.Core.Entities;

namespace VhonaAI.Tests;

public class WhoToCallRulesTests
{
    private static readonly DateOnly AsAt = new(2026, 3, 31);

    [Fact]
    public void Sample_books_flag_the_four_customers_exactly()
    {
        var books = WhoToCallSample.Build("Harbour Street Studio");
        Assert.Equal(AsAt, books.AsAt);

        var result = WhoToCallRules.Evaluate(books, WhoToCallThresholds.FromDefaults());

        Assert.Contains("most days overdue first", WhoToCallResult.SortExplanation);
        Assert.Equal(
            ["Sondela Dental Studio", "Marula Ridge Office Park", "Tamboti Clinic Rooms", "Kopano Guest Lodge"],
            result.Flags.Select(flag => flag.CustomerName).ToArray());
        Assert.Equal(
            [CallFlagKind.Late, CallFlagKind.Stopped, CallFlagKind.Stopped, CallFlagKind.Dropped],
            result.Flags.Select(flag => flag.Kind).ToArray());
        Assert.All(result.Flags, flag => Assert.NotEmpty(flag.CitedRowIds));
        Assert.Equal("Flagged from these rows only. No score.", WhoToCallResult.ReceiptFooter);
        Assert.DoesNotContain(
            typeof(WhoToCallFlag).GetProperties().Select(property => property.Name),
            name => name.Contains("Score", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Rank", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Risk", StringComparison.OrdinalIgnoreCase));

        var marula = result.Flags[1];
        Assert.Equal(6, marula.RowCount);
        Assert.Equal("Monthly invoices stopped after 28 January", marula.Why);
        Assert.Equal("Last invoice 28 January. Monthly maintenance, R18 400, not billed since.", marula.Detail);
        Assert.Null(marula.DraftBody);

        var tamboti = result.Flags[2];
        Assert.Equal(5, tamboti.RowCount);
        Assert.Equal("Every few weeks invoices stopped after 19 February", tamboti.Why);
        Assert.Contains("Aircon service call outs", tamboti.Detail);
        Assert.Contains("R2 750", tamboti.Detail);
        Assert.Null(tamboti.DraftBody);

        var kopano = result.Flags[3];
        Assert.Equal(5, kopano.RowCount);
        Assert.Equal("Dropped from about R9 700 to about R3 150 a month since February", kopano.Why);
        Assert.Equal(
            "Linen and laundry service, about R6 550 a month, dropped from February invoices. Pool and garden, R3 150, still billed.",
            kopano.Detail);
        Assert.Equal("Linen and laundry service", kopano.Dropped!.VanishedDescription);
        Assert.Equal([9700m, 9700m, 9700m, 3150m, 3150m], kopano.Dropped.Totals.ToArray());
        Assert.Null(kopano.DraftBody);

        var sondela = result.Flags[0];
        Assert.Equal(4, sondela.RowCount);
        Assert.Equal(2, sondela.OpenInvoiceCount);
        Assert.Equal(21350m, sondela.OpenTotal);
        Assert.Equal(47, sondela.OldestDaysOverdue);
        Assert.Equal("Two invoices open, oldest 47 days overdue", sondela.Why);
        Assert.Equal("Oldest open INV 2295, due 12 February.", sondela.Detail);
        Assert.Contains("paid on or before their due dates", sondela.Late!.Intro, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([47, 19], sondela.Late.OpenInvoices.Select(row => row.DaysOverdue).ToArray());

        var draft = sondela.DraftBody!;
        Assert.Contains("Hi Naledi", draft);
        Assert.Contains("Sondela Dental Studio", draft);
        Assert.Contains("Harbour Street Studio", draft);
        Assert.Contains("INV 2295", draft);
        Assert.Contains("R12 400", draft);
        Assert.Contains("INV 2318", draft);
        Assert.Contains("R8 950", draft);
        Assert.Contains("R21 350", draft);
        Assert.Contains("47 days overdue", draft);
        Assert.DoesNotContain("INV 2229", draft);
        Assert.DoesNotContain("R9 850", draft);
        Assert.DoesNotContain("Sipho", draft);
        foreach (var open in sondela.Late.OpenInvoices)
        {
            Assert.Contains(open.RowId, sondela.CitedRowIds);
            Assert.Contains(open.Number, draft);
            Assert.Contains(RandAmounts.Format(open.AmountDue), draft);
        }
    }

    [Fact]
    public void Late_edges_skip_due_today_missing_due_zero_voids_and_blank_rows()
    {
        var dueToday = Evaluate(Customer("Due today", Open("due-today", AsAt, AsAt, 100m)));
        Assert.Empty(dueToday.Flags);

        var noDue = Evaluate(Customer("No due", Open("no-due", AsAt.AddDays(-40), null, 100m)));
        Assert.Empty(noDue.Flags);

        var settled = Evaluate(Customer("Settled", Open("settled", AsAt.AddDays(-40), AsAt.AddDays(-10), 100m, amountDue: 0)));
        Assert.Empty(settled.Flags);

        foreach (var status in new[] { InvoiceStatus.Void, InvoiceStatus.Draft, InvoiceStatus.Credited })
        {
            var skipped = Evaluate(Customer(status.ToString(), Open("bad", AsAt.AddDays(-40), AsAt.AddDays(-10), 100m, status)));
            Assert.Empty(skipped.Flags);
        }

        var blank = Evaluate(Customer("Blank", Open("", AsAt.AddDays(-40), AsAt.AddDays(-10), 100m)));
        Assert.Empty(blank.Flags);

        var part = Evaluate(Customer("Part", Open("part", new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), 1000m, amountDue: 400m)));
        var flag = Assert.Single(part.Flags);
        Assert.Equal(CallFlagKind.Late, flag.Kind);
        Assert.Equal(400m, flag.OpenTotal);
        Assert.Contains("R400", flag.DraftBody);
        Assert.DoesNotContain("R1 000", flag.DraftBody);
        Assert.True(flag.OldestDaysOverdue > 0);
    }

    [Fact]
    public void One_overdue_invoice_is_late_without_the_history_minimum()
    {
        var result = Evaluate(Customer("Once", Open("once", new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), 50m)));
        var flag = Assert.Single(result.Flags);
        Assert.Equal(CallFlagKind.Late, flag.Kind);
        Assert.Equal(["once"], flag.CitedRowIds.ToArray());
        Assert.NotNull(flag.DraftBody);
    }

    [Fact]
    public void Stopped_needs_two_missed_cycles_and_three_invoices()
    {
        var shortHistory = Customer(
            "Short",
            Paid("a", new DateOnly(2025, 11, 1), 100m),
            Paid("b", new DateOnly(2025, 12, 1), 100m));
        Assert.Empty(Evaluate(shortHistory).Flags);

        var oneMiss = Customer(
            "One miss",
            Paid("d", new DateOnly(2025, 12, 19), 100m),
            Paid("j", new DateOnly(2026, 1, 19), 100m),
            Paid("f", new DateOnly(2026, 2, 19), 100m));
        Assert.Empty(Evaluate(oneMiss).Flags);

        var twoMiss = Evaluate(oneMiss, asAt: new DateOnly(2026, 4, 22));
        var flag = Assert.Single(twoMiss.Flags);
        Assert.Equal(CallFlagKind.Stopped, flag.Kind);
        Assert.Equal(3, flag.RowCount);
        Assert.Null(flag.DraftBody);

        var resumed = Customer(
            "Back",
            Paid("d", new DateOnly(2025, 12, 19), 100m),
            Paid("j", new DateOnly(2026, 1, 19), 100m),
            Paid("f", new DateOnly(2026, 2, 19), 100m),
            Paid("a", new DateOnly(2026, 4, 20), 100m));
        Assert.Empty(Evaluate(resumed, asAt: new DateOnly(2026, 4, 22)).Flags);
    }

    [Fact]
    public void Dropped_percent_vanished_line_and_threshold_edges()
    {
        var steady = Months("Steady", 1000m, 1000m, 1000m, 700m, 700m, "Same work");
        Assert.Empty(Evaluate(steady).Flags);

        var halved = Months("Halved", 1000m, 1000m, 1000m, 400m, 400m, "Same work");
        var dropped = Assert.Single(Evaluate(halved).Flags);
        Assert.Equal(CallFlagKind.Dropped, dropped.Kind);
        Assert.Equal(5, dropped.RowCount);
        Assert.Null(dropped.DraftBody);

        var oneMonth = Months("One month", 1000m, 1000m, 1000m, 400m, 1000m, "Same work");
        Assert.Empty(Evaluate(oneMonth).Flags);

        var vanished = Customer(
            "Vanished",
            Mixed(new DateOnly(2025, 11, 30), ("Linen", 100m), ("Pool", 1000m)),
            Mixed(new DateOnly(2025, 12, 31), ("Linen", 100m), ("Pool", 1000m)),
            Mixed(new DateOnly(2026, 1, 31), ("Linen", 100m), ("Pool", 1000m)),
            Mixed(new DateOnly(2026, 2, 28), ("Pool", 1000m)),
            Mixed(new DateOnly(2026, 3, 31), ("Pool", 1000m)));
        var line = Assert.Single(Evaluate(vanished).Flags);
        Assert.Equal(CallFlagKind.Dropped, line.Kind);
        Assert.Equal("Linen", line.Dropped!.VanishedDescription);
        Assert.Contains("Pool", line.Detail);
    }

    [Fact]
    public void Changing_thresholds_recomputes_the_sample()
    {
        var books = WhoToCallSample.Build();
        var stricterStop = WhoToCallRules.Evaluate(books, new WhoToCallThresholds(3, 50m, 2, 3));
        Assert.Equal(["Sondela Dental Studio", "Kopano Guest Lodge"], stricterStop.Flags.Select(flag => flag.CustomerName).ToArray());

        var longerDrop = WhoToCallRules.Evaluate(books, new WhoToCallThresholds(2, 50m, 3, 3));
        Assert.DoesNotContain(longerDrop.Flags, flag => flag.CustomerName == "Kopano Guest Lodge");
        Assert.Contains(longerDrop.Flags, flag => flag.CustomerName == "Sondela Dental Studio");

        var moreHistory = WhoToCallRules.Evaluate(books, new WhoToCallThresholds(2, 50m, 2, 7));
        var onlyLate = Assert.Single(moreHistory.Flags);
        Assert.Equal("Sondela Dental Studio", onlyLate.CustomerName);
    }

    [Fact]
    public void Inactive_customers_are_skipped_and_late_sorts_by_days_overdue()
    {
        var quiet = Customer("Quiet", false, Open("q", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), 10m));
        var older = Customer("Older", Open("o", new DateOnly(2025, 11, 1), new DateOnly(2025, 12, 1), 10m));
        var newer = Customer("Newer", Open("n", new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1), 10m));
        var result = Evaluate(quiet, older, newer);
        Assert.Equal(["Older", "Newer"], result.Flags.Select(flag => flag.CustomerName).ToArray());
        Assert.True(result.Flags[0].OldestDaysOverdue > result.Flags[1].OldestDaysOverdue);
    }

    [Fact]
    public void Reference_date_is_explicit()
    {
        Assert.Equal(AsAtSource.EndOfImportedBooks, AsAtDates.Current);
        var dates = new[] { new DateOnly(2026, 1, 1), AsAt };
        Assert.Equal(AsAt, AsAtDates.Resolve(AsAtSource.EndOfImportedBooks, dates, new DateOnly(2026, 10, 7)));
        Assert.Equal(new DateOnly(2026, 10, 7), AsAtDates.Resolve(AsAtSource.Today, dates, new DateOnly(2026, 10, 7)));
        Assert.Equal(new DateOnly(2026, 10, 7), AsAtDates.Resolve(AsAtSource.EndOfImportedBooks, [], new DateOnly(2026, 10, 7)));
    }

    [Fact]
    public void Handoff_links_do_not_send()
    {
        var whatsApp = ReminderHandoff.WhatsAppLink("0820001111", "Hi Naledi");
        Assert.StartsWith("https://wa.me/27820001111?text=", whatsApp);
        Assert.DoesNotContain("api.whatsapp.com", whatsApp);
        Assert.Null(ReminderHandoff.WhatsAppLink(null, "Hi"));
        Assert.Null(ReminderHandoff.WhatsAppLink("082", "Hi"));

        var email = ReminderHandoff.EmailLink("naledi@sondela.example", "Open invoices", "Hi");
        Assert.StartsWith("mailto:naledi@sondela.example?", email);
        Assert.Contains("body=", email);
        Assert.Null(ReminderHandoff.EmailLink("not-an-email", "Open invoices", "Hi"));
        Assert.Equal("Not sent", ReminderDraftTemplate.NotSent);
    }

    private static WhoToCallResult Evaluate(params WhoToCallCustomerBook[] customers) =>
        Evaluate(customers, AsAt);

    private static WhoToCallResult Evaluate(WhoToCallCustomerBook[] customers, DateOnly asAt) =>
        WhoToCallRules.Evaluate(
            new WhoToCallBooks { AsAt = asAt, BusinessName = "Harbour Street Studio", Customers = customers },
            WhoToCallThresholds.FromDefaults());

    private static WhoToCallResult Evaluate(WhoToCallCustomerBook customer, DateOnly asAt) =>
        Evaluate([customer], asAt);

    private static WhoToCallCustomerBook Customer(string name, params WhoToCallInvoice[] invoices) =>
        Customer(name, true, invoices);

    private static WhoToCallCustomerBook Customer(string name, bool active, params WhoToCallInvoice[] invoices) =>
        new()
        {
            CustomerId = Guid.NewGuid(),
            Name = name,
            RowId = "c-" + name.ToLowerInvariant().Replace(' ', '-'),
            IsActive = active,
            Invoices = invoices
        };

    private static WhoToCallInvoice Paid(string rowId, DateOnly date, decimal amount) =>
        new()
        {
            RowId = rowId,
            Number = rowId,
            InvoiceDate = date,
            DueDate = date.AddDays(14),
            Amount = amount,
            AmountDue = 0,
            Status = InvoiceStatus.Paid,
            PaidDate = date,
            Lines = [new WhoToCallLine { RowId = rowId + "-l", Description = "Work", Amount = amount }]
        };

    private static WhoToCallInvoice Open(
        string rowId,
        DateOnly issued,
        DateOnly? due,
        decimal amount,
        InvoiceStatus status = InvoiceStatus.Open,
        decimal? amountDue = null) =>
        new()
        {
            RowId = rowId,
            Number = string.IsNullOrEmpty(rowId) ? "INV" : rowId,
            InvoiceDate = issued,
            DueDate = due,
            Amount = amount,
            AmountDue = amountDue ?? amount,
            Status = status,
            Lines = [new WhoToCallLine { RowId = rowId + "-l", Description = "Work", Amount = amount }]
        };

    private static WhoToCallCustomerBook Months(string name, decimal a, decimal b, decimal c, decimal d, decimal e, string line)
    {
        var dates = new[]
        {
            new DateOnly(2025, 11, 30),
            new DateOnly(2025, 12, 31),
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 2, 28),
            new DateOnly(2026, 3, 31)
        };
        var amounts = new[] { a, b, c, d, e };
        var invoices = dates.Select((date, index) => new WhoToCallInvoice
        {
            RowId = name + index,
            Number = name + index,
            InvoiceDate = date,
            DueDate = date,
            Amount = amounts[index],
            AmountDue = 0,
            Status = InvoiceStatus.Paid,
            PaidDate = date,
            Lines = [new WhoToCallLine { RowId = name + index + "-l", Description = line, Amount = amounts[index] }]
        }).ToArray();

        return Customer(name, invoices);
    }

    private static WhoToCallInvoice Mixed(DateOnly date, params (string Description, decimal Amount)[] lines)
    {
        var amount = lines.Sum(line => line.Amount);
        return new WhoToCallInvoice
        {
            RowId = date.ToString("yyyyMM"),
            Number = date.ToString("yyyyMM"),
            InvoiceDate = date,
            DueDate = date,
            Amount = amount,
            AmountDue = 0,
            Status = InvoiceStatus.Paid,
            PaidDate = date,
            Lines = lines.Select((line, index) => new WhoToCallLine
            {
                RowId = date.ToString("yyyyMM") + "-" + index,
                Description = line.Description,
                Amount = line.Amount
            }).ToList()
        };
    }
}
