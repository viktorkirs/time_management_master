using Microsoft.Data.Sqlite;
using TimeManagementMaster.Core;

namespace TimeManagementMaster.Tests;

public class HoursTests
{
    private static WorkTime Day => new(new(8, 0), new(17, 0), new(12, 0), new(13, 0));

    [Fact]
    public void LunchAndOvernightWorkAreCalculatedInMinutes()
    {
        Assert.Null(Day.ValidationError());
        Assert.Equal(480, Day.NetMinutes);
        Assert.Equal("8:00", Day.HoursText);
        var night = new WorkTime(new(22, 0), new(6, 0), new(2, 0), new(2, 30), true);
        Assert.Null(night.ValidationError());
        Assert.Equal(450, night.NetMinutes);
        Assert.Equal("7:30", night.HoursText);
        Assert.Equal(540, (Day with { LunchStart = null, LunchEnd = null }).NetMinutes);
    }

    [Fact]
    public void InvalidRangesAndLunchOutsideWorkAreRejected()
    {
        foreach (var time in new[]
        {
            Day with { End = new(7, 0) }, Day with { End = new(8, 0) }, Day with { EndsNextDay = true },
            Day with { LunchStart = new(7, 0), LunchEnd = new(8, 0) },
            Day with { LunchEnd = new(18, 0) }, Day with { LunchEnd = null },
            Day with { LunchEnd = new(12, 0) }, Day with { Start = new(8, 0, 30) }
        }) Assert.NotNull(time.ValidationError());
    }

    [Fact]
    public void TimeSurvivesReopenAndDayAndWeekCopy()
    {
        using var fx = new TempDatabase();
        var person = fx.AddEmployee("Meier", "Anna");
        var site = fx.AddSite("Nord");
        var date = new DateOnly(2026, 11, 2);
        fx.Db.ApplyAssignments([new(person.Id, site.Id, date, Day)], false);
        fx.Reopen();
        Assert.Equal(Day, fx.Db.GetEmployeeDay(person.Id, date).Time);
        var dayCopy = fx.Db.BuildDayCopy(date, date.AddDays(1), date.AddDays(1), false);
        fx.Db.ApplyAssignments(dayCopy.Operations, false);
        var weekCopy = fx.Db.BuildWeekCopy(date, date.AddDays(7), date.AddDays(13), false);
        fx.Db.ApplyAssignments(weekCopy.Operations, false);
        Assert.All(fx.Db.ListAssignments(), row => Assert.Equal(Day, row.Time));
        Assert.Equal(4, fx.Db.ListAssignments().Count);
    }

    [Fact]
    public void ChangingTimeOnSameSiteRequiresConfirmationAndInvalidBatchWritesNothing()
    {
        using var fx = new TempDatabase();
        var person = fx.AddEmployee("Meier", "Anna");
        var site = fx.AddSite("Nord");
        var date = new DateOnly(2026, 11, 2);
        fx.Db.ApplyAssignments([new(person.Id, site.Id, date, Day)], false);
        AssignmentOp[] changed = [new(person.Id, site.Id, date, Day with { End = new(16, 0) })];
        Assert.True(fx.Db.PlanAssignments(changed).HasConflicts);
        Assert.Throws<InvalidOperationException>(() => fx.Db.ApplyAssignments(changed, false));
        Assert.Equal(Day, fx.Db.GetEmployeeDay(person.Id, date).Time);
        fx.Db.ApplyAssignments(changed, true);
        Assert.Equal(420, fx.Db.GetEmployeeDay(person.Id, date).Time?.NetMinutes);
        AssignmentOp[] invalid = [new(person.Id, site.Id, date.AddDays(1), Day), new(person.Id, site.Id, date.AddDays(2), Day with { End = new(7, 0) })];
        Assert.True(fx.Db.PlanAssignments(invalid).IsBlocked);
        Assert.Throws<InvalidOperationException>(() => fx.Db.ApplyAssignments(invalid, true));
        Assert.Single(fx.Db.ListAssignments());
    }

    [Fact]
    public void AbsenceReplacesOnlyChosenDayAndItsHours()
    {
        using var fx = new TempDatabase();
        var person = fx.AddEmployee("Meier", "Anna");
        var site = fx.AddSite("Nord");
        var date = new DateOnly(2026, 11, 2);
        fx.Db.ApplyAssignments(Enumerable.Range(0, 3).Select(offset => new AssignmentOp(person.Id, site.Id, date.AddDays(offset), Day)).ToList(), false);
        var leave = new AbsenceDraft(null, person.Id, AbsenceKind.Sick, date.AddDays(1), date.AddDays(1), null);
        Assert.True(fx.Db.PlanAbsence(leave).HasConflicts);
        Assert.Throws<InvalidOperationException>(() => fx.Db.ApplyAbsence(leave, false));
        Assert.Equal(1440, fx.Db.GetMonthlyHours(2026, 11).Single().NetMinutes);
        fx.Db.ApplyAbsence(leave, true);
        Assert.Equal(CellKind.Sick, fx.Db.GetEmployeeDay(person.Id, date.AddDays(1)).Kind);
        Assert.Equal(Day, fx.Db.GetEmployeeDay(person.Id, date).Time);
        Assert.Equal(960, fx.Db.GetMonthlyHours(2026, 11).Single().NetMinutes);
        Assert.Equal(1, fx.Db.GetMonthlyHours(2026, 11).Single().SickDays);
    }

