using Microsoft.Data.Sqlite;

namespace TimeManagementMaster.Core;

public sealed partial class TimeManagementDatabase
{
    public ChangePlan PlanAssignments(IReadOnlyList<AssignmentOp> operations) =>
        BuildAssignmentPlan(operations);

    public void ApplyAssignments(IReadOnlyList<AssignmentOp> operations, bool replaceConflicts)
    {
        InTransaction(() =>
        {
            var plan = BuildAssignmentPlan(operations);
            if (plan.IsBlocked)
                throw new InvalidOperationException(string.Join(Environment.NewLine, plan.Blockers));
            if (plan.HasConflicts && !replaceConflicts)
                throw new InvalidOperationException(TextCatalog.Get("ConflictsRemain"));

            foreach (var group in operations.Distinct().GroupBy(op => op.EmployeeId))
            {
                var dates = group.Select(op => op.Date).Distinct().ToList();
                foreach (var absence in LoadAbsences(group.Key, null, null))
                {
                    if (!dates.Any(absence.Covers))
                        continue;
                    if (absence.Kind == AbsenceKind.Sick && absence.EndDate is null)
                        throw new InvalidOperationException(TextCatalog.Get("OpenSickNoAssign"));

                    RewriteAbsence(absence, PeriodMath.SubtractDates(absence.StartDate, absence.EndDate, dates));
                }

                foreach (var op in group)
                    UpsertAssignment(op);
            }
        });
    }

    public void ClearAssignments(long employeeId, IReadOnlyList<DateOnly> dates) =>
        ClearAssignments(dates.Select(date => new DayKey(employeeId, date)).ToList());

    public void ClearAssignments(IReadOnlyList<DayKey> days)
    {
        if (days.Count == 0)
            return;

        InTransaction(() =>
        {
            foreach (var day in days.Distinct())
            {
                Exec(
                    "DELETE FROM assignment_days WHERE employee_id = $employee AND date = $date",
                    ("$employee", day.EmployeeId),
                    ("$date", D(day.Date)));
            }
        });
    }

    public ChangePlan PlanAbsence(AbsenceDraft draft) => BuildAbsencePlan(draft);

    public void ApplyAbsence(AbsenceDraft draft, bool replaceConflicts)
    {
        InTransaction(() =>
        {
            var plan = BuildAbsencePlan(draft);
            if (plan.IsBlocked)
                throw new InvalidOperationException(string.Join(Environment.NewLine, plan.Blockers));
            if (plan.HasConflicts && !replaceConflicts)
                throw new InvalidOperationException(TextCatalog.Get("ConflictsRemain"));

            var note = NormalizeNote(draft);
            foreach (var other in LoadAbsences(draft.EmployeeId, null, null).Where(row => row.Id != draft.AbsenceId))
            {
                if (!PeriodMath.Overlaps(other.StartDate, other.EndDate, draft.StartDate, draft.EndDate))
                    continue;
                RewriteAbsence(other, PeriodMath.SubtractRange(other.StartDate, other.EndDate, draft.StartDate, draft.EndDate));
            }

            Exec(
                """
                DELETE FROM assignment_days
                WHERE employee_id = $employee
                  AND date >= $start
                  AND ($end IS NULL OR date <= $end)
                """,
                ("$employee", draft.EmployeeId),
                ("$start", D(draft.StartDate)),
                ("$end", draft.EndDate is null ? null : D(draft.EndDate.Value)));

            if (draft.AbsenceId is null)
            {
                InsertAbsence(draft.EmployeeId, draft.Kind, draft.StartDate, draft.EndDate, note);
            }
            else
            {
                Exec(
                    """
                    UPDATE absences
                    SET kind = $kind, start_date = $start, end_date = $end, note = $note
                    WHERE id = $id
                    """,
                    ("$kind", KindToDb(draft.Kind)),
                    ("$start", D(draft.StartDate)),
                    ("$end", draft.EndDate is null ? null : D(draft.EndDate.Value)),
                    ("$note", note),
                    ("$id", draft.AbsenceId.Value));
            }
        });
    }

    public void DeleteAbsence(long absenceId)
    {
        var existing = LoadAbsences(null, null, null).FirstOrDefault(row => row.Id == absenceId);
        if (existing is null)
            throw new InvalidOperationException(TextCatalog.Get("AbsenceMissing"));
        Exec("DELETE FROM absences WHERE id = $id", ("$id", absenceId));
    }

