using TimeManagementMaster.Core;

namespace TimeManagementMaster.Tests;

public class RulesTests
{
    [Fact]
    public void EmployeesAndSites_PersistAfterReopen()
    {
        using var fx = new TempDatabase();
        var employee = fx.AddEmployee("Иванов", "Иван", "Каменщик", "123");
        var site = fx.AddSite("Север", "ул. Лесная");
        Assert.Throws<InvalidOperationException>(() => fx.AddSite("север"));

        var db = fx.Reopen();
        Assert.Equal(TimeManagementDatabase.SchemaVersion, db.GetSchemaVersion());
        var loaded = Assert.Single(db.SearchEmployees("иван", includeArchived: false));
        Assert.Equal(employee.Id, loaded.Id);
        Assert.Equal("Каменщик", loaded.Specialty);
        Assert.Equal("123", loaded.Phone);
        var loadedSite = Assert.Single(db.SearchSites("лес", includeArchived: false));
        Assert.Equal(site.Id, loadedSite.Id);
        Assert.Equal("Север", loadedSite.Name);
    }

    [Fact]
    public void SeveralEmployees_OnOneSite_ForAWeek()
    {
        using var fx = new TempDatabase();
        var site = fx.AddSite("Север");
        var people = new[]
        {
            fx.AddEmployee("Иванов", "Иван"),
            fx.AddEmployee("Петров", "Пётр"),
            fx.AddEmployee("Сидоров", "Сидор")
        };
        var monday = TestDates.Monday(new DateOnly(2026, 10, 1));
        var dates = CalendarDates.Expand(monday, monday.AddDays(4), weekdaysOnly: true);
        var ops = people.SelectMany(person => dates.Select(date => new AssignmentOp(person.Id, site.Id, date))).ToList();

        fx.Db.ApplyAssignments(ops, replaceConflicts: false);

        Assert.Equal(15, fx.Db.ListAssignments().Count);
        foreach (var date in dates)
            Assert.Equal(3, fx.Db.ListAssignments().Count(row => row.Date == date && row.SiteId == site.Id));
    }

    [Fact]
    public void Move_ChangesOnlySelectedDays()
    {
        using var fx = new TempDatabase();
        var siteA = fx.AddSite("Север");
        var siteB = fx.AddSite("Юг");
        var employee = fx.AddEmployee("Иванов", "Иван");
        var monday = TestDates.Monday(new DateOnly(2026, 10, 1));
        var days = CalendarDates.Expand(monday, monday.AddDays(4), weekdaysOnly: true);
        fx.Db.ApplyAssignments(days.Select(date => new AssignmentOp(employee.Id, siteA.Id, date)).ToList(), false);

        var moved = new[] { monday.AddDays(2), monday.AddDays(3) };
        var plan = fx.Db.PlanAssignments(moved.Select(date => new AssignmentOp(employee.Id, siteB.Id, date)).ToList());
        Assert.True(plan.HasConflicts);
        Assert.Equal(5, fx.Db.ListAssignments().Count(row => row.SiteId == siteA.Id));

        fx.Db.ApplyAssignments(moved.Select(date => new AssignmentOp(employee.Id, siteB.Id, date)).ToList(), true);

        Assert.Equal(siteA.Id, fx.Db.GetEmployeeDay(employee.Id, monday).SiteId);
        Assert.Equal(siteA.Id, fx.Db.GetEmployeeDay(employee.Id, monday.AddDays(1)).SiteId);
        Assert.Equal(siteB.Id, fx.Db.GetEmployeeDay(employee.Id, monday.AddDays(2)).SiteId);
        Assert.Equal(siteB.Id, fx.Db.GetEmployeeDay(employee.Id, monday.AddDays(3)).SiteId);
        Assert.Equal(siteA.Id, fx.Db.GetEmployeeDay(employee.Id, monday.AddDays(4)).SiteId);

        var sameSite = fx.Db.PlanAssignments([new AssignmentOp(employee.Id, siteA.Id, monday)]);
        Assert.False(sameSite.HasConflicts);
        Assert.Equal(0, sameSite.ChangeCount);
    }

