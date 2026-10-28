using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace TimeManagementMaster.Core;

public sealed partial class TimeManagementDatabase : IDisposable
{
    public const int SchemaVersion = 2;

    private static readonly Regex DailyBackupName = new(
        @"^daily-\d{4}-\d{2}-\d{2}\.sqlite$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly string _dbPath;
    private readonly string _backupDirectory;
    private SqliteConnection? _connection;
    private SqliteTransaction? _transaction;

    private TimeManagementDatabase(string dbPath, string backupDirectory)
    {
        _dbPath = Path.GetFullPath(dbPath);
        _backupDirectory = Path.GetFullPath(backupDirectory);
    }

    public string FilePath => _dbPath;

    public string BackupDirectory => _backupDirectory;

    public static string AppDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeManagementMaster");

    public static string DefaultDatabasePath => Path.Combine(AppDirectory, "data.sqlite");

    public static string DefaultBackupDirectory => Path.Combine(AppDirectory, "backups");

    public static TimeManagementDatabase OpenDefault() =>
        Open(DefaultDatabasePath, DefaultBackupDirectory);

    public static TimeManagementDatabase Open(string databasePath, string backupDirectory)
    {
        var fullDb = Path.GetFullPath(databasePath);
        var folder = Path.GetDirectoryName(fullDb);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        Directory.CreateDirectory(backupDirectory);

        var database = new TimeManagementDatabase(fullDb, backupDirectory);
        try
        {
            database.OpenConnection();
            return database;
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _transaction = null;
        _connection?.Dispose();
        _connection = null;
    }

    public int GetSchemaVersion()
    {
        var value = Scalar("SELECT value FROM meta WHERE key = 'schema_version'")?.ToString();
        if (!int.TryParse(value, out var version))
            throw new InvalidOperationException(TextCatalog.Get("NoSchema"));
        return version;
    }

    public static BackupInspection Inspect(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new BackupInspection(false, TextCatalog.Get("FileMissing"));

        try
        {
            using var connection = new SqliteConnection(ReadOnlyConnectionString(path));
            connection.Open();

            using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA integrity_check;";
                using var reader = check.ExecuteReader();
                var messages = new List<string>();
                while (reader.Read())
                    messages.Add(reader.GetString(0));
                if (messages.Count != 1 || !messages[0].Equals("ok", StringComparison.OrdinalIgnoreCase))
                    return new BackupInspection(false, TextCatalog.Get("FileCorrupt"));
            }

            foreach (var table in new[] { "meta", "employees", "sites", "assignment_days", "absences" })
            {
                using var exists = connection.CreateCommand();
                exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
                exists.Parameters.AddWithValue("$name", table);
                if (exists.ExecuteScalar() is null)
                    return new BackupInspection(false, TextCatalog.Get("NotThisApp"));
            }

            using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "SELECT value FROM meta WHERE key = 'schema_version' LIMIT 1;";
            var raw = versionCommand.ExecuteScalar()?.ToString();
            if (!int.TryParse(raw?.Trim(), out var version) || version is < 1 or > SchemaVersion)
                return new BackupInspection(false, TextCatalog.Get("BadSchema"));
            if (version == 2)
            {
                using var timing = connection.CreateCommand();
                timing.CommandText = "SELECT assignment_id, start_minute, end_minute, lunch_start_minute, lunch_end_minute, next_day FROM work_times LIMIT 0";
                timing.ExecuteNonQuery();
            }

            return new BackupInspection(true, TextCatalog.Get("FileOk"));
        }
        catch (SqliteException)
        {
            return new BackupInspection(false, TextCatalog.Get("NotAppOrCorrupt"));
        }
        catch (Exception)
        {
            return new BackupInspection(false, TextCatalog.Get("NotAppOrCorrupt"));
        }
    }

    public void EnsureDailyBackup(DateOnly localDate)
    {
        Directory.CreateDirectory(_backupDirectory);
        var destination = Path.Combine(_backupDirectory, $"daily-{localDate:yyyy-MM-dd}.sqlite");
        if (!File.Exists(destination))
            BackupTo(destination);
        RotateDailyBackups(30);
    }

    public void CreateManualBackup(string destinationPath) => BackupTo(destinationPath);

    public void RestoreFrom(string sourcePath)
    {
        var inspection = Inspect(sourcePath);
        if (!inspection.IsCompatible)
            throw new InvalidOperationException(inspection.Message);

        var source = Path.GetFullPath(sourcePath);
        if (string.Equals(source, _dbPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(TextCatalog.Get("SameFile"));

        Directory.CreateDirectory(_backupDirectory);
        var safety = NextSafetyPath();
        BackupTo(safety);

        try
        {
            ReplaceWorkingFile(source);
        }
        catch
        {
            try
            {
                ReplaceWorkingFile(safety);
            }
            catch (Exception restoreError)
            {
                throw new InvalidOperationException(
                    TextCatalog.Format("SafetyFailed", restoreError.Message),
                    restoreError);
            }

            throw;
        }
    }

    private void BackupTo(string destinationPath)
    {
        var full = Path.GetFullPath(destinationPath);
        var folder = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        if (File.Exists(full))
            File.Delete(full);

        using var destination = new SqliteConnection(ConnectionString(full));
        destination.Open();
        Connection.BackupDatabase(destination);
    }

    private void ReplaceWorkingFile(string sourcePath)
    {
        CloseConnection();
        DeleteSidecars(_dbPath);
        CopyOver(sourcePath, _dbPath);
        OpenConnection();
    }

    private string NextSafetyPath()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var path = Path.Combine(_backupDirectory, $"safety-{stamp}.sqlite");
        var index = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(_backupDirectory, $"safety-{stamp}-{index}.sqlite");
            index++;
        }

        return path;
    }

    private void RotateDailyBackups(int keep)
    {
        var files = Directory.GetFiles(_backupDirectory, "daily-*.sqlite")
            .Select(path => new FileInfo(path))
            .Where(file => DailyBackupName.IsMatch(file.Name))
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var old in files.Skip(keep))
            old.Delete();
    }

