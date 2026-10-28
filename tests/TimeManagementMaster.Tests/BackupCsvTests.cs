using System.Text;
using Microsoft.Data.Sqlite;
using TimeManagementMaster.Core;

namespace TimeManagementMaster.Tests;

public class BackupCsvTests
{
    [Fact]
    public void CorruptOrWrongSchema_DoesNotReplaceWorkingDatabase()
    {
        using var fx = new TempDatabase();
        fx.AddEmployee("Keep", "Anna");
        var corrupt = Path.Combine(fx.Root, "corrupt.sqlite");
        File.WriteAllBytes(corrupt, [1, 2, 3, 4, 5, 6, 7, 8]);
        var corruptCheck = TimeManagementDatabase.Inspect(corrupt);
        Assert.False(corruptCheck.IsCompatible);
        Assert.Throws<InvalidOperationException>(() => fx.Db.RestoreFrom(corrupt));
        Assert.Equal("Keep", fx.Db.SearchEmployees(null, false).Single().LastName);
        Assert.Empty(Directory.GetFiles(fx.BackupPath, "safety-*"));

        var wrong = Path.Combine(fx.Root, "wrong.sqlite");
        fx.Db.CreateManualBackup(wrong);
        using (var connection = new SqliteConnection($"Data Source={wrong};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE meta SET value = '999' WHERE key = 'schema_version';";
            command.ExecuteNonQuery();
        }

        var wrongCheck = TimeManagementDatabase.Inspect(wrong);
        Assert.False(wrongCheck.IsCompatible);
        Assert.Throws<InvalidOperationException>(() => fx.Db.RestoreFrom(wrong));
        Assert.Equal("Keep", fx.Db.SearchEmployees(null, false).Single().LastName);
        Assert.Empty(Directory.GetFiles(fx.BackupPath, "safety-*"));
        Assert.Equal(TimeManagementDatabase.SchemaVersion, fx.Db.GetSchemaVersion());
    }

    [Fact]
    public void ValidRestore_ReplacesDatabase_AndWritesSafetyCopy()
    {
        using var fx = new TempDatabase();
        fx.AddEmployee("Keep", "Anna");
        var backup = Path.Combine(fx.Root, "good.sqlite");
        fx.Db.CreateManualBackup(backup);
        fx.AddEmployee("New", "Boris");
        Assert.Equal(2, fx.Db.SearchEmployees(null, false).Count);

        fx.Db.RestoreFrom(backup);

        var people = fx.Db.SearchEmployees(null, false);
        Assert.Equal("Keep", Assert.Single(people).LastName);
        Assert.NotEmpty(Directory.GetFiles(fx.BackupPath, "safety-*.sqlite"));
        Assert.Equal(TimeManagementDatabase.SchemaVersion, fx.Reopen().GetSchemaVersion());
        Assert.Equal("Keep", fx.Db.SearchEmployees(null, false).Single().LastName);
    }

    [Fact]
    public void DailyBackup_CreatedOncePerLocalDate_AndRotationKeepsSafety()
    {
        using var fx = new TempDatabase();
        fx.AddEmployee("Before", "Backup");
        var firstDay = new DateOnly(2026, 10, 28);
        fx.Db.EnsureDailyBackup(firstDay);
        fx.AddEmployee("After", "Backup");
        fx.Db.EnsureDailyBackup(firstDay);

        var daily = Directory.GetFiles(fx.BackupPath, "daily-*.sqlite");
        Assert.Single(daily);
        using (var snapshot = TimeManagementDatabase.Open(daily[0], Path.Combine(fx.Root, "snapshot-backups")))
        {
            Assert.Equal("Before", Assert.Single(snapshot.SearchEmployees(null, true)).LastName);
        }

        fx.Db.EnsureDailyBackup(firstDay.AddDays(1));
        Assert.Equal(2, Directory.GetFiles(fx.BackupPath, "daily-*.sqlite").Length);

        for (var i = 0; i < 35; i++)
        {
            var date = new DateOnly(2024, 1, 1).AddDays(i);
            File.WriteAllText(Path.Combine(fx.BackupPath, $"daily-{date:yyyy-MM-dd}.sqlite"), "x");
        }

        File.WriteAllText(Path.Combine(fx.BackupPath, "safety-keep.sqlite"), "safe");
        File.WriteAllText(Path.Combine(fx.BackupPath, "manual-note.sqlite"), "manual");
        fx.Db.EnsureDailyBackup(new DateOnly(2026, 10, 30));

        var kept = Directory.GetFiles(fx.BackupPath, "daily-*.sqlite");
        Assert.Equal(30, kept.Length);
        Assert.Contains(kept, path => path.EndsWith("daily-2026-10-30.sqlite", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(fx.BackupPath, "safety-keep.sqlite")));
        Assert.True(File.Exists(Path.Combine(fx.BackupPath, "manual-note.sqlite")));
    }

    [Fact]
    public void Csv_HasColumns_OmitsEmptyWeekends()
    {
        using var language = new LanguageScope(AppLanguage.Ru);
        using var fx = new TempDatabase();
        var assigned = fx.AddEmployee("Иванов", "Иван", "Каменщик");
        var idle = fx.AddEmployee("Петров", "Пётр", "Плотник");
        var site = fx.AddSite("Север", "ул. Лесная");
        var thursday = TestDates.Next(DayOfWeek.Thursday, new DateOnly(2026, 10, 1));
        var friday = thursday.AddDays(1);
        var saturday = thursday.AddDays(2);
        Assert.Equal(DayOfWeek.Saturday, saturday.DayOfWeek);
        fx.Db.ApplyAssignments([new AssignmentOp(assigned.Id, site.Id, friday)], false);
        fx.Db.ApplyAssignments([new AssignmentOp(assigned.Id, site.Id, saturday)], false);

        var csv = fx.Db.ExportCsv(thursday, saturday);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(csv);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var lines = csv.Replace("\r", "").TrimStart('\uFEFF').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("дата;фамилия;имя;специальность;категория;объект;пояснение;адрес", lines[0]);
        Assert.Contains(lines, line => line.StartsWith($"{thursday:yyyy-MM-dd};Иванов;Иван;Каменщик;нераспределён;;;", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith($"{thursday:yyyy-MM-dd};Петров;Пётр;Плотник;нераспределён;;;", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains($"{friday:yyyy-MM-dd};Иванов;Иван;Каменщик;объект;Север;;ул. Лесная", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains($"{friday:yyyy-MM-dd};Петров;Пётр;Плотник;нераспределён;;;", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains($"{saturday:yyyy-MM-dd};Иванов;Иван;Каменщик;объект;Север;;ул. Лесная", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("Петров") && line.StartsWith($"{saturday:yyyy-MM-dd}", StringComparison.Ordinal));
        Assert.Equal(6, lines.Length);
    }
}
