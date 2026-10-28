namespace TimeManagementMaster.Core;

public static class PeriodMath
{
    public static bool Overlaps(DateOnly start, DateOnly? end, DateOnly otherStart, DateOnly? otherEnd)
    {
        if (end is not null && end.Value < otherStart)
            return false;
        if (otherEnd is not null && otherEnd.Value < start)
            return false;
        return true;
    }

    public static List<(DateOnly Start, DateOnly? End)> SubtractDates(
        DateOnly start,
        DateOnly? end,
        IReadOnlyCollection<DateOnly> remove)
    {
        var removed = remove
            .Where(date => date >= start && (end is null || date <= end.Value))
            .Distinct()
            .OrderBy(date => date)
            .ToList();

        if (removed.Count == 0)
            return [(start, end)];

        var result = new List<(DateOnly Start, DateOnly? End)>();
        DateOnly? cursor = start;
        var index = 0;
        while (cursor is not null && index < removed.Count)
        {
            var islandStart = removed[index];
            var islandEnd = islandStart;
            while (index + 1 < removed.Count && removed[index + 1] == islandEnd.AddDays(1))
            {
                index++;
                islandEnd = removed[index];
            }

            index++;

            if (cursor.Value < islandStart)
                result.Add((cursor.Value, islandStart.AddDays(-1)));

            var next = islandEnd.AddDays(1);
            if (end is not null && next > end.Value)
            {
                cursor = null;
                break;
            }

            cursor = next;
        }

        if (cursor is not null && (end is null || cursor.Value <= end.Value))
            result.Add((cursor.Value, end));

        return result;
    }

    public static List<(DateOnly Start, DateOnly? End)> SubtractRange(
        DateOnly start,
        DateOnly? end,
        DateOnly cutStart,
        DateOnly? cutEnd)
    {
        if (cutEnd is not null && cutEnd.Value < start)
            return [(start, end)];
        if (end is not null && cutStart > end.Value)
            return [(start, end)];

        var cutFrom = cutStart < start ? start : cutStart;
        DateOnly? cutTo = cutEnd;
        if (end is not null && (cutTo is null || cutTo.Value > end.Value))
            cutTo = end;

        var coversAll = cutFrom <= start && CoversThroughEnd(end, cutEnd, cutTo);
        if (coversAll)
            return [];

        var result = new List<(DateOnly Start, DateOnly? End)>();
        if (cutFrom > start)
            result.Add((start, cutFrom.AddDays(-1)));

        if (cutTo is not null)
        {
            var right = cutTo.Value.AddDays(1);
            if (end is null || right <= end.Value)
                result.Add((right, end));
        }

        return result;
    }

    private static bool CoversThroughEnd(DateOnly? periodEnd, DateOnly? requestedCutEnd, DateOnly? clampedCutEnd)
    {
        if (periodEnd is null)
            return requestedCutEnd is null;

        return clampedCutEnd is not null && clampedCutEnd.Value >= periodEnd.Value;
    }

    public static string OverlapText(DateOnly start, DateOnly? end, DateOnly otherStart, DateOnly? otherEnd)
    {
        var from = start > otherStart ? start : otherStart;
        DateOnly? to;
        if (end is null && otherEnd is null)
            to = null;
        else if (end is null)
            to = otherEnd;
        else if (otherEnd is null)
            to = end;
        else
            to = end.Value < otherEnd.Value ? end : otherEnd;

        return Labels.Period(from, to);
    }
}