    public CopyBatch BuildDayCopy(DateOnly source, DateOnly targetStart, DateOnly targetEnd, bool weekdaysOnly)
    {
        IReadOnlyList<DateOnly> targets;
        try
        {
            targets = CalendarDates.Expand(targetStart, targetEnd, weekdaysOnly);
        }
        catch (ArgumentException ex)
        {
            return new CopyBatch([], ex.Message);
        }

        if (targets.Count == 0)
            return new CopyBatch([], TextCatalog.Get("NoDaysInRange"));

        var sourceRows = LoadAssignments(null, null, source, source);
        if (sourceRows.Count == 0)
            return new CopyBatch([], TextCatalog.Get("SourceEmpty"));

        var ops = new List<AssignmentOp>();
        foreach (var target in targets)
        {
            foreach (var row in sourceRows)
                ops.Add(new AssignmentOp(row.EmployeeId, row.SiteId, target, row.Time));
        }

        return new CopyBatch(ops, null);
    }

    public CopyBatch BuildWeekCopy(DateOnly sourceDate, DateOnly targetStart, DateOnly targetEnd, bool weekdaysOnly)
    {
        IReadOnlyList<DateOnly> targets;
        try
        {
            targets = CalendarDates.Expand(targetStart, targetEnd, weekdaysOnly);
        }
        catch (ArgumentException ex)
        {
            return new CopyBatch([], ex.Message);
        }

        if (targets.Count == 0)
            return new CopyBatch([], TextCatalog.Get("NoDaysInRange"));

        var monday = CalendarDates.MondayOf(sourceDate);
        var sourceRows = LoadAssignments(null, null, monday, monday.AddDays(6));
        if (sourceRows.Count == 0)
            return new CopyBatch([], TextCatalog.Get("SourceEmpty"));

        var byOffset = sourceRows
            .GroupBy(row => ((int)row.Date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7)
            .ToDictionary(group => group.Key, group => group.ToList());

        var ops = new List<AssignmentOp>();
        foreach (var target in targets)
        {
            var offset = ((int)target.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            if (!byOffset.TryGetValue(offset, out var rows))
                continue;
            foreach (var row in rows)
                ops.Add(new AssignmentOp(row.EmployeeId, row.SiteId, target, row.Time));
        }

        if (ops.Count == 0)
            return new CopyBatch([], TextCatalog.Get("SourceEmptyDays"));

        return new CopyBatch(ops, null);
    }

    private ChangePlan BuildAssignmentPlan(IReadOnlyList<AssignmentOp> operations)
    {
        var blockers = new List<string>();
        var raw = new List<RawConflict>();
        var list = operations.Distinct().ToList();
        if (list.Count == 0)
            return new ChangePlan(new[] { TextCatalog.Get("NoDaysToWrite") }, Array.Empty<ConflictRow>(), 0);

        if (list.GroupBy(op => (op.EmployeeId, op.Date)).Any(group => group.Select(op => (op.SiteId, op.Time)).Distinct().Count() > 1))
            return new ChangePlan(new[] { TextCatalog.Get("TwoSitesOneDay") }, Array.Empty<ConflictRow>(), 0);

        var employees = LoadEmployees(includeArchived: true).ToDictionary(employee => employee.Id);
        var sites = LoadSites(includeArchived: true).ToDictionary(site => site.Id);
        var changes = 0;

        foreach (var group in list.GroupBy(op => op.EmployeeId))
        {
            if (!employees.TryGetValue(group.Key, out var employee))
            {
                blockers.Add(TextCatalog.Format("EmployeeMissingId", group.Key));
                continue;
            }

            if (employee.IsArchived)
            {
                blockers.Add(TextCatalog.Format("EmployeeArchivedAssign", employee.FullName));
                continue;
            }

            var absences = LoadAbsences(employee.Id, null, null);
            var assignments = LoadAssignments(employee.Id, null, null, null).ToDictionary(row => row.Date);
            var reportedOpenSick = false;

            foreach (var op in group)
            {
                if (op.Time?.ValidationError() is string timeError)
                {
                    blockers.Add(timeError);
                    continue;
                }
                if (!sites.TryGetValue(op.SiteId, out var site))
                {
                    blockers.Add(TextCatalog.Format("SiteMissingId", op.SiteId));
                    continue;
                }

                if (site.IsArchived)
                {
                    blockers.Add(TextCatalog.Format("SiteArchivedAssign", site.Name));
                    continue;
                }

                var openSick = absences.FirstOrDefault(absence =>
                    absence.Kind == AbsenceKind.Sick && absence.EndDate is null && absence.Covers(op.Date));
                if (openSick is not null)
                {
                    if (!reportedOpenSick)
                    {
                        blockers.Add(TextCatalog.Format(
                            "OpenSickBlocks",
                            employee.FullName,
                            TextCatalog.FormatDate(openSick.StartDate)));
                        reportedOpenSick = true;
                    }

                    continue;
                }

                var requested = AssignmentDescription(site.Name, op.Time);
                var closedAbsence = absences.FirstOrDefault(absence => absence.Covers(op.Date));
                if (closedAbsence is not null)
                {
                    changes++;
                    raw.Add(new RawConflict(
                        employee.Id,
                        employee.FullName,
                        employee.Specialty,
                        op.Date,
                        null,
                        requested,
                        Labels.Absence(closedAbsence.Kind, closedAbsence.Note, closedAbsence.IsOpen)));
                    continue;
                }

                if (assignments.TryGetValue(op.Date, out var existing))
                {
                    if (existing.SiteId == op.SiteId && existing.Time == op.Time)
                        continue;
                    changes++;
                    var existingName = sites.TryGetValue(existing.SiteId, out var existingSite)
                        ? existingSite.Name
                        : TextCatalog.Get("SiteWord");
                    raw.Add(new RawConflict(
                        employee.Id,
                        employee.FullName,
                        employee.Specialty,
                        op.Date,
                        null,
                        requested,
                        AssignmentDescription(existingName, existing.Time)));
                    continue;
                }

                changes++;
            }
        }

        return new ChangePlan(blockers.Distinct().ToList(), GroupConflicts(raw), changes);
    }

    private ChangePlan BuildAbsencePlan(AbsenceDraft draft)
    {
        var blockers = new List<string>();
        string? note = null;
        try
        {
            note = NormalizeNote(draft);
        }
        catch (InvalidOperationException ex)
        {
            blockers.Add(ex.Message);
        }

        if (draft.EndDate is null && draft.Kind != AbsenceKind.Sick)
            blockers.Add(TextCatalog.Get("OpenEndOnlySick"));
        if (draft.EndDate is not null && draft.EndDate.Value < draft.StartDate)
            blockers.Add(TextCatalog.Get("EndBeforeStart"));

        var employees = LoadEmployees(includeArchived: true).ToDictionary(employee => employee.Id);
        if (!employees.TryGetValue(draft.EmployeeId, out var employee))
        {
            blockers.Add(TextCatalog.Get("EmployeeMissing"));
            return new ChangePlan(blockers.Distinct().ToList(), Array.Empty<ConflictRow>(), 0);
        }

        if (employee.IsArchived)
            blockers.Add(TextCatalog.Format("EmployeeArchivedAbsence", employee.FullName));

        AbsenceRow? current = null;
        if (draft.AbsenceId is long absenceId)
        {
            current = LoadAbsences(draft.EmployeeId, null, null).FirstOrDefault(row => row.Id == absenceId);
            if (current is null)
                blockers.Add(TextCatalog.Get("AbsenceMissing"));
        }

        if (blockers.Count > 0)
            return new ChangePlan(blockers.Distinct().ToList(), Array.Empty<ConflictRow>(), 0);

        var requested = Labels.Absence(draft.Kind, note, draft.EndDate is null);
        var raw = new List<RawConflict>();
        foreach (var other in LoadAbsences(draft.EmployeeId, null, null).Where(row => row.Id != draft.AbsenceId))
        {
            if (!PeriodMath.Overlaps(other.StartDate, other.EndDate, draft.StartDate, draft.EndDate))
                continue;
            raw.Add(new RawConflict(
                employee.Id,
                employee.FullName,
                employee.Specialty,
                null,
                PeriodMath.OverlapText(other.StartDate, other.EndDate, draft.StartDate, draft.EndDate),
                requested,
                Labels.Absence(other.Kind, other.Note, other.IsOpen)));
        }

        var sites = LoadSites(includeArchived: true).ToDictionary(site => site.Id);
        foreach (var assignment in LoadAssignments(draft.EmployeeId, null, draft.StartDate, draft.EndDate))
        {
            var siteName = sites.TryGetValue(assignment.SiteId, out var site) ? site.Name : TextCatalog.Get("SiteWord");
            raw.Add(new RawConflict(
                employee.Id,
                employee.FullName,
                employee.Specialty,
                assignment.Date,
                null,
                requested,
                AssignmentDescription(siteName, assignment.Time)));
        }

        var conflicts = GroupConflicts(raw);
        var same = current is not null
            && current.Kind == draft.Kind
            && current.StartDate == draft.StartDate
            && current.EndDate == draft.EndDate
            && string.Equals(current.Note, note, StringComparison.Ordinal);
        var changes = same && conflicts.Count == 0 ? 0 : 1;
        return new ChangePlan(Array.Empty<string>(), conflicts, changes);
    }

    private void UpsertAssignment(AssignmentOp op)
    {
        var existing = LoadAssignments(op.EmployeeId, null, op.Date, op.Date).FirstOrDefault();
        if (existing is null)
        {
            Exec(
                """
                INSERT INTO assignment_days(employee_id, site_id, date)
                VALUES ($employee, $site, $date)
                """,
                ("$employee", op.EmployeeId),
                ("$site", op.SiteId),
                ("$date", D(op.Date)));
        }
        else if (existing.SiteId != op.SiteId)
        {
            Exec(
                "UPDATE assignment_days SET site_id = $site WHERE id = $id",
                ("$site", op.SiteId),
                ("$id", existing.Id));
        }

        var id = existing?.Id ?? LastId();
        if (op.Time is not WorkTime time)
        {
            Exec("DELETE FROM work_times WHERE assignment_id = $id", ("$id", id));
            return;
        }
        Exec(
            """
            INSERT INTO work_times(assignment_id, start_minute, end_minute, lunch_start_minute, lunch_end_minute, next_day)
            VALUES($id, $start, $end, $lunchStart, $lunchEnd, $next)
            ON CONFLICT(assignment_id) DO UPDATE SET start_minute=$start, end_minute=$end,
                lunch_start_minute=$lunchStart, lunch_end_minute=$lunchEnd, next_day=$next
            """,
            ("$id", id), ("$start", WorkTime.Minute(time.Start)), ("$end", WorkTime.Minute(time.End)),
            ("$lunchStart", time.LunchStart is TimeOnly lunchStart ? WorkTime.Minute(lunchStart) : null),
            ("$lunchEnd", time.LunchEnd is TimeOnly lunchEnd ? WorkTime.Minute(lunchEnd) : null),
            ("$next", time.EndsNextDay ? 1 : 0));
    }

    private static string AssignmentDescription(string name, WorkTime? time) => Labels.SiteRequest(name)
        + (time is null ? " · " + TextCatalog.Get("TimeNotSet") : $" · {time.RangeText} · {TextCatalog.Get("Lunch")}: {time.LunchText} · {time.HoursText}");

    private void RewriteAbsence(AbsenceRow absence, List<(DateOnly Start, DateOnly? End)> parts)
    {
        if (parts.Count == 0)
        {
            Exec("DELETE FROM absences WHERE id = $id", ("$id", absence.Id));
            return;
        }

        var firstEnd = parts[0].End;
        Exec(
            "UPDATE absences SET start_date = $start, end_date = $end WHERE id = $id",
            ("$start", D(parts[0].Start)),
            ("$end", firstEnd is null ? null : D(firstEnd.Value)),
            ("$id", absence.Id));

        for (var index = 1; index < parts.Count; index++)
            InsertAbsence(absence.EmployeeId, absence.Kind, parts[index].Start, parts[index].End, absence.Note);
    }

    private void InsertAbsence(long employeeId, AbsenceKind kind, DateOnly start, DateOnly? end, string? note)
    {
        Exec(
            """
            INSERT INTO absences(employee_id, kind, start_date, end_date, note)
            VALUES ($employee, $kind, $start, $end, $note)
            """,
            ("$employee", employeeId),
            ("$kind", KindToDb(kind)),
            ("$start", D(start)),
            ("$end", end is null ? null : D(end.Value)),
            ("$note", note));
    }

    private static string? NormalizeNote(AbsenceDraft draft)
    {
        var note = Clean(draft.Note);
        if (draft.Kind != AbsenceKind.Other)
            return null;
        if (string.IsNullOrEmpty(note))
            throw new InvalidOperationException(TextCatalog.Get("OtherNoteRequired"));
        if (note.Length > 50)
            throw new InvalidOperationException(TextCatalog.Get("OtherNoteTooLong"));
        return note;
    }

    private static List<ConflictRow> GroupConflicts(List<RawConflict> raw) =>
        raw.GroupBy(item => (item.EmployeeId, item.EmployeeName, item.Specialty, item.Requested, item.Existing))
            .Select(group =>
            {
                var dated = group.Where(item => item.Date is not null).Select(item => item.Date!.Value).ToList();
                var texts = group.Where(item => item.DateText is not null).Select(item => item.DateText!).Distinct().ToList();
                var dates = dated.Count > 0 ? CalendarDates.FormatList(dated) : string.Join("; ", texts);
                return new ConflictRow(
                    group.Key.EmployeeId,
                    group.Key.EmployeeName,
                    group.Key.Specialty,
                    dates,
                    group.Key.Requested,
                    group.Key.Existing);
            })
            .OrderBy(row => row.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Dates, StringComparer.Ordinal)
            .ToList();

    private List<AssignmentRow> LoadAssignments(long? employeeId, long? siteId, DateOnly? from, DateOnly? to) =>
        Query(
            """
            SELECT a.id, a.employee_id, a.site_id, a.date, t.start_minute, t.end_minute,
                   t.lunch_start_minute, t.lunch_end_minute, t.next_day
            FROM assignment_days a LEFT JOIN work_times t ON t.assignment_id = a.id
            WHERE ($employee IS NULL OR employee_id = $employee)
              AND ($site IS NULL OR site_id = $site)
              AND ($from IS NULL OR date >= $from)
              AND ($to IS NULL OR date <= $to)
            ORDER BY date, a.id
            """,
            ReadAssignment,
            ("$employee", employeeId),
            ("$site", siteId),
            ("$from", from is null ? null : D(from.Value)),
            ("$to", to is null ? null : D(to.Value)));

    private List<AbsenceRow> LoadAbsences(long? employeeId, DateOnly? from, DateOnly? to) =>
        Query(
            """
            SELECT id, employee_id, kind, start_date, end_date, note
            FROM absences
            WHERE ($employee IS NULL OR employee_id = $employee)
              AND ($from IS NULL OR end_date IS NULL OR end_date >= $from)
              AND ($to IS NULL OR start_date <= $to)
            ORDER BY start_date, id
            """,
            ReadAbsence,
            ("$employee", employeeId),
            ("$from", from is null ? null : D(from.Value)),
            ("$to", to is null ? null : D(to.Value)));

    private static AssignmentRow ReadAssignment(SqliteDataReader reader) => new(
        reader.GetInt64(reader.GetOrdinal("id")),
        reader.GetInt64(reader.GetOrdinal("employee_id")),
        reader.GetInt64(reader.GetOrdinal("site_id")),
        ReadDate(reader, "date"),
        reader.IsDBNull(reader.GetOrdinal("start_minute")) ? null : new WorkTime(
            TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(reader.GetInt32(reader.GetOrdinal("start_minute")))),
            TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(reader.GetInt32(reader.GetOrdinal("end_minute")))),
            ReadMinute(reader, "lunch_start_minute"), ReadMinute(reader, "lunch_end_minute"),
            reader.GetInt32(reader.GetOrdinal("next_day")) == 1));

