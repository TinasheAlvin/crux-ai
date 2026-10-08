using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VhonaAI.Application.Calling;
using VhonaAI.Core.Calling;
using VhonaAI.Core.Identity;
using VhonaAI.Web.Components.Pages;
using VhonaAI.Web.Components.Shared;

namespace VhonaAI.Tests;

public class WhoToCallDraftRenderTests : TestContext
{
    [Fact]
    public void The_rendered_draft_and_links_use_the_real_draft_text()
    {
        const string draft = "Hi Naledi, INV 2295 for R8 950 is 47 days overdue.";
        var customerId = Guid.NewGuid();
        var flag = LateFlag(customerId, draft);
        Services.AddSingleton<IWhoToCallAppService>(new FakeCalls(flag, draft));
        Services.AddSingleton<ICurrentUser>(new Owner());

        var cut = RenderComponent<WhoToCall>();
        cut.Find("button.who-row").Click();

        var body = cut.Find("textarea.who-draft-body");
        Assert.Equal(draft, body.GetAttribute("value"));
        Assert.Contains(draft, cut.Markup);
        Assert.DoesNotContain("draftText", cut.Markup);

        var whatsApp = cut.Find("a[href^='https://wa.me/']").GetAttribute("href")!;
        Assert.StartsWith("https://wa.me/27820001111?text=", whatsApp);
        Assert.Contains(Uri.EscapeDataString(draft), whatsApp);
        Assert.DoesNotContain("draftText", whatsApp);

        var email = cut.Find("a[href^='mailto:']").GetAttribute("href")!;
        Assert.Contains("body=", email);
        Assert.Contains(Uri.EscapeDataString(draft), email);
        Assert.DoesNotContain("draftText", email);

        Assert.NotEqual("snoozeDate", cut.Find("#snooze-until").GetAttribute("value"));
    }

    [Fact]
    public void The_receipt_total_is_overdue_and_a_future_invoice_says_not_due_yet()
    {
        var detail = new WhoToCallFlagDetail
        {
            Flag = new WhoToCallFlag
            {
                CustomerName = "Clinic",
                Kind = CallFlagKind.Late,
                OpenTotal = 800m,
                Late = new LateReceipt
                {
                    Intro = "One invoice is past its due date.",
                    Invoices =
                    [
                        new LateInvoiceRow
                        {
                            Number = "INV 2295",
                            Issued = new DateOnly(2026, 1, 10),
                            Due = new DateOnly(2026, 2, 9),
                            Amount = 800m,
                            AmountDue = 800m,
                            IsOpen = true,
                            DaysOverdue = 50
                        },
                        new LateInvoiceRow
                        {
                            Number = "INV 1004",
                            Issued = new DateOnly(2026, 3, 20),
                            Due = new DateOnly(2026, 4, 19),
                            Amount = 12400m,
                            AmountDue = 12400m,
                            NotDueYet = true
                        }
                    ]
                }
            },
            AsAt = new DateOnly(2026, 3, 31),
            BusinessName = "Harbour Street Studio"
        };

        var cut = RenderComponent<WhoToCallReceipt>(parameters => parameters
            .Add(component => component.Detail, detail)
            .Add(component => component.CanRecord, false));

        var pending = cut.FindAll("tbody tr").Single(row => row.TextContent.Contains("INV 1004", StringComparison.Ordinal));
        Assert.Contains("Not due yet", pending.TextContent);

        var total = cut.Find("tr.who-total");
        Assert.Contains("Overdue", total.TextContent);
        Assert.Contains(RandAmounts.Format(800m), total.TextContent);
        Assert.DoesNotContain("12 400", total.TextContent);
        Assert.DoesNotContain("Open", total.TextContent);
    }

    private static WhoToCallFlag LateFlag(Guid customerId, string draft) => new()
    {
        CustomerId = customerId,
        CustomerName = "Sondela Dental Studio",
        ContactPerson = "Naledi Khumalo",
        Phone = "0820001111",
        Email = "naledi@sondela.example",
        Kind = CallFlagKind.Late,
        Why = "One invoice open, 47 days overdue",
        Detail = "Oldest open INV 2295, due 12 February.",
        CitedRowIds = ["sondela-2295"],
        DraftBody = draft,
        Late = new LateReceipt
        {
            Intro = "One invoice is past its due date.",
            Invoices = []
        }
    };

    private sealed class FakeCalls(WhoToCallFlag flag, string draft) : IWhoToCallAppService
    {
        public Task<WhoToCallList> GetListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new WhoToCallList
            {
                AsAt = new DateOnly(2026, 3, 31),
                BusinessName = "Harbour Street Studio",
                HasInvoices = true,
                IsOwner = true,
                Flags = [flag]
            });

        public Task<WhoToCallFlagDetail?> GetFlagAsync(Guid customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WhoToCallFlagDetail?>(new WhoToCallFlagDetail
            {
                Flag = flag,
                AsAt = new DateOnly(2026, 3, 31),
                BusinessName = "Harbour Street Studio",
                DraftBody = draft,
                CanEdit = true
            });

        public Task<ReminderDraftDto> SaveDraftAsync(Guid customerId, string body, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordActionAsync(Guid customerId, CallActionKind kind, DateOnly? snoozeUntil, string? note, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<CallActionDto>> GetHistoryAsync(Guid customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CallActionDto>>([]);

        public Task LoadSampleAsync(string bookId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Owner : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid OrganizationId { get; } = Guid.NewGuid();
        public string Email => "owner@harbourstreet.local";
        public string DisplayName => "Owner";
        public string OrganizationName => "Harbour Street Studio";
        public string Role => "Owner";
    }
}
