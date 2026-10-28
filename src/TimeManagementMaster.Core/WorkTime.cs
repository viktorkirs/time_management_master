using System.Globalization;

namespace TimeManagementMaster.Core;

public sealed record WorkTime(TimeOnly Start, TimeOnly End, TimeOnly? LunchStart, TimeOnly? LunchEnd, bool EndsNextDay = false)
{
    public int GrossMinutes => Minute(End) + (EndsNextDay ? 1440 : 0) - Minute(Start);
    public int LunchMinutes => LunchStart is TimeOnly start && LunchEnd is TimeOnly end
        ? (Minute(end) - Minute(start) + 1440) % 1440 : 0;
    public int NetMinutes => GrossMinutes - LunchMinutes;
    public string RangeText => $"{Clock(Start)}–{Clock(End)}" + (EndsNextDay ? " (+1)" : "");
    public string LunchText => LunchStart is TimeOnly start && LunchEnd is TimeOnly end ? $"{Clock(start)}–{Clock(end)}" : "—";
    public string HoursText => FormatMinutes(NetMinutes);

    public string? ValidationError()
    {
        if (Start.Second != 0 || End.Second != 0 || Start.Ticks % TimeSpan.TicksPerMinute != 0 || End.Ticks % TimeSpan.TicksPerMinute != 0
            || GrossMinutes <= 0 || GrossMinutes > 1440)
            return TextCatalog.Get("InvalidWorkRange");
        if (LunchStart.HasValue != LunchEnd.HasValue)
            return TextCatalog.Get("LunchBothEnds");
        if (LunchStart is TimeOnly start && LunchEnd is TimeOnly end)
        {
            var begin = Minute(start);
            if (begin < Minute(Start)) begin += 1440;
            if (start.Ticks % TimeSpan.TicksPerMinute != 0 || end.Ticks % TimeSpan.TicksPerMinute != 0
                || LunchMinutes <= 0 || LunchMinutes >= GrossMinutes || begin + LunchMinutes > Minute(Start) + GrossMinutes)
                return TextCatalog.Get("LunchOutsideWork");
        }
        return null;
    }

    public static int Minute(TimeOnly value) => value.Hour * 60 + value.Minute;
    public static string Clock(TimeOnly value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);
    public static string FormatMinutes(int minutes) => $"{minutes / 60}:{minutes % 60:00}";
}

public sealed record MonthlyHoursRow(Employee Employee, int AssignmentDays, int TimedDays, int MissingTimeDays,
    int NetMinutes, int VacationDays, int SickDays, int UnpaidDays, int OtherDays)
{
    public string EmployeeName => Employee.FullName;
    public string Specialty => Employee.Specialty ?? "";
    public string HoursText => WorkTime.FormatMinutes(NetMinutes);
    public decimal DecimalHours => NetMinutes / 60m;
}

public sealed record WorkDayDetail(DateOnly Date, string SiteName, WorkTime? Time)
{
    public string DateText => TextCatalog.FormatDate(Date);
    public string StartText => Time is null ? "—" : WorkTime.Clock(Time.Start);
    public string EndText => Time is null ? "—" : WorkTime.Clock(Time.End) + (Time.EndsNextDay ? " (+1)" : "");
    public string LunchText => Time?.LunchText ?? "—";
    public string HoursText => Time?.HoursText ?? TextCatalog.Get("TimeNotSet");
}
