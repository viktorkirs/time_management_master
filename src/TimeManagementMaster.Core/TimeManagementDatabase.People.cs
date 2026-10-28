using Microsoft.Data.Sqlite;

namespace TimeManagementMaster.Core;

public sealed partial class TimeManagementDatabase
{
    public Employee CreateEmployee(string lastName, string firstName, string? specialty, string? phone, string? note)
    {
        var last = RequireText(lastName, "LastName");
        var first = RequireText(firstName, "FirstName");
        var spec = Clean(specialty);
        var tel = CleanPhone(phone);
        var comment = Clean(note);
        long id = 0;
        InTransaction(() =>
        {
            Exec(
                """
                INSERT INTO employees(last_name, first_name, specialty, phone, note, is_archived)
                VALUES ($last, $first, $spec, $phone, $note, 0)
                """,
                ("$last", last),
                ("$first", first),
                ("$spec", spec),
                ("$phone", tel),
                ("$note", comment));
            id = LastId();
        });
        return new Employee(id, last, first, spec, tel, comment, false);
    }

    public Employee UpdateEmployee(long id, string lastName, string firstName, string? specialty, string? phone, string? note)
    {
        var existing = GetEmployee(id);
        var last = RequireText(lastName, "LastName");
        var first = RequireText(firstName, "FirstName");
        var spec = Clean(specialty);
        var tel = CleanPhone(phone);
        var comment = Clean(note);
        InTransaction(() =>
        {
            Exec(
                """
                UPDATE employees
                SET last_name = $last, first_name = $first, specialty = $spec, phone = $phone, note = $note
                WHERE id = $id
                """,
                ("$last", last),
                ("$first", first),
                ("$spec", spec),
                ("$phone", tel),
                ("$note", comment),
                ("$id", id));
        });
        return existing with { LastName = last, FirstName = first, Specialty = spec, Phone = tel, Note = comment };
    }

    public Employee GetEmployee(long id) =>
        LoadEmployees(includeArchived: true).FirstOrDefault(employee => employee.Id == id)
        ?? throw new InvalidOperationException(TextCatalog.Get("EmployeeMissing"));

    public IReadOnlyList<Employee> SearchEmployees(string? query, bool includeArchived)
    {
        var people = LoadEmployees(includeArchived);
        var text = query?.Trim();
        if (string.IsNullOrEmpty(text))
            return people;
        return people.Where(employee =>
                ContainsText(employee.LastName, text)
                || ContainsText(employee.FirstName, text)
                || ContainsText(employee.Specialty, text))
            .ToList();
    }

    public Site CreateSite(string name, string? address, string? contactName, string? phone, string? note)
    {
        var siteName = RequireText(name, "SiteName");
        var addr = Clean(address);
        var contact = Clean(contactName);
        var tel = CleanPhone(phone);
        var comment = Clean(note);
        long id = 0;
        InTransaction(() =>
        {
            EnsureActiveSiteName(siteName, null);
            Exec(
                """
                INSERT INTO sites(name, address, contact_name, phone, note, is_archived)
                VALUES ($name, $address, $contact, $phone, $note, 0)
                """,
                ("$name", siteName),
                ("$address", addr),
                ("$contact", contact),
                ("$phone", tel),
                ("$note", comment));
            id = LastId();
        });
        return new Site(id, siteName, addr, contact, tel, comment, false);
    }

    public Site UpdateSite(long id, string name, string? address, string? contactName, string? phone, string? note)
    {
        var existing = GetSite(id);
        var siteName = RequireText(name, "SiteName");
        var addr = Clean(address);
        var contact = Clean(contactName);
        var tel = CleanPhone(phone);
        var comment = Clean(note);
        InTransaction(() =>
        {
            if (!existing.IsArchived)
                EnsureActiveSiteName(siteName, id);
            Exec(
                """
                UPDATE sites
                SET name = $name, address = $address, contact_name = $contact, phone = $phone, note = $note
                WHERE id = $id
                """,
                ("$name", siteName),
                ("$address", addr),
                ("$contact", contact),
                ("$phone", tel),
                ("$note", comment),
                ("$id", id));
        });
        return existing with
        {
            Name = siteName,
            Address = addr,
            ContactName = contact,
            Phone = tel,
            Note = comment
        };
    }

