using System.Globalization;

namespace VhonaAI.Core.Calling;

public static class RandAmounts
{
    public static string Format(decimal amount)
    {
        var negative = amount < 0;
        var abs = Math.Abs(amount);
        var whole = abs == decimal.Truncate(abs);
        var text = whole
            ? decimal.Truncate(abs).ToString("#,0", CultureInfo.InvariantCulture)
            : abs.ToString("#,0.00", CultureInfo.InvariantCulture);
        text = text.Replace(",", " ");
        return negative ? "-R" + text : "R" + text;
    }

    public static string About(decimal amount) => "about " + Format(amount);

    public static string DayMonth(DateOnly date) => $"{date.Day} {MonthName(date.Month)}";

    public static string DayShortMonth(DateOnly date) => $"{date.Day} {ShortMonth(date.Month)}";

    public static string MonthName(int month) => month switch
    {
        1 => "January",
        2 => "February",
        3 => "March",
        4 => "April",
        5 => "May",
        6 => "June",
        7 => "July",
        8 => "August",
        9 => "September",
        10 => "October",
        11 => "November",
        12 => "December",
        _ => throw new ArgumentOutOfRangeException(nameof(month))
    };

    private static string ShortMonth(int month) => month switch
    {
        1 => "Jan",
        2 => "Feb",
        3 => "Mar",
        4 => "Apr",
        5 => "May",
        6 => "Jun",
        7 => "Jul",
        8 => "Aug",
        9 => "Sep",
        10 => "Oct",
        11 => "Nov",
        12 => "Dec",
        _ => throw new ArgumentOutOfRangeException(nameof(month))
    };
}