    private void OpenConnection()
    {
        _connection = new SqliteConnection(ConnectionString(_dbPath));
        _connection.Open();
        Exec("PRAGMA foreign_keys = ON;");
        Exec("PRAGMA journal_mode = DELETE;");
        Exec("PRAGMA busy_timeout = 5000;");

        if (!TableExists("meta"))
        {
            InTransaction(CreateSchema);
            return;
        }

        var value = Scalar("SELECT value FROM meta WHERE key = 'schema_version'")?.ToString();
        if (!int.TryParse(value, out var version) || version is < 1 or > SchemaVersion)
            throw new InvalidOperationException(TextCatalog.Format("BadDbVersion", value));
        if (version == 1)
        {
            // Keep a version-1 snapshot before introducing hours. Old days stay untimed.
            BackupTo(Path.Combine(_backupDirectory, $"before-hours-{Guid.NewGuid():N}.sqlite"));
            InTransaction(() =>
            {
                CreateWorkTimeTable();
                Exec("UPDATE meta SET value = '2' WHERE key = 'schema_version'");
            });
        }
    }

    private void CloseConnection()
    {
        _transaction?.Dispose();
        _transaction = null;
        _connection?.Dispose();
        _connection = null;
    }

    private void CreateSchema()
    {
        Exec(
            """
            CREATE TABLE meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            INSERT INTO meta(key, value) VALUES ('schema_version', '2');
            CREATE TABLE employees (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                last_name TEXT NOT NULL,
                first_name TEXT NOT NULL,
                specialty TEXT NULL,
                phone TEXT NULL,
                note TEXT NULL,
                is_archived INTEGER NOT NULL DEFAULT 0 CHECK (is_archived IN (0, 1)),
                CHECK (phone IS NULL OR length(phone) <= 40)
            );
            CREATE TABLE sites (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                address TEXT NULL,
                contact_name TEXT NULL,
                phone TEXT NULL,
                note TEXT NULL,
                is_archived INTEGER NOT NULL DEFAULT 0 CHECK (is_archived IN (0, 1)),
                CHECK (length(trim(name)) > 0),
                CHECK (phone IS NULL OR length(phone) <= 40)
            );
            CREATE UNIQUE INDEX ux_sites_active_name ON sites(name COLLATE NOCASE) WHERE is_archived = 0;
            CREATE TABLE assignment_days (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                employee_id INTEGER NOT NULL REFERENCES employees(id),
                site_id INTEGER NOT NULL REFERENCES sites(id),
                date TEXT NOT NULL,
                UNIQUE (employee_id, date)
            );
            CREATE INDEX ix_assignment_site_date ON assignment_days(site_id, date);
            CREATE INDEX ix_assignment_date ON assignment_days(date);
            CREATE TABLE absences (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                employee_id INTEGER NOT NULL REFERENCES employees(id),
                kind TEXT NOT NULL CHECK (kind IN ('Sick', 'Vacation', 'Unpaid', 'Other')),
                start_date TEXT NOT NULL,
                end_date TEXT NULL,
                note TEXT NULL,
                CHECK (kind = 'Sick' OR end_date IS NOT NULL),
                CHECK (end_date IS NULL OR end_date >= start_date),
                CHECK (kind <> 'Other' OR (note IS NOT NULL AND length(trim(note)) > 0 AND length(note) <= 50))
            );
            CREATE INDEX ix_absences_employee ON absences(employee_id, start_date);
            """);
        CreateWorkTimeTable();
    }

