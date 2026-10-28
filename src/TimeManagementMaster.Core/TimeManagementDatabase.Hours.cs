using System.Globalization;
using System.Text;

namespace TimeManagementMaster.Core;

public sealed partial class TimeManagementDatabase
{
    public IReadOnlyList<MonthlyHoursRow> GetMonthlyHours(int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var days = CalendarDates.Expand(from, to, weekdaysOnly: false);
        var assignments = LoadAssignments(null, null, from, to).ToLookup(row => row.EmployeeId);
        var absences = LoadAbsences(null, from, to).ToLookup(row => row.EmployeeId);
        return LoadEmployees(includeArchived: true)
            .Where(person => !person.IsArchived || assignments[person.Id].Any() || absences[person.Id].Any())
            .OrderBy(person => person.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(person => person.FirstName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(person => person.Id)
            .Select(person =>
            {
                var work = assignments[person.Id].ToList();
                var leaves = absences[person.Id].ToList();
                int Count(AbsenceKind kind) => days.Count(date => leaves.Any(leave => leave.Kind == kind && leave.Covers(date)));
                return new MonthlyHoursRow(person, work.Count, work.Count(row => row.Time is not null),
                    work.Count(row => row.Time is null), work.Sum(row => row.Time?.NetMinutes ?? 0),
                    Count(AbsenceKind.Vacation), Count(AbsenceKind.Sick), Count(AbsenceKind.Unpaid), Count(AbsenceKind.Other));
            }).ToList();
    }

    public IReadOnlyList<WorkDayDetail> GetWorkDayDetails(long employeeId, int year, int month)
    {
        GetEmployee(employeeId);
        var from = new DateOnly(year, month, 1);
        var sites = SitesById();
        return LoadAssignments(employeeId, null, from, from.AddMonths(1).AddDays(-1))
            .Select(row => new WorkDayDetail(row.Date, sites[row.SiteId].Name, row.Time)).ToList();
    }

    public string ExportMonthlyHoursCsv(int year, int month)
    {
        var csv = new StringBuilder("\uFEFF");
        csv.AppendLine(TextCatalog.Get("HoursCsvHeader"));
        foreach (var row in GetMonthlyHours(year, month))
        {
            csv.AppendLine(string.Join(";", new[]
            {
                $"{year:D4}-{month:D2}", row.Employee.LastName, row.Employee.FirstName, row.Specialty,
                row.AssignmentDays.ToString(CultureInfo.InvariantCulture), row.TimedDays.ToString(CultureInfo.InvariantCulture),
                row.MissingTimeDays.ToString(CultureInfo.InvariantCulture), row.HoursText,
                row.DecimalHours.ToString("0.####", TextCatalog.Culture),
                row.VacationDays.ToString(CultureInfo.InvariantCulture), row.SickDays.ToString(CultureInfo.InvariantCulture),
                row.UnpaidDays.ToString(CultureInfo.InvariantCulture), row.OtherDays.ToString(CultureInfo.InvariantCulture)
            }.Select(Csv)));
        }
        return csv.ToString();
    }

    public string ExportWorkDayDetailsCsv(int year, int month)
    {
        var csv = new StringBuilder("\uFEFF");
        csv.AppendLine(TextCatalog.Get("HoursDetailsCsvHeader"));
        foreach (var person in GetMonthlyHours(year, month))
        foreach (var day in GetWorkDayDetails(person.Employee.Id, year, month))
        {
            csv.AppendLine(string.Join(";", new[]
            {
                day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), person.Employee.LastName,
                person.Employee.FirstName, day.SiteName, day.Time is null ? "" : WorkTime.Clock(day.Time.Start),
                day.Time is null ? "" : WorkTime.Clock(day.Time.End), day.Time?.EndsNextDay == true ? "1" : "0",
                day.Time?.LunchStart is TimeOnly lunchStart ? WorkTime.Clock(lunchStart) : "",
                day.Time?.LunchEnd is TimeOnly lunchEnd ? WorkTime.Clock(lunchEnd) : "",
                day.Time?.HoursText ?? "", day.Time is null ? TextCatalog.Get("TimeNotSet") : ""
            }.Select(Csv)));
        }
        return csv.ToString();
    }
}