    [Fact]
    public void VacationSickUnpaid_AppearOnCoveredDates()
    {
        using var language = new LanguageScope(AppLanguage.Ru);
        using var fx = new TempDatabase();
        var employee = fx.AddEmployee("Иванов", "Иван");
        var vacationStart = new DateOnly(2026, 4, 6);
        var vacationEnd = new DateOnly(2026, 4, 8);
        var sickStart = new DateOnly(2026, 4, 13);
        var sickEnd = new DateOnly(2026, 4, 14);
        var unpaid = new DateOnly(2026, 4, 20);

        fx.Db.ApplyAbsence(new AbsenceDraft(null, employee.Id, AbsenceKind.Vacation, vacationStart, vacationEnd, null), false);
        fx.Db.ApplyAbsence(new AbsenceDraft(null, employee.Id, AbsenceKind.Sick, sickStart, sickEnd, null), false);
        fx.Db.ApplyAbsence(new AbsenceDraft(null, employee.Id, AbsenceKind.Unpaid, unpaid, unpaid, null), false);

        Assert.Equal("Отпуск", fx.Db.GetEmployeeDay(employee.Id, vacationStart).Text);
        Assert.Equal(CellKind.Vacation, fx.Db.GetEmployeeDay(employee.Id, vacationEnd).Kind);
        Assert.Equal(CellKind.Empty, fx.Db.GetEmployeeDay(employee.Id, vacationEnd.AddDays(1)).Kind);
        Assert.Equal("Больничный", fx.Db.GetEmployeeDay(employee.Id, sickStart).Text);
        Assert.Equal(CellKind.Sick, fx.Db.GetEmployeeDay(employee.Id, sickEnd).Kind);
        Assert.Equal(CellKind.Empty, fx.Db.GetEmployeeDay(employee.Id, sickEnd.AddDays(1)).Kind);
        Assert.Equal("Неоплачиваемый", fx.Db.GetEmployeeDay(employee.Id, unpaid).Text);
        Assert.Equal(CellKind.Empty, fx.Db.GetEmployeeDay(employee.Id, unpaid.AddDays(1)).Kind);
    }

    [Fact]
    public void OpenSick_BlocksAssignment_UntilClosedInclusive()
    {
        using var language = new LanguageScope(AppLanguage.Ru);
        using var fx = new TempDatabase();
        var employee = fx.AddEmployee("Иванов", "Иван");
        var site = fx.AddSite("Север");
        var start = new DateOnly(2026, 1, 5);
        var later = start.AddDays(31);
        fx.Db.ApplyAssignments([new AssignmentOp(employee.Id, site.Id, later)], false);

        var open = new AbsenceDraft(null, employee.Id, AbsenceKind.Sick, start, null, null);
        var opening = fx.Db.PlanAbsence(open);
        Assert.True(opening.HasConflicts);
        Assert.Throws<InvalidOperationException>(() => fx.Db.ApplyAbsence(open, false));
        Assert.Single(fx.Db.ListAssignments());
        Assert.Empty(fx.Db.ListAbsences());

        fx.Db.ApplyAbsence(open, true);
        Assert.Empty(fx.Db.ListAssignments());
        var covered = fx.Db.GetEmployeeDay(employee.Id, later);
        Assert.Equal(CellKind.Sick, covered.Kind);
        Assert.Equal("Больничный (продолжается)", covered.Text);

        var blocked = fx.Db.PlanAssignments([new AssignmentOp(employee.Id, site.Id, later)]);
        Assert.True(blocked.IsBlocked);
        Assert.Contains(blocked.Blockers, line => line.Contains("больнич", StringComparison.OrdinalIgnoreCase));
        Assert.Throws<InvalidOperationException>(() => fx.Db.ApplyAssignments([new AssignmentOp(employee.Id, site.Id, later)], true));
        Assert.Empty(fx.Db.ListAssignments());

        var absence = Assert.Single(fx.Db.ListAbsences());
        var close = start.AddDays(10);
        fx.Db.ApplyAbsence(new AbsenceDraft(absence.Id, employee.Id, AbsenceKind.Sick, start, close, null), false);
        Assert.Equal(CellKind.Sick, fx.Db.GetEmployeeDay(employee.Id, close).Kind);
        var next = close.AddDays(1);
        Assert.Equal(CellKind.Empty, fx.Db.GetEmployeeDay(employee.Id, next).Kind);
        fx.Db.ApplyAssignments([new AssignmentOp(employee.Id, site.Id, next)], false);
        Assert.Equal(site.Id, fx.Db.GetEmployeeDay(employee.Id, next).SiteId);
    }

