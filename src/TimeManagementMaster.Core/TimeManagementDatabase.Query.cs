using System.Text;

namespace TimeManagementMaster.Core;

public sealed partial class TimeManagementDatabase
{
    public IReadOnlyList<AssignmentRow> ListAssignments() => LoadAssignments(null, null, null, null);

    public IReadOnlyList<AbsenceRow> ListAbsences(long? employeeId = null) => LoadAbsences(employeeId, null, null);

    public IReadOnlyList<Employee> ListUnassigned(DateOnly date)
    {
        if (CalendarDates.IsWeekend(date))
            return Array.Empty<Employee>();

        var assigned = LoadAssignments(null, null, date, date).Select(row => row.EmployeeId).ToHashSet();
        var absent = LoadAbsences(null, date, date).Select(row => row.EmployeeId).ToHashSet();
        return LoadEmployees(includeArchived: false)
            .Where(employee => !assigned.Contains(employee.Id) && !absent.Contains(employee.Id))
            .ToList();
    }

    public DayCell GetEmployeeDay(long employeeId, DateOnly date)
    {
        GetEmployee(employeeId);
        return BuildCell(employeeId, date, LoadAbsences(employeeId, date, date), LoadAssignments(employeeId, null, date, date), SitesById());
    }

    public DayBoard GetDayBoard(DateOnly date)
    {
        var employees = LoadEmployees(includeArchived: true);
        var active = employees.Where(employee => !employee.IsArchived).ToList();
        var sites = LoadSites(includeArchived: true);
        var assignments = LoadAssignments(null, null, date, date);
        var absences = LoadAbsences(null, date, date).Where(absence => absence.Covers(date)).ToList();
        var people = employees.ToDictionary(employee => employee.Id);

        var groups = new List<SiteDayGroup>();
        foreach (var site in sites.OrderBy(site => site.IsArchived).ThenBy(site => site.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var members = assignments
                .Where(row => row.SiteId == site.Id)
                .Select(row => people[row.EmployeeId])
                .OrderBy(employee => employee.LastName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(employee => employee.FirstName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (site.IsArchived && members.Count == 0)
                continue;
            groups.Add(new SiteDayGroup(site, members));
        }

        var absentIds = absences.Select(absence => absence.EmployeeId).ToHashSet();
        var assignedIds = assignments.Select(row => row.EmployeeId).ToHashSet();
        var unassigned = CalendarDates.IsWeekend(date)
            ? new List<Employee>()
            : active.Where(employee => !assignedIds.Contains(employee.Id) && !absentIds.Contains(employee.Id)).ToList();

        var absentPeople = absences
            .Select(absence => new AbsentPerson(
                people[absence.EmployeeId],
                absence,
                Labels.Absence(absence.Kind, absence.Note, absence.IsOpen)))
            .OrderBy(item => item.Employee.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Employee.FirstName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new DayBoard(date, groups, unassigned, absentPeople, assignments.Count, active.Count);
    }

    public IReadOnlyList<EmployeeCalendarRow> GetEmployeeCalendar(
        DateOnly from,
        DateOnly to,
        string? employeeQuery,
        long? siteFilterId)
    {
        if (to < from)
            throw new ArgumentException(TextCatalog.Get("EndBeforeStart"));

        var dates = CalendarDates.Expand(from, to, weekdaysOnly: false);
        var employees = LoadEmployees(includeArchived: true);
        var absences = LoadAbsences(null, from, to);
        var assignments = LoadAssignments(null, null, from, to);
        var sites = SitesById();
        var query = employeeQuery?.Trim();
        var rows = new List<EmployeeCalendarRow>();

        foreach (var employee in employees)
        {
            if (!string.IsNullOrEmpty(query)
                && !ContainsText(employee.LastName, query)
                && !ContainsText(employee.FirstName, query)
                && !ContainsText(employee.Specialty, query))
                continue;

            var employeeAbsences = absences.Where(absence => absence.EmployeeId == employee.Id).ToList();
            var employeeAssignments = assignments.Where(row => row.EmployeeId == employee.Id).ToList();
            var cells = dates
                .Select(date => BuildCell(employee.Id, date, employeeAbsences, employeeAssignments, sites))
                .ToList();
            var hasRecord = cells.Any(cell => cell.Kind != CellKind.Empty);
            if (employee.IsArchived && !hasRecord)
                continue;
            if (siteFilterId is long siteId && cells.All(cell => cell.SiteId != siteId))
                continue;
            rows.Add(new EmployeeCalendarRow(employee, cells));
        }

        return rows;
    }

    public IReadOnlyList<SiteAssignmentLine> GetSiteAssignments(long siteId, DateOnly from, DateOnly to)
    {
        GetSite(siteId);
        var people = LoadEmployees(includeArchived: true).ToDictionary(employee => employee.Id);
        return LoadAssignments(null, siteId, from, to)
            .OrderBy(row => row.Date)
            .ThenBy(row => people[row.EmployeeId].LastName, StringComparer.CurrentCultureIgnoreCase)
            .Select(row =>
            {
                var employee = people[row.EmployeeId];
                return new SiteAssignmentLine(row.Date, employee.Id, employee.FullName, employee.Specialty);
            })
            .ToList();
    }

    public string ExportCsv(DateOnly from, DateOnly to)
    {
        if (to < from)
            throw new ArgumentException(TextCatalog.Get("EndBeforeStart"));

        var employees = LoadEmployees(includeArchived: true)
            .OrderBy(employee => employee.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(employee => employee.FirstName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(employee => employee.Id)
            .ToList();
        var assignments = LoadAssignments(null, null, from, to);
        var absences = LoadAbsences(null, from, to);
        var sites = SitesById();
        var builder = new StringBuilder();
        builder.Append('\uFEFF');
        builder.AppendLine(TextCatalog.Get("CsvHeader"));

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var weekend = CalendarDates.IsWeekend(date);
            foreach (var employee in employees)
            {
                var absence = absences.FirstOrDefault(row => row.EmployeeId == employee.Id && row.Covers(date));
                var assignment = assignments.FirstOrDefault(row => row.EmployeeId == employee.Id && row.Date == date);
                var hasRecord = absence is not null || assignment is not null;
                if (weekend && !hasRecord)
                    continue;
                if (employee.IsArchived && !hasRecord)
                    continue;

                string category;
                var siteName = "";
                var explanation = "";
                var address = "";
                if (absence is not null)
                {
                    category = Labels.CsvCategory(KindToCell(absence.Kind));
                    if (absence.Kind == AbsenceKind.Other)
                        explanation = absence.Note ?? "";
                    else if (absence.IsOpen)
                        explanation = TextCatalog.Get("OngoingWord");
                }
                else if (assignment is not null)
                {
                    category = TextCatalog.Get("CsvSite");
                    var site = sites[assignment.SiteId];
                    siteName = site.Name;
                    address = site.Address ?? "";
                }
                else
                {
                    category = TextCatalog.Get("CsvUnassigned");
                }

                builder.Append(Csv(date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
                builder.Append(';');
                builder.Append(Csv(employee.LastName));
                builder.Append(';');
                builder.Append(Csv(employee.FirstName));
                builder.Append(';');
                builder.Append(Csv(employee.Specialty ?? ""));
                builder.Append(';');
                builder.Append(Csv(category));
                builder.Append(';');
                builder.Append(Csv(siteName));
                builder.Append(';');
                builder.Append(Csv(explanation));
                builder.Append(';');
                builder.Append(Csv(address));
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private DayCell BuildCell(
        long employeeId,
        DateOnly date,
        IReadOnlyList<AbsenceRow> absences,
        IReadOnlyList<AssignmentRow> assignments,
        IReadOnlyDictionary<long, Site> sites)
    {
        var absence = absences.FirstOrDefault(row => row.EmployeeId == employeeId && row.Covers(date));
        if (absence is not null)
        {
            return new DayCell(
                date,
                KindToCell(absence.Kind),
                Labels.Absence(absence.Kind, absence.Note, absence.IsOpen),
                null,
                null,
                absence.Id);
        }

        var assignment = assignments.FirstOrDefault(row => row.EmployeeId == employeeId && row.Date == date);
        if (assignment is not null)
        {
            var site = sites[assignment.SiteId];
            return new DayCell(date, CellKind.Assignment, site.Name, site.Id, site.Name, null, assignment.Time);
        }

        return new DayCell(date, CellKind.Empty, "", null, null, null);
    }

    private Dictionary<long, Site> SitesById() =>
        LoadSites(includeArchived: true).ToDictionary(site => site.Id);

    private static CellKind KindToCell(AbsenceKind kind) => kind switch
    {
        AbsenceKind.Sick => CellKind.Sick,
        AbsenceKind.Vacation => CellKind.Vacation,
        AbsenceKind.Unpaid => CellKind.Unpaid,
        AbsenceKind.Other => CellKind.Other,
        _ => CellKind.Empty
    };

    private static string Csv(string value)
    {
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