    [Fact]
    public void MonthlyReportClipsAbsencesAndSeparatesMissingTimes()
    {
        using var fx = new TempDatabase();
        var person = fx.AddEmployee("Meier", "Anna");
        var site = fx.AddSite("Nord; West");
        fx.Db.ApplyAssignments([
            new(person.Id, site.Id, new(2026, 11, 2), Day),
            new(person.Id, site.Id, new(2026, 11, 3), Day with { End = new(16, 30) }),
            new(person.Id, site.Id, new(2026, 11, 4)),
            new(person.Id, site.Id, new(2026, 12, 1), Day)
        ], false);
        fx.Db.ApplyAbsence(new(null, person.Id, AbsenceKind.Vacation, new(2026, 10, 30), new(2026, 11, 1), null), false);
        fx.Db.ApplyAbsence(new(null, person.Id, AbsenceKind.Sick, new(2026, 11, 29), null, null), true);
        var row = Assert.Single(fx.Db.GetMonthlyHours(2026, 11));
        Assert.Equal(3, row.AssignmentDays);
        Assert.Equal(2, row.TimedDays);
        Assert.Equal(1, row.MissingTimeDays);
        Assert.Equal("15:30", row.HoursText);
        Assert.Equal(15.5m, row.DecimalHours);
        Assert.Equal(1, row.VacationDays);
        Assert.Equal(2, row.SickDays);
        Assert.Equal(31, fx.Db.GetMonthlyHours(2026, 12).Single().SickDays);
        Assert.StartsWith("\uFEFF", fx.Db.ExportMonthlyHoursCsv(2026, 11));
        Assert.Contains("15:30", fx.Db.ExportMonthlyHoursCsv(2026, 11));
        Assert.Contains("\"Nord; West\"", fx.Db.ExportWorkDayDetailsCsv(2026, 11));
        Assert.Contains("12:00;13:00", fx.Db.ExportWorkDayDetailsCsv(2026, 11));
    }

    [Fact]
    public void VersionOneUpgradeBacksUpAndPreservesUntimedDaysAndCanRestoreOldBackup()
    {
        using var fx = new TempDatabase();
        var person = fx.AddEmployee("Meier", "Anna");
        var site = fx.AddSite("Nord");
        var date = new DateOnly(2026, 11, 2);
        fx.Db.ApplyAssignments([new(person.Id, site.Id, date)], false);
        fx.Db.Dispose();
        using (var connection = new SqliteConnection($"Data Source={fx.DbPath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE work_times; UPDATE meta SET value = '1' WHERE key = 'schema_version';";
            command.ExecuteNonQuery();
        }
        var oldBackup = Path.Combine(fx.Root, "old.sqlite");
        File.Copy(fx.DbPath, oldBackup);
        Assert.True(TimeManagementDatabase.Inspect(oldBackup).IsCompatible);
        fx.Reopen();
        Assert.Equal(2, fx.Db.GetSchemaVersion());
        Assert.Equal(site.Id, fx.Db.GetEmployeeDay(person.Id, date).SiteId);
        Assert.Null(fx.Db.GetEmployeeDay(person.Id, date).Time);
        Assert.Single(Directory.GetFiles(fx.BackupPath, "before-hours-*.sqlite"));
        fx.Reopen();
        Assert.Single(Directory.GetFiles(fx.BackupPath, "before-hours-*.sqlite"));
        fx.Db.ApplyAssignments([new(person.Id, site.Id, date, Day)], true);
        fx.Db.RestoreFrom(oldBackup);
        Assert.Null(fx.Db.GetEmployeeDay(person.Id, date).Time);
        Assert.NotEmpty(Directory.GetFiles(fx.BackupPath, "safety-*.sqlite"));
        using var original = new SqliteConnection($"Data Source={oldBackup};Mode=ReadOnly;Pooling=False");
        original.Open();
        using var check = original.CreateCommand();
        check.CommandText = "SELECT value FROM meta WHERE key='schema_version'";
        Assert.Equal("1", check.ExecuteScalar());
    }
}
