using TimeManagementMaster.Core;

namespace TimeManagementMaster.Tests;

public sealed class TempDatabase : IDisposable
{
    public string Root { get; }

    public string DbPath { get; }

    public string BackupPath { get; }

    public TimeManagementDatabase Db { get; private set; }

    public TempDatabase()
    {
        Root = Path.Combine(Path.GetTempPath(), "tmm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        DbPath = Path.Combine(Root, "data.sqlite");
        BackupPath = Path.Combine(Root, "backups");
        Db = TimeManagementDatabase.Open(DbPath, BackupPath);
    }

    public TimeManagementDatabase Reopen()
    {
        Db.Dispose();
        Db = TimeManagementDatabase.Open(DbPath, BackupPath);
        return Db;
    }

    public Employee AddEmployee(string lastName, string firstName, string? specialty = null, string? phone = null)
        => Db.CreateEmployee(lastName, firstName, specialty, phone, null);

    public Site AddSite(string name, string? address = null)
        => Db.CreateSite(name, address, null, null, null);

    public void Dispose()
    {
        Db.Dispose();
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
        catch (IOException)
        {
            // A locked temp file must not fail the assertion that already ran.
        }
    }
}

internal static class TestDates
{
    public static DateOnly Next(DayOfWeek day, DateOnly from)
    {
        while (from.DayOfWeek != day)
            from = from.AddDays(1);
        return from;
    }

    public static DateOnly Monday(DateOnly from) => Next(DayOfWeek.Monday, from);
}