    [Fact]
    public void Conflicts_WriteNothing_UntilReplaceAll()
    {
        using var fx = new TempDatabase();
        var employee = fx.AddEmployee("Иванов", "Иван", "Каменщик");
        var siteA = fx.AddSite("Север");
        var siteB = fx.AddSite("Юг");
        var monday = TestDates.Monday(new DateOnly(2026, 6, 1));
        fx.Db.ApplyAssignments([new AssignmentOp(employee.Id, siteA.Id, monday)], false);

        var second = new[] { new AssignmentOp(employee.Id, siteB.Id, monday) };
        var duplicate = fx.Db.PlanAssignments(second);
        Assert.True(duplicate.HasConflicts);
        Assert.Equal(employee.Specialty, Assert.Single(duplicate.Conflicts).Specialty);
        Assert.Throws<InvalidOperationException>(() => fx.Db.ApplyAssignments(second, false));
        Assert.Equal(siteA.Id, Assert.Single(fx.Db.ListAssignments()).SiteId);

        fx.Db.ApplyAssignments(second, true);
        Assert.Equal(siteB.Id, Assert.Single(fx.Db.ListAssignments()).SiteId);

        var vacationStart = new DateOnly(2026, 3, 1);
        var vacationEnd = new DateOnly(2026, 3, 10);
        var hole = new DateOnly(2026, 3, 5);
        fx.Db.ApplyAbsence(new AbsenceDraft(null, employee.Id, AbsenceKind.Vacation, vacationStart, vacationEnd, null), false);
        var during = new[] { new AssignmentOp(employee.Id, siteA.Id, hole) };
        var absenceConflict = fx.Db.PlanAssignments(during);
        Assert.True(absenceConflict.HasConflicts);
        Assert.Throws<InvalidOperationException>(() => fx.Db.ApplyAssignments(during, false));
        Assert.Equal(vacationEnd, Assert.Single(fx.Db.ListAbsences()).EndDate);
        Assert.DoesNotContain(fx.Db.ListAssignments(), row => row.Date == hole);

        fx.Db.ApplyAssignments(during, true);
        Assert.Equal(siteA.Id, fx.Db.GetEmployeeDay(employee.Id, hole).SiteId);
        var parts = fx.Db.ListAbsences().OrderBy(row => row.StartDate).ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal(vacationStart, parts[0].StartDate);
        Assert.Equal(hole.AddDays(-1), parts[0].EndDate);
        Assert.Equal(hole.AddDays(1), parts[1].StartDate);
        Assert.Equal(vacationEnd, parts[1].EndDate);
    }

    [Fact]
    public void GroupAssignment_OneConflict_WritesNothingBeforeConfirm()
    {
        using var fx = new TempDatabase();
        var siteX = fx.AddSite("Старый");
        var siteY = fx.AddSite("Новый");
        var first = fx.AddEmployee("Иванов", "Иван", "Каменщик");
        var second = fx.AddEmployee("Петров", "Пётр", "Плотник");
        var third = fx.AddEmployee("Сидоров", "Сидор", "Маляр");
        var day = new DateOnly(2026, 5, 12);
        fx.Db.ApplyAssignments([new AssignmentOp(first.Id, siteX.Id, day)], false);

        var ops = new[]
        {
            new AssignmentOp(first.Id, siteY.Id, day),
            new AssignmentOp(second.Id, siteY.Id, day),
            new AssignmentOp(third.Id, siteY.Id, day)
        };
        var plan = fx.Db.PlanAssignments(ops);
        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(first.Id, conflict.EmployeeId);
        Assert.Equal("Каменщик", conflict.Specialty);
        Assert.Contains(day.ToString("dd.MM.yyyy"), conflict.Dates);
        Assert.Contains(siteY.Name, conflict.Requested);
        Assert.Contains(siteX.Name, conflict.Existing);
        Assert.Equal(1, fx.Db.ListAssignments().Count);
        Assert.DoesNotContain(fx.Db.ListAssignments(), row => row.EmployeeId == second.Id || row.EmployeeId == third.Id);

        Assert.Throws<InvalidOperationException>(() => fx.Db.ApplyAssignments(ops, false));
        Assert.Equal(1, fx.Db.ListAssignments().Count);
        Assert.Equal(siteX.Id, fx.Db.ListAssignments().Single().SiteId);

        fx.Db.ApplyAssignments(ops, true);
        Assert.Equal(3, fx.Db.ListAssignments().Count);
        Assert.All(new[] { first, second, third }, person =>
            Assert.Equal(siteY.Id, fx.Db.GetEmployeeDay(person.Id, day).SiteId));
    }

    [Fact]
    public void ClearWorkday_ReturnsToUnassigned_SaturdayDoesNot()
    {
        using var fx = new TempDatabase();
        var employee = fx.AddEmployee("Иванов", "Иван");
        var site = fx.AddSite("Север");
        var friday = TestDates.Next(DayOfWeek.Friday, new DateOnly(2026, 10, 1));
        var saturday = friday.AddDays(1);
        fx.Db.ApplyAssignments(
            [new AssignmentOp(employee.Id, site.Id, friday), new AssignmentOp(employee.Id, site.Id, saturday)],
            false);
        Assert.Equal(CellKind.Assignment, fx.Db.GetEmployeeDay(employee.Id, saturday).Kind);
        Assert.DoesNotContain(fx.Db.ListUnassigned(friday), person => person.Id == employee.Id);

        fx.Db.ClearAssignments(employee.Id, [friday]);
        Assert.Contains(fx.Db.ListUnassigned(friday), person => person.Id == employee.Id);
        Assert.Equal(CellKind.Empty, fx.Db.GetEmployeeDay(employee.Id, friday).Kind);

        fx.Db.ClearAssignments(employee.Id, [saturday]);
        Assert.Empty(fx.Db.ListUnassigned(saturday));
        Assert.DoesNotContain(fx.Db.ListUnassigned(saturday), person => person.Id == employee.Id);
        Assert.Equal(CellKind.Empty, fx.Db.GetEmployeeDay(employee.Id, saturday).Kind);
        Assert.DoesNotContain(fx.Db.ListAssignments(), row => row.Date == saturday);
    }

