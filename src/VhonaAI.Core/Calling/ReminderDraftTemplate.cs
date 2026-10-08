using System.Text;

namespace VhonaAI.Core.Calling;

/// <summary>
/// Late reminders are a template. Every number, date, and name is copied from the cited rows.
/// There is no model call and no send.
/// </summary>
public static class ReminderDraftTemplate
{
    public const string NotSent = "Not sent";

    public static string? Build(WhoToCallFlag flag, string businessName)
    {
        if (flag.Kind != CallFlagKind.Late || flag.Late is null || flag.Late.OpenInvoices.Count == 0)
        {
            return null;
        }

        var open = flag.Late.OpenInvoices;
        var greeting = FirstName(flag.ContactPerson) ?? flag.CustomerName;
        var count = open.Count == 1
            ? "one of our invoices is still open"
            : $"{NumberWord(open.Count)} of our invoices are still open";
        var builder = new StringBuilder();
        builder.Append("Hi ").Append(greeting).Append(", I hope you and everyone at ").Append(flag.CustomerName).AppendLine(" are well.");
        builder.AppendLine();
        builder.Append("Just a friendly reminder from ").Append(businessName).Append(" that ").Append(count).AppendLine(":");
        foreach (var invoice in open)
        {
            builder.Append(invoice.Number)
                .Append(" for ")
                .Append(RandAmounts.Format(invoice.AmountDue))
                .Append(", due ")
                .Append(invoice.Due is DateOnly due ? RandAmounts.DayMonth(due) : "the due date");
            if (invoice.DaysOverdue is int days)
            {
                builder.Append(", ").Append(days).Append(days == 1 ? " day overdue" : " days overdue");
            }

            builder.AppendLine();
        }

        builder.Append("That comes to ").Append(RandAmounts.Format(flag.OpenTotal)).AppendLine(" in total.");
        builder.AppendLine();
        builder.AppendLine("If they didn't reach you, or something on them needs sorting out, just let me know and I'll gladly resend them or fix it. If payment is already on its way, please ignore this.");
        builder.AppendLine();
        builder.Append("Thanks so much,").AppendLine();
        builder.Append(businessName);
        return builder.ToString();
    }

    private static string? FirstName(string? contact)
    {
        if (string.IsNullOrWhiteSpace(contact))
        {
            return null;
        }

        var token = contact.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static string NumberWord(int count) => count switch
    {
        2 => "two",
        3 => "three",
        4 => "four",
        5 => "five",
        6 => "six",
        7 => "seven",
        8 => "eight",
        9 => "nine",
        _ => count.ToString()
    };
}
