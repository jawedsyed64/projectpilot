using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ProjectPilot.Data;

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectPilot");

    public static string DbPath => Path.Combine(DataDir, "projectpilot.db");
    public static string AttachmentsDir => Path.Combine(DataDir, "attachments");
    public static string BackupsDir => Path.Combine(DataDir, "backups");
    public static string ErrorLog => Path.Combine(DataDir, "error.log");

    public static void Ensure()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(AttachmentsDir);
        Directory.CreateDirectory(BackupsDir);
    }
}

/// <summary>SQLite database with simple versioned schema migrations (PRAGMA user_version).</summary>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        c.Exec("PRAGMA foreign_keys = ON;");
        return c;
    }

    public void Migrate()
    {
        using var c = Open();
        var version = Convert.ToInt64(c.Scalar("PRAGMA user_version;") ?? 0L);
        if (version < 1)
        {
            c.Exec("BEGIN;");
            try
            {
                c.Exec(Schema.V1);
                c.Exec("PRAGMA user_version = 1;");
                c.Exec("COMMIT;");
            }
            catch
            {
                c.Exec("ROLLBACK;");
                throw;
            }
        }
        // Future phases add: if (version < 2) { ... }
    }
}

internal static class Schema
{
    public const string V1 = @"
CREATE TABLE IF NOT EXISTS projects(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    objectives TEXT NOT NULL DEFAULT '',
    outcomes TEXT NOT NULL DEFAULT '',
    start_date TEXT NULL,
    target_date TEXT NULL,
    owner TEXT NOT NULL DEFAULT '',
    team_members TEXT NOT NULL DEFAULT '',
    stakeholders TEXT NOT NULL DEFAULT '',
    priority TEXT NOT NULL DEFAULT 'Medium',
    budget REAL NULL,
    status TEXT NOT NULL DEFAULT 'Pending',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS tasks(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    parent_id INTEGER NULL REFERENCES tasks(id) ON DELETE CASCADE,
    title TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    notes TEXT NOT NULL DEFAULT '',
    assignee TEXT NOT NULL DEFAULT '',
    priority TEXT NOT NULL DEFAULT 'Medium',
    status TEXT NOT NULL DEFAULT 'NotStarted',
    start_date TEXT NULL,
    due_date TEXT NULL,
    progress INTEGER NOT NULL DEFAULT 0,
    recurrence TEXT NOT NULL DEFAULT 'None',
    completed_at TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_tasks_project ON tasks(project_id);
CREATE INDEX IF NOT EXISTS ix_tasks_parent ON tasks(parent_id);
CREATE TABLE IF NOT EXISTS task_dependencies(
    task_id INTEGER NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    depends_on_id INTEGER NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    PRIMARY KEY(task_id, depends_on_id)
);
CREATE TABLE IF NOT EXISTS attachments(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NULL REFERENCES projects(id) ON DELETE CASCADE,
    task_id INTEGER NULL REFERENCES tasks(id) ON DELETE CASCADE,
    file_name TEXT NOT NULL,
    stored_path TEXT NOT NULL,
    added_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS comments(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    task_id INTEGER NULL REFERENCES tasks(id) ON DELETE CASCADE,
    author TEXT NOT NULL DEFAULT '',
    text TEXT NOT NULL,
    created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS activity(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NULL REFERENCES projects(id) ON DELETE CASCADE,
    task_id INTEGER NULL,
    action TEXT NOT NULL,
    details TEXT NOT NULL DEFAULT '',
    created_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_activity_project ON activity(project_id);
CREATE TABLE IF NOT EXISTS settings(
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
";
}

/// <summary>Small helpers so we can write plain SQL with named parameters.</summary>
internal static class SqlExtensions
{
    private const string DateFormat = "yyyy-MM-ddTHH:mm:ss";

    public static SqliteCommand Cmd(this SqliteConnection c, string sql, params (string Name, object? Value)[] ps)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in ps)
            cmd.Parameters.AddWithValue(p.Name, ToDb(p.Value));
        return cmd;
    }

    public static object ToDb(object? v) => v switch
    {
        null => DBNull.Value,
        DateTime d => d.ToString(DateFormat, CultureInfo.InvariantCulture),
        System.Enum e => e.ToString(),
        bool b => b ? 1 : 0,
        _ => v
    };

    public static int Exec(this SqliteConnection c, string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = c.Cmd(sql, ps);
        return cmd.ExecuteNonQuery();
    }

    public static object? Scalar(this SqliteConnection c, string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = c.Cmd(sql, ps);
        var r = cmd.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    public static int LastId(this SqliteConnection c) =>
        Convert.ToInt32(c.Scalar("SELECT last_insert_rowid();"));

    public static List<T> Query<T>(this SqliteConnection c, string sql, Func<SqliteDataReader, T> map,
        params (string Name, object? Value)[] ps)
    {
        using var cmd = c.Cmd(sql, ps);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    public static string Str(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? "" : r.GetString(i);
    }

    public static int Int(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
    }

    public static int? NInt(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    }

    public static double? NDouble(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : Convert.ToDouble(r.GetValue(i), CultureInfo.InvariantCulture);
    }

    public static DateTime? NDate(this SqliteDataReader r, string col)
    {
        var s = r.Str(col);
        if (string.IsNullOrEmpty(s)) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    public static DateTime Date(this SqliteDataReader r, string col) => r.NDate(col) ?? DateTime.MinValue;

    public static T EnumOf<T>(this SqliteDataReader r, string col, T fallback) where T : struct, System.Enum =>
        System.Enum.TryParse<T>(r.Str(col), out var v) ? v : fallback;
}