    [Fact]
    public void PastAssignment_RemainsAfterLaterEdits()
    {
        using var fx = new TempDatabase();
        var employee = fx.AddEmployee("Иванов", "Иван");
        var siteA = fx.AddSite("Север");
        var siteB = fx.AddSite("Юг");
        var monday = TestDates.Monday(new DateOnly(2026, 9, 1));
        var wednesday = monday.AddDays(2);
        fx.Db.ApplyAssignments(
            [new AssignmentOp(employee.Id, siteA.Id, monday), new AssignmentOp(employee.Id, siteA.Id, wednesday)],
            false);
        fx.Db.ApplyAssignments([new AssignmentOp(employee.Id, siteB.Id, wednesday)], true);

        Assert.Equal(siteA.Id, fx.Db.GetEmployeeDay(employee.Id, monday).SiteId);
        Assert.Equal(siteB.Id, fx.Db.GetEmployeeDay(employee.Id, wednesday).SiteId);
        Assert.Equal(2, fx.Db.ListAssignments().Count);
    }

    [Fact]
    public void ArchiveEmployee_KeepsPast_DropsFuture_BlocksNewAssignment()
    {
        using var fx = new TempDatabase();
        var employee = fx.AddEmployee("Иванов", "Иван");
        var other = fx.AddEmployee("Петров", "Пётр");
        var site = fx.AddSite("Север");
        var today = TestDates.Next(DayOfWeek.Wednesday, new DateOnly(2026, 10, 1));
        var yesterday = today.AddDays(-1);
        var tomorrow = today.AddDays(1);
        var sickPerson = fx.AddEmployee("Смирнов", "Степан");
        fx.Db.ApplyAssignments(
            [
                new AssignmentOp(employee.Id, site.Id, yesterday),
                new AssignmentOp(employee.Id, site.Id, today),
                new AssignmentOp(employee.Id, site.Id, tomorrow)
            ],
            false);
        fx.Db.ApplyAbsence(new AbsenceDraft(null, sickPerson.Id, AbsenceKind.Sick, today.AddDays(-8), null, null), false);
        fx.Db.ApplyAbsence(new AbsenceDraft(null, employee.Id, AbsenceKind.Vacation, today.AddDays(-30), today.AddDays(-20), null), false);
        fx.Db.ApplyAbsence(new AbsenceDraft(null, employee.Id, AbsenceKind.Unpaid, today.AddDays(10), today.AddDays(12), null), false);
        fx.Db.ApplyAbsence(new AbsenceDraft(null, other.Id, AbsenceKind.Vacation, today, today.AddDays(3), null), false);

        var preview = fx.Db.PreviewEmployeeArchive(employee.Id, today);
        Assert.NotEmpty(preview.Lines);
        Assert.Contains(fx.Db.ListAssignments(), row => row.EmployeeId == employee.Id && row.Date == tomorrow);

        fx.Db.ArchiveEmployee(employee.Id, today);

        Assert.True(fx.Db.GetEmployee(employee.Id).IsArchived);
        Assert.Contains(fx.Db.ListAssignments(), row => row.EmployeeId == employee.Id && row.Date == yesterday);
        Assert.Contains(fx.Db.ListAssignments(), row => row.EmployeeId == employee.Id && row.Date == today);
        Assert.DoesNotContain(fx.Db.ListAssignments(), row => row.EmployeeId == employee.Id && row.Date == tomorrow);

        fx.Db.ArchiveEmployee(sickPerson.Id, today);
        var sick = fx.Db.ListAbsences(sickPerson.Id).Single(row => row.Kind == AbsenceKind.Sick);
        Assert.Equal(today.AddDays(-8), sick.StartDate);
        Assert.Equal(yesterday, sick.EndDate);
        var past = fx.Db.ListAbsences(employee.Id).Single(row => row.Kind == AbsenceKind.Vacation);
        Assert.Equal(today.AddDays(-30), past.StartDate);
        Assert.Equal(today.AddDays(-20), past.EndDate);
        Assert.DoesNotContain(fx.Db.ListAbsences(employee.Id), row => row.Kind == AbsenceKind.Unpaid);

        fx.Db.ArchiveEmployee(other.Id, today);
        var trimmedToday = fx.Db.ListAbsences(other.Id).Single();
        Assert.Equal(today, trimmedToday.StartDate);
        Assert.Equal(today, trimmedToday.EndDate);

        var blocked = fx.Db.PlanAssignments([new AssignmentOp(employee.Id, site.Id, today.AddDays(7))]);
        Assert.True(blocked.IsBlocked);
        Assert.Throws<InvalidOperationException>(() =>
            fx.Db.ApplyAssignments([new AssignmentOp(employee.Id, site.Id, today.AddDays(7))], true));
        Assert.Equal(CellKind.Assignment, fx.Db.GetEmployeeDay(employee.Id, yesterday).Kind);
    }