    private static TimeOnly? ReadMinute(SqliteDataReader reader, string column) => reader.IsDBNull(reader.GetOrdinal(column))
        ? null : TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(reader.GetInt32(reader.GetOrdinal(column))));

    private static AbsenceRow ReadAbsence(SqliteDataReader reader) => new(
        reader.GetInt64(reader.GetOrdinal("id")),
        reader.GetInt64(reader.GetOrdinal("employee_id")),
        ParseKind(reader.GetString(reader.GetOrdinal("kind"))),
        ReadDate(reader, "start_date"),
        ReadDateOrNull(reader, "end_date"),
        ReadString(reader, "note"));

    private static string KindToDb(AbsenceKind kind) => kind switch
    {
        AbsenceKind.Sick => "Sick",
        AbsenceKind.Vacation => "Vacation",
        AbsenceKind.Unpaid => "Unpaid",
        AbsenceKind.Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static AbsenceKind ParseKind(string value) => value switch
    {
        "Sick" => AbsenceKind.Sick,
        "Vacation" => AbsenceKind.Vacation,
        "Unpaid" => AbsenceKind.Unpaid,
        "Other" => AbsenceKind.Other,
        _ => throw new InvalidOperationException(TextCatalog.Format("UnknownKind", value))
    };

    private sealed record RawConflict(
        long EmployeeId,
        string EmployeeName,
        string? Specialty,
        DateOnly? Date,
        string? DateText,
        string Requested,
        string Existing);
}
