namespace TimeManagementMaster.Core;

public static class CalendarDates
{
    public static bool IsWeekend(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static bool IsWorkday(DateOnly date) => !IsWeekend(date);

    public static DateOnly MondayOf(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff);
    }

    public static IReadOnlyList<DateOnly> Expand(DateOnly start, DateOnly end, bool weekdaysOnly)
    {
        if (end < start)
            throw new ArgumentException(TextCatalog.Get("EndBeforeStart"));

        var dates = new List<DateOnly>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (weekdaysOnly && IsWeekend(day))
                continue;
            dates.Add(day);
        }

        return dates;
    }

    public static string FormatList(IEnumerable<DateOnly> dates) =>
        string.Join(", ", dates.OrderBy(d => d).Select(TextCatalog.FormatDate));
}