    [Fact]
    public void ArchiveSite_DropsFutureAssignments_BlocksNewUse()
    {
        using var fx = new TempDatabase();
        var employee = fx.AddEmployee("Иванов", "Иван");
        var site = fx.AddSite("Север");
        var today = TestDates.Next(DayOfWeek.Wednesday, new DateOnly(2026, 10, 1));
        var yesterday = today.AddDays(-1);
        var tomorrow = today.AddDays(1);
        fx.Db.ApplyAssignments(
            [new AssignmentOp(employee.Id, site.Id, yesterday), new AssignmentOp(employee.Id, site.Id, tomorrow)],
            false);

        var preview = fx.Db.PreviewSiteArchive(site.Id, today);
        Assert.NotEmpty(preview.Lines);
        Assert.Contains(fx.Db.ListAssignments(), row => row.Date == tomorrow);

        fx.Db.ArchiveSite(site.Id, today);

        Assert.True(fx.Db.GetSite(site.Id).IsArchived);
        Assert.Contains(fx.Db.ListAssignments(), row => row.Date == yesterday && row.SiteId == site.Id);
        Assert.DoesNotContain(fx.Db.ListAssignments(), row => row.Date == tomorrow);
        Assert.Contains(fx.Db.ListUnassigned(tomorrow), person => person.Id == employee.Id);
        Assert.DoesNotContain(fx.Db.ListUnassigned(yesterday), person => person.Id == employee.Id);

        var blocked = fx.Db.PlanAssignments([new AssignmentOp(employee.Id, site.Id, today.AddDays(7))]);
        Assert.True(blocked.IsBlocked);
        Assert.Throws<InvalidOperationException>(() =>
            fx.Db.ApplyAssignments([new AssignmentOp(employee.Id, site.Id, today.AddDays(7))], true));
        Assert.Empty(fx.Db.ListAssignments().Where(row => row.Date == today.AddDays(7)));
        Assert.Equal(site.Name, fx.Db.GetEmployeeDay(employee.Id, yesterday).SiteName);
    }

    [Fact]
    public void Rename_KeepsIds_ShowsNewNameOnPastDays()
    {
        using var fx = new TempDatabase();
        var employee = fx.AddEmployee("Иванов", "Иван");
        var site = fx.AddSite("Север");
        var day = TestDates.Monday(new DateOnly(2026, 5, 1));
        fx.Db.ApplyAssignments([new AssignmentOp(employee.Id, site.Id, day)], false);

        fx.Db.UpdateEmployee(employee.Id, "Новиков", "Иван", "Каменщик", null, null);
        fx.Db.UpdateSite(site.Id, "Новый объект", "ул. Новая", null, null, null);

        var cell = fx.Db.GetEmployeeDay(employee.Id, day);
        Assert.Equal(site.Id, cell.SiteId);
        Assert.Equal("Новый объект", cell.SiteName);
        var assignment = Assert.Single(fx.Db.ListAssignments());
        Assert.Equal(employee.Id, assignment.EmployeeId);
        Assert.Equal(site.Id, assignment.SiteId);
        var renamed = Assert.Single(fx.Db.SearchEmployees("Новиков", false));
        Assert.Equal(employee.Id, renamed.Id);
        Assert.Equal(CellKind.Assignment, fx.Db.GetEmployeeCalendar(day, day, "Новиков", site.Id).Single().Days.Single().Kind);
    }

    [Fact]
    public void OtherAbsence_RequiresNote_AndShowsInCellAndCsv()
    {
        using var language = new LanguageScope(AppLanguage.Ru);
        using var fx = new TempDatabase();
        var employee = fx.AddEmployee("Иванов", "Иван", "Каменщик");
        var day = new DateOnly(2026, 4, 15);
        var missing = new AbsenceDraft(null, employee.Id, AbsenceKind.Other, day, day, "  ");
        var rejected = fx.Db.PlanAbsence(missing);
        Assert.True(rejected.IsBlocked);
        Assert.Throws<InvalidOperationException>(() => fx.Db.ApplyAbsence(missing, true));
        Assert.Empty(fx.Db.ListAbsences());

        fx.Db.ApplyAbsence(new AbsenceDraft(null, employee.Id, AbsenceKind.Other, day, day, "обучение"), false);
        Assert.Equal("Другое: обучение", fx.Db.GetEmployeeDay(employee.Id, day).Text);
        var csv = fx.Db.ExportCsv(day, day);
        Assert.Contains("другое", csv);
        Assert.Contains("обучение", csv);
        Assert.Contains("Каменщик", csv);
    }