    public Site GetSite(long id) =>
        LoadSites(includeArchived: true).FirstOrDefault(site => site.Id == id)
        ?? throw new InvalidOperationException(TextCatalog.Get("SiteMissing"));

    public IReadOnlyList<Site> SearchSites(string? query, bool includeArchived)
    {
        var sites = LoadSites(includeArchived);
        var text = query?.Trim();
        if (string.IsNullOrEmpty(text))
            return sites;
        return sites.Where(site => ContainsText(site.Name, text) || ContainsText(site.Address, text)).ToList();
    }

    public ArchivePreview PreviewEmployeeArchive(long employeeId, DateOnly today)
    {
        var employee = GetEmployee(employeeId);
        return new ArchivePreview(DescribeEmployeeArchive(employee, today));
    }

    public void ArchiveEmployee(long employeeId, DateOnly today)
    {
        InTransaction(() =>
        {
            var employee = GetEmployee(employeeId);
            var tomorrow = today.AddDays(1);
            var yesterday = today.AddDays(-1);
            Exec("UPDATE employees SET is_archived = 1 WHERE id = $id", ("$id", employee.Id));
            Exec(
                "DELETE FROM assignment_days WHERE employee_id = $id AND date >= $tomorrow",
                ("$id", employee.Id),
                ("$tomorrow", D(tomorrow)));

            foreach (var absence in LoadAbsences(employee.Id, null, null))
            {
                if (absence.StartDate >= tomorrow)
                {
                    Exec("DELETE FROM absences WHERE id = $id", ("$id", absence.Id));
                }
                else if (absence.StartDate < today && (absence.EndDate is null || absence.EndDate.Value >= today))
                {
                    Exec(
                        "UPDATE absences SET end_date = $end WHERE id = $id",
                        ("$end", D(yesterday)),
                        ("$id", absence.Id));
                }
                else if (absence.StartDate == today && (absence.EndDate is null || absence.EndDate.Value > today))
                {
                    Exec(
                        "UPDATE absences SET end_date = $end WHERE id = $id",
                        ("$end", D(today)),
                        ("$id", absence.Id));
                }
            }
        });
    }

    public void RestoreEmployee(long employeeId)
    {
        GetEmployee(employeeId);
        Exec("UPDATE employees SET is_archived = 0 WHERE id = $id", ("$id", employeeId));
    }

    public ArchivePreview PreviewSiteArchive(long siteId, DateOnly today)
    {
        var site = GetSite(siteId);
        return new ArchivePreview(DescribeSiteArchive(site, today));
    }

    public void ArchiveSite(long siteId, DateOnly today)
    {
        InTransaction(() =>
        {
            var site = GetSite(siteId);
            var tomorrow = today.AddDays(1);
            Exec("UPDATE sites SET is_archived = 1 WHERE id = $id", ("$id", site.Id));
            Exec(
                "DELETE FROM assignment_days WHERE site_id = $id AND date >= $tomorrow",
                ("$id", site.Id),
                ("$tomorrow", D(tomorrow)));
        });
    }

    public void RestoreSite(long siteId)
    {
        var site = GetSite(siteId);
        InTransaction(() =>
        {
            EnsureActiveSiteName(site.Name, site.Id);
            Exec("UPDATE sites SET is_archived = 0 WHERE id = $id", ("$id", site.Id));
        });
    }

