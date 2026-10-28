namespace TimeManagementMaster.Core;

public enum AbsenceKind
{
    Sick,
    Vacation,
    Unpaid,
    Other
}

public enum CellKind
{
    Empty,
    Assignment,
    Sick,
    Vacation,
    Unpaid,
    Other
}

public sealed record Employee(
    long Id,
    string LastName,
    string FirstName,
    string? Specialty,
    string? Phone,
    string? Note,
    bool IsArchived)
{
    public string FullName => $"{LastName} {FirstName}";
}

public sealed record Site(
    long Id,
    string Name,
    string? Address,
    string? ContactName,
    string? Phone,
    string? Note,
    bool IsArchived);

public sealed record AssignmentRow(long Id, long EmployeeId, long SiteId, DateOnly Date, WorkTime? Time = null);

public sealed record AbsenceRow(
    long Id,
    long EmployeeId,
    AbsenceKind Kind,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Note)
{
    public bool IsOpen => EndDate is null;

    public bool Covers(DateOnly date) =>
        date >= StartDate && (EndDate is null || date <= EndDate);
}

public sealed record DayCell(
    DateOnly Date,
    CellKind Kind,
    string Text,
    long? SiteId,
    string? SiteName,
    long? AbsenceId,
    WorkTime? Time = null);

public sealed record EmployeeCalendarRow(Employee Employee, IReadOnlyList<DayCell> Days);

public sealed record SiteDayGroup(Site Site, IReadOnlyList<Employee> Employees)
{
    public int Count => Employees.Count;
}

public sealed record AbsentPerson(Employee Employee, AbsenceRow Absence, string Text);

public sealed record DayBoard(
    DateOnly Date,
    IReadOnlyList<SiteDayGroup> Sites,
    IReadOnlyList<Employee> Unassigned,
    IReadOnlyList<AbsentPerson> Absences,
    int AssignedHeadcount,
    int ActiveEmployeeCount);

public sealed record AssignmentOp(long EmployeeId, long SiteId, DateOnly Date, WorkTime? Time = null);

public sealed record ConflictRow(
    long EmployeeId,
    string EmployeeName,
    string? Specialty,
    string Dates,
    string Requested,
    string Existing);

public sealed record ChangePlan(
    IReadOnlyList<string> Blockers,
    IReadOnlyList<ConflictRow> Conflicts,
    int ChangeCount)
{
    public bool IsBlocked => Blockers.Count > 0;

    public bool HasConflicts => Conflicts.Count > 0;
}

public sealed record AbsenceDraft(
    long? AbsenceId,
    long EmployeeId,
    AbsenceKind Kind,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Note);

public sealed record ArchivePreview(IReadOnlyList<string> Lines)
{
    public bool HasEffects => Lines.Count > 0;
}

public sealed record BackupInspection(bool IsCompatible, string Message);

public sealed record CopyBatch(IReadOnlyList<AssignmentOp> Operations, string? EmptyNotice);

public sealed record SiteAssignmentLine(
    DateOnly Date,
    long EmployeeId,
    string EmployeeName,
    string? Specialty);

public readonly record struct DayKey(long EmployeeId, DateOnly Date);

public static class Labels
{
    public static string Absence(AbsenceKind kind, string? note, bool openEnded) => kind switch
    {
        AbsenceKind.Sick when openEnded => TextCatalog.Get("SickOngoing"),
        AbsenceKind.Sick => TextCatalog.Get("Sick"),
        AbsenceKind.Vacation => TextCatalog.Get("Vacation"),
        AbsenceKind.Unpaid => TextCatalog.Get("Unpaid"),
        AbsenceKind.Other => TextCatalog.Format("OtherNote", note),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static string CsvCategory(CellKind kind) => kind switch
    {
        CellKind.Assignment => TextCatalog.Get("CsvSite"),
        CellKind.Sick => TextCatalog.Get("CsvSick"),
        CellKind.Vacation => TextCatalog.Get("CsvVacation"),
        CellKind.Unpaid => TextCatalog.Get("CsvUnpaid"),
        CellKind.Other => TextCatalog.Get("CsvOther"),
        _ => TextCatalog.Get("CsvUnassigned")
    };

    public static string SiteRequest(string siteName) => TextCatalog.Format("SiteRequest", siteName);

    public static string Period(DateOnly start, DateOnly? end)
    {
        if (end is null)
            return TextCatalog.Format("PeriodOngoing", TextCatalog.FormatDate(start));
        if (end.Value == start)
            return TextCatalog.FormatDate(start);
        return TextCatalog.Format("PeriodRange", TextCatalog.FormatDate(start), TextCatalog.FormatDate(end.Value));
    }
}