    [Fact]
    public void UnpaidOverlap_TrimsVacation_OnlyAfterConfirm()
    {
        using var fx = new TempDatabase();
        var employee = fx.AddEmployee("Иванов", "Иван");
        var start = new DateOnly(2026, 3, 1);
        var seventh = new DateOnly(2026, 3, 7);
        var eighth = new DateOnly(2026, 3, 8);
        var tenth = new DateOnly(2026, 3, 10);
        var twelfth = new DateOnly(2026, 3, 12);
        fx.Db.ApplyAbsence(new AbsenceDraft(null, employee.Id, AbsenceKind.Vacation, start, tenth, null), false);

        var unpaid = new AbsenceDraft(null, employee.Id, AbsenceKind.Unpaid, eighth, twelfth, null);
        var plan = fx.Db.PlanAbsence(unpaid);
        Assert.True(plan.HasConflicts);
        Assert.Throws<InvalidOperationException>(() => fx.Db.ApplyAbsence(unpaid, false));
        Assert.Equal(tenth, Assert.Single(fx.Db.ListAbsences()).EndDate);

        fx.Db.ApplyAbsence(unpaid, true);
        var vacation = fx.Db.ListAbsences().Single(row => row.Kind == AbsenceKind.Vacation);
        var unpaidRow = fx.Db.ListAbsences().Single(row => row.Kind == AbsenceKind.Unpaid);
        Assert.Equal(start, vacation.StartDate);
        Assert.Equal(seventh, vacation.EndDate);
        Assert.Equal(eighth, unpaidRow.StartDate);
        Assert.Equal(twelfth, unpaidRow.EndDate);
        Assert.Equal(CellKind.Vacation, fx.Db.GetEmployeeDay(employee.Id, seventh).Kind);
        Assert.Equal(CellKind.Unpaid, fx.Db.GetEmployeeDay(employee.Id, eighth).Kind);
    }