    private void CreateWorkTimeTable() => Exec(
        """
        CREATE TABLE work_times (
            assignment_id INTEGER PRIMARY KEY REFERENCES assignment_days(id) ON DELETE CASCADE,
            start_minute INTEGER NOT NULL CHECK(start_minute BETWEEN 0 AND 1439),
            end_minute INTEGER NOT NULL CHECK(end_minute BETWEEN 0 AND 1439),
            lunch_start_minute INTEGER NULL CHECK(lunch_start_minute BETWEEN 0 AND 1439),
            lunch_end_minute INTEGER NULL CHECK(lunch_end_minute BETWEEN 0 AND 1439),
            next_day INTEGER NOT NULL CHECK(next_day IN (0,1)),
            CHECK((lunch_start_minute IS NULL) = (lunch_end_minute IS NULL)),
            CHECK(end_minute + next_day * 1440 > start_minute),
            CHECK(end_minute + next_day * 1440 - start_minute <= 1440)
        );
        """);

    private bool TableExists(string name)
    {
        var value = Scalar(
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;",
            ("$name", name));
        return value is not null;
    }

    private SqliteConnection Connection =>
        _connection ?? throw new ObjectDisposedException(nameof(TimeManagementDatabase));

    private void InTransaction(Action action)
    {
        if (_transaction is not null)
        {
            action();
            return;
        }

        using var transaction = Connection.BeginTransaction();
        _transaction = transaction;
        try
        {
            action();
            transaction.Commit();
        }
        catch
        {
            try
            {
                transaction.Rollback();
            }
            catch (SqliteException)
            {
                // The original error is rethrown below.
            }

            throw;
        }
        finally
        {
            _transaction = null;
        }
    }

    private void Exec(string sql, params (string Name, object? Value)[] args)
    {
        try
        {
            using var command = Cmd(sql, args);
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(TextCatalog.Format("SaveDataFailed", ex.Message), ex);
        }
    }

    private object? Scalar(string sql, params (string Name, object? Value)[] args)
    {
        using var command = Cmd(sql, args);
        return command.ExecuteScalar();
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args)
    {
        using var command = Cmd(sql, args);
        using var reader = command.ExecuteReader();
        var list = new List<T>();
        while (reader.Read())
            list.Add(map(reader));
        return list;
    }

    private SqliteCommand Cmd(string sql, params (string Name, object? Value)[] args)
    {
        var command = Connection.CreateCommand();
        if (_transaction is not null)
            command.Transaction = _transaction;
        command.CommandText = sql;
        foreach (var (name, value) in args)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private long LastId() => Convert.ToInt64(Scalar("SELECT last_insert_rowid()"));

    private static string ConnectionString(string path) =>
        $"Data Source={path};Mode=ReadWriteCreate;Pooling=False";

    private static string ReadOnlyConnectionString(string path) =>
        $"Data Source={path};Mode=ReadOnly;Pooling=False";

    private static string D(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ReadDate(SqliteDataReader reader, string name) =>
        DateOnly.ParseExact(reader.GetString(reader.GetOrdinal(name)), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly? ReadDateOrNull(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? null
            : DateOnly.ParseExact(reader.GetString(ordinal), "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string? ReadString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string RequireText(string? value, string label)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            throw new InvalidOperationException(TextCatalog.Format("FieldRequired", TextCatalog.Get(label)));
        return text;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }

    private static string? CleanPhone(string? phone)
    {
        var text = Clean(phone);
        if (text is not null && text.Length > 40)
            throw new InvalidOperationException(TextCatalog.Get("PhoneTooLong"));
        return text;
    }

    private static bool ContainsText(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static void CopyOver(string source, string destination)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(50);
            }
        }
    }

    private static void DeleteSidecars(string dbPath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