    private List<string> DescribeEmployeeArchive(Employee employee, DateOnly today)
    {
        var tomorrow = today.AddDays(1);
        var yesterday = today.AddDays(-1);
        var lines = new List<string>();
        var sites = LoadSites(includeArchived: true).ToDictionary(site => site.Id);
        foreach (var assignment in LoadAssignments(employee.Id, null, tomorrow, null))
        {
            var siteName = sites.TryGetValue(assignment.SiteId, out var site) ? site.Name : TextCatalog.Get("SiteWord");
            lines.Add(TextCatalog.Format("AssignmentWillClear", siteName, TextCatalog.FormatDate(assignment.Date)));
        }

        foreach (var absence in LoadAbsences(employee.Id, null, null))
        {
            var label = Labels.Absence(absence.Kind, absence.Note, absence.IsOpen);
            var period = Labels.Period(absence.StartDate, absence.EndDate);
            if (absence.StartDate >= tomorrow)
                lines.Add(TextCatalog.Format("AbsenceWillDelete", label, period));
            else if (absence.StartDate < today && (absence.EndDate is null || absence.EndDate.Value >= today))
                lines.Add(TextCatalog.Format("AbsenceEndBecomes", label, period, TextCatalog.FormatDate(yesterday)));
            else if (absence.StartDate == today && (absence.EndDate is null || absence.EndDate.Value > today))
                lines.Add(TextCatalog.Format("AbsenceEndBecomes", label, period, TextCatalog.FormatDate(today)));
        }

        return lines;
    }

    private List<string> DescribeSiteArchive(Site site, DateOnly today)
    {
        var tomorrow = today.AddDays(1);
        var people = LoadEmployees(includeArchived: true).ToDictionary(employee => employee.Id);
        var lines = new List<string>();
        foreach (var assignment in LoadAssignments(null, site.Id, tomorrow, null))
        {
            var name = people.TryGetValue(assignment.EmployeeId, out var employee)
                ? employee.FullName
                : TextCatalog.Get("EmployeeWord");
            lines.Add(TextCatalog.Format(
                "SiteAssignmentWillClear",
                name,
                TextCatalog.FormatDate(assignment.Date),
                site.Name));
        }

        return lines;
    }

    private void EnsureActiveSiteName(string name, long? exceptId)
    {
        var clash = LoadSites(includeArchived: true).Any(site =>
            !site.IsArchived
            && site.Id != exceptId
            && string.Equals(site.Name, name, StringComparison.OrdinalIgnoreCase));
        if (clash)
            throw new InvalidOperationException(TextCatalog.Get("DuplicateSiteName"));
    }

    private List<Employee> LoadEmployees(bool includeArchived)
    {
        var sql = includeArchived
            ? """
              SELECT id, last_name, first_name, specialty, phone, note, is_archived
              FROM employees
              ORDER BY is_archived, last_name, first_name, id
              """
            : """
              SELECT id, last_name, first_name, specialty, phone, note, is_archived
              FROM employees
              WHERE is_archived = 0
              ORDER BY last_name, first_name, id
              """;
        return Query(sql, ReadEmployee);
    }

    private List<Site> LoadSites(bool includeArchived)
    {
        var sql = includeArchived
            ? """
              SELECT id, name, address, contact_name, phone, note, is_archived
              FROM sites
              ORDER BY is_archived, name, id
              """
            : """
              SELECT id, name, address, contact_name, phone, note, is_archived
              FROM sites
              WHERE is_archived = 0
              ORDER BY name, id
              """;
        return Query(sql, ReadSite);
    }

    private static Employee ReadEmployee(SqliteDataReader reader) => new(
        reader.GetInt64(reader.GetOrdinal("id")),
        reader.GetString(reader.GetOrdinal("last_name")),
        reader.GetString(reader.GetOrdinal("first_name")),
        ReadString(reader, "specialty"),
        ReadString(reader, "phone"),
        ReadString(reader, "note"),
        reader.GetInt64(reader.GetOrdinal("is_archived")) != 0);

    private static Site ReadSite(SqliteDataReader reader) => new(
        reader.GetInt64(reader.GetOrdinal("id")),
        reader.GetString(reader.GetOrdinal("name")),
        ReadString(reader, "address"),
        ReadString(reader, "contact_name"),
        ReadString(reader, "phone"),
        ReadString(reader, "note"),
        reader.GetInt64(reader.GetOrdinal("is_archived")) != 0);
}