    [Fact]
    public void WeekCopy_ByWeekday_EmptySourceDoesNotClear_NoPartialWrite()
    {
        using var fx = new TempDatabase();
        var siteA = fx.AddSite("Север");
        var siteB = fx.AddSite("Юг");
        var siteC = fx.AddSite("Запад");
        var siteD = fx.AddSite("Восток");
        var first = fx.AddEmployee("Иванов", "Иван");
        var second = fx.AddEmployee("Петров", "Пётр");
        var third = fx.AddEmployee("Сидоров", "Сидор");
        var fourth = fx.AddEmployee("Орлов", "Олег");
        var sourceMonday = TestDates.Monday(new DateOnly(2026, 10, 5));
        var targetMonday = sourceMonday.AddDays(7);
        var laterMonday = sourceMonday.AddDays(14);

        fx.Db.ApplyAssignments(
            [
                new AssignmentOp(first.Id, siteA.Id, sourceMonday),
                new AssignmentOp(third.Id, siteA.Id, sourceMonday.AddDays(2)),
                new AssignmentOp(first.Id, siteA.Id, sourceMonday.AddDays(5))
            ],
            false);
        fx.Db.ApplyAbsence(new AbsenceDraft(null, fourth.Id, AbsenceKind.Vacation, sourceMonday, sourceMonday, null), false);
        fx.Db.ApplyAssignments(
            [
                new AssignmentOp(first.Id, siteB.Id, targetMonday),
                new AssignmentOp(first.Id, siteC.Id, targetMonday.AddDays(1)),
                new AssignmentOp(second.Id, siteD.Id, targetMonday)
            ],
            false);

        var copy = fx.Db.BuildWeekCopy(sourceMonday, targetMonday, targetMonday.AddDays(6), weekdaysOnly: false);
        Assert.Contains(copy.Operations, op => op.EmployeeId == first.Id && op.Date == targetMonday && op.SiteId == siteA.Id);
        Assert.Contains(copy.Operations, op => op.EmployeeId == third.Id && op.Date == targetMonday.AddDays(2));
        Assert.Contains(copy.Operations, op => op.EmployeeId == first.Id && op.Date == targetMonday.AddDays(5));
        Assert.DoesNotContain(copy.Operations, op => op.Date == targetMonday.AddDays(1));
        Assert.DoesNotContain(copy.Operations, op => op.EmployeeId == second.Id || op.EmployeeId == fourth.Id);

        var plan = fx.Db.PlanAssignments(copy.Operations);
        Assert.True(plan.HasConflicts);
        Assert.Equal(siteB.Id, fx.Db.GetEmployeeDay(first.Id, targetMonday).SiteId);
        Assert.Equal(siteC.Id, fx.Db.GetEmployeeDay(first.Id, targetMonday.AddDays(1)).SiteId);
        Assert.Equal(siteD.Id, fx.Db.GetEmployeeDay(second.Id, targetMonday).SiteId);
        Assert.Equal(CellKind.Empty, fx.Db.GetEmployeeDay(third.Id, targetMonday.AddDays(2)).Kind);
        Assert.Equal(CellKind.Empty, fx.Db.GetEmployeeDay(fourth.Id, targetMonday).Kind);

        Assert.Throws<InvalidOperationException>(() => fx.Db.ApplyAssignments(copy.Operations, false));
        Assert.Equal(CellKind.Empty, fx.Db.GetEmployeeDay(third.Id, targetMonday.AddDays(2)).Kind);
        Assert.Equal(siteB.Id, fx.Db.GetEmployeeDay(first.Id, targetMonday).SiteId);

        fx.Db.ApplyAssignments(copy.Operations, true);
        Assert.Equal(siteA.Id, fx.Db.GetEmployeeDay(first.Id, targetMonday).SiteId);
        Assert.Equal(siteC.Id, fx.Db.GetEmployeeDay(first.Id, targetMonday.AddDays(1)).SiteId);
        Assert.Equal(siteD.Id, fx.Db.GetEmployeeDay(second.Id, targetMonday).SiteId);
        Assert.Equal(siteA.Id, fx.Db.GetEmployeeDay(third.Id, targetMonday.AddDays(2)).SiteId);
        Assert.Equal(siteA.Id, fx.Db.GetEmployeeDay(first.Id, targetMonday.AddDays(5)).SiteId);
        Assert.Equal(CellKind.Empty, fx.Db.GetEmployeeDay(fourth.Id, targetMonday).Kind);
        Assert.Single(fx.Db.ListAbsences(fourth.Id));

        var weekdays = fx.Db.BuildWeekCopy(sourceMonday, laterMonday, laterMonday.AddDays(6), weekdaysOnly: true);
        Assert.DoesNotContain(weekdays.Operations, op => CalendarDates.IsWeekend(op.Date));
        Assert.Contains(weekdays.Operations, op => op.Date == laterMonday && op.EmployeeId == first.Id);
        fx.Db.ApplyAssignments(weekdays.Operations, false);
        Assert.Equal(CellKind.Empty, fx.Db.GetEmployeeDay(first.Id, laterMonday.AddDays(5)).Kind);
        Assert.Equal(siteA.Id, fx.Db.GetEmployeeDay(first.Id, laterMonday).SiteId);
        Assert.Equal(siteA.Id, fx.Db.GetEmployeeDay(first.Id, targetMonday.AddDays(5)).SiteId);
    }

    [Fact]
    public void WeekdayMode_LeavesSaturdayEmpty_ExplicitSaturdayCanBeAssigned()
    {
        using var fx = new TempDatabase();
        var employee = fx.AddEmployee("Иванов", "Иван");
        var site = fx.AddSite("Север");
        var saturday = TestDates.Next(DayOfWeek.Saturday, new DateOnly(2026, 10, 1));
        var end = saturday.AddDays(8);
        var dates = CalendarDates.Expand(saturday, end, weekdaysOnly: true);
        Assert.DoesNotContain(dates, CalendarDates.IsWeekend);
        Assert.Contains(saturday.AddDays(2), dates);

        fx.Db.ApplyAssignments(dates.Select(date => new AssignmentOp(employee.Id, site.Id, date)).ToList(), false);
        Assert.Equal(CellKind.Empty, fx.Db.GetEmployeeDay(employee.Id, saturday).Kind);
        Assert.Equal(CellKind.Empty, fx.Db.GetEmployeeDay(employee.Id, saturday.AddDays(1)).Kind);
        Assert.Equal(CellKind.Assignment, fx.Db.GetEmployeeDay(employee.Id, saturday.AddDays(2)).Kind);

        fx.Db.ApplyAssignments([new AssignmentOp(employee.Id, site.Id, saturday)], false);
        Assert.Equal(site.Name, fx.Db.GetEmployeeDay(employee.Id, saturday).Text);
    }

    [Fact]
    public void SiteFilter_KeepsOtherDaysOfMatchingEmployee()
    {
        using var fx = new TempDatabase();
        var north = fx.AddSite("Север");
        var south = fx.AddSite("Юг");
        var kept = fx.AddEmployee("Иванов", "Иван");
        var other = fx.AddEmployee("Петров", "Пётр");
        var monday = TestDates.Monday(new DateOnly(2026, 8, 3));
        fx.Db.ApplyAssignments(
            [
                new AssignmentOp(kept.Id, south.Id, monday),
                new AssignmentOp(kept.Id, north.Id, monday.AddDays(2)),
                new AssignmentOp(other.Id, south.Id, monday)
            ],
            false);

        var rows = fx.Db.GetEmployeeCalendar(monday, monday.AddDays(4), null, north.Id);
        var row = Assert.Single(rows);
        Assert.Equal(kept.Id, row.Employee.Id);
        Assert.Equal(south.Id, row.Days.Single(cell => cell.Date == monday).SiteId);
        Assert.Equal(north.Id, row.Days.Single(cell => cell.Date == monday.AddDays(2)).SiteId);
    }

    [Fact]
    public void DispatcherFlow_AddEditArchiveRestore_AssignWeek_ClearOneDay()
    {
        using var fx = new TempDatabase();
        Assert.Throws<InvalidOperationException>(() => fx.Db.CreateEmployee("", "Anna", null, null, null));
        Assert.Throws<InvalidOperationException>(() => fx.Db.CreateEmployee("Meier", " ", null, null, null));

        var anna = fx.Db.CreateEmployee("Meier", "Anna", "Maurer", null, null);
        var lukas = fx.Db.CreateEmployee("Schmidt", "Lukas", null, null, null);
        var maria = fx.Db.CreateEmployee("Ionescu", "Maria", null, null, null);
        Assert.Equal(3, fx.Db.SearchEmployees(null, includeArchived: false).Count);

        Assert.Equal("Anne", fx.Db.UpdateEmployee(anna.Id, "Meier", "Anne", "Maurer", null, null).FirstName);
        Assert.Equal("Anne", fx.Db.GetEmployee(anna.Id).FirstName);

        var north = fx.Db.CreateSite("Nord", "Hafenstraße 4", null, null, null);
        var south = fx.Db.CreateSite("Süd", null, null, null, null);
        var monday = TestDates.Monday(new DateOnly(2026, 10, 5));
        var wednesday = monday.AddDays(2);
        var ops = new List<AssignmentOp>();
        foreach (var person in new[] { anna, lukas })
        {
            for (var day = 0; day < 5; day++)
                ops.Add(new AssignmentOp(person.Id, north.Id, monday.AddDays(day)));
        }

        for (var day = 0; day < 5; day++)
            ops.Add(new AssignmentOp(maria.Id, south.Id, monday.AddDays(day)));
        fx.Db.ApplyAssignments(ops, false);

        fx.Db.ClearAssignments(lukas.Id, [wednesday]);
        Assert.Contains(fx.Db.ListUnassigned(wednesday), person => person.Id == lukas.Id);
        Assert.Equal(CellKind.Empty, fx.Db.GetEmployeeDay(lukas.Id, wednesday).Kind);
        Assert.Equal(north.Id, fx.Db.GetEmployeeDay(lukas.Id, monday).SiteId);
        Assert.Equal(north.Id, fx.Db.GetEmployeeDay(lukas.Id, monday.AddDays(1)).SiteId);
        Assert.Equal(north.Id, fx.Db.GetEmployeeDay(anna.Id, wednesday).SiteId);
        Assert.Equal(south.Id, fx.Db.GetEmployeeDay(maria.Id, wednesday).SiteId);

        var freeDay = monday.AddDays(7);
        fx.Db.ApplyAbsence(new AbsenceDraft(null, maria.Id, AbsenceKind.Vacation, freeDay, freeDay, null), false);
        Assert.Equal(CellKind.Vacation, fx.Db.GetEmployeeDay(maria.Id, freeDay).Kind);

        var clash = new[] { new AssignmentOp(lukas.Id, south.Id, monday) };
        Assert.NotEmpty(fx.Db.PlanAssignments(clash).Conflicts);
        Assert.Throws<InvalidOperationException>(() => fx.Db.ApplyAssignments(clash, false));
        Assert.Equal(north.Id, fx.Db.GetEmployeeDay(lukas.Id, monday).SiteId);

        var afterWeek = monday.AddDays(14);
        fx.Db.ArchiveEmployee(anna.Id, afterWeek);
        Assert.True(fx.Db.GetEmployee(anna.Id).IsArchived);
        Assert.DoesNotContain(fx.Db.SearchEmployees(null, includeArchived: false), person => person.Id == anna.Id);
        Assert.Contains(fx.Db.SearchEmployees(null, includeArchived: true), person => person.Id == anna.Id);
        Assert.Equal(north.Id, fx.Db.GetEmployeeDay(anna.Id, monday).SiteId);

        fx.Db.RestoreEmployee(anna.Id);
        Assert.False(fx.Db.GetEmployee(anna.Id).IsArchived);
        Assert.Contains(fx.Db.SearchEmployees(null, includeArchived: false), person => person.Id == anna.Id);
    }
}
