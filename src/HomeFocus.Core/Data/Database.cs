using Microsoft.Data.Sqlite;

namespace HomeFocus.Core.Data;

/// <summary>Opens the SQLite file and keeps its schema up to date.</summary>
public sealed class Database
{
    public const int SchemaVersion = 1;

    private readonly string _connectionString;

    public Database(string path)
    {
        Path = path;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        }.ToString();
    }

    public string Path { get; }

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HomeFocus", "data.db");

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    public void Initialize()
    {
        using var c = Open();
        Exec(c, "PRAGMA journal_mode=WAL;");
        var version = Convert.ToInt32(Scalar(c, "PRAGMA user_version;"));
        if (version < 1) CreateV1(c);
    }

    private static void CreateV1(SqliteConnection c)
    {
        using var tx = c.BeginTransaction();
        Exec(c, """
            CREATE TABLE devices (
                id          TEXT PRIMARY KEY,
                name        TEXT NOT NULL,
                kind        TEXT NOT NULL,
                created_utc INTEGER NOT NULL
            );
            CREATE TABLE categories (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                name       TEXT NOT NULL,
                color      TEXT NOT NULL,
                is_builtin INTEGER NOT NULL DEFAULT 0,
                sort_order INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE rules (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                pattern     TEXT NOT NULL,
                kind        TEXT NOT NULL DEFAULT 'app',
                category_id INTEGER NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
                is_user     INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE activity_log (
                id           TEXT PRIMARY KEY,
                device_id    TEXT NOT NULL REFERENCES devices(id),
                kind         TEXT NOT NULL,
                app_key      TEXT NOT NULL,
                display_name TEXT NOT NULL,
                url          TEXT NULL,
                category_id  INTEGER NULL REFERENCES categories(id) ON DELETE SET NULL,
                start_utc    INTEGER NOT NULL,
                end_utc      INTEGER NOT NULL,
                duration_ms  INTEGER NOT NULL
            );
            CREATE INDEX ix_activity_start ON activity_log(start_utc);
            CREATE INDEX ix_activity_end ON activity_log(end_utc);
            CREATE TABLE settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """, tx);

        var cats = new (string Name, string Color, string[] Patterns)[]
        {
            ("Práca", "#2F6FEB", ["devenv.exe", "code.exe", "rider64.exe", "idea64.exe", "winword.exe", "excel.exe",
                "powerpnt.exe", "onenote.exe", "windowsterminal.exe", "wt.exe", "powershell.exe", "pwsh.exe", "cmd.exe",
                "ssms.exe", "notepad++.exe", "figma.exe", "postman.exe", "github*.exe"]),
            ("Komunikácia", "#0E8F80", ["outlook.exe", "olk.exe", "ms-teams.exe", "teams.exe", "slack.exe", "zoom.exe",
                "thunderbird.exe", "signal.exe", "telegram.exe", "whatsapp*.exe"]),
            ("Zábava", "#E07A2E", ["spotify.exe", "steam.exe", "steamwebhelper.exe", "epicgameslauncher.exe", "vlc.exe",
                "netflix.exe", "battle.net.exe", "leagueclient*.exe", "riotclient*.exe"]),
            ("Sociálne siete", "#C9437A", ["discord.exe", "messenger.exe"]),
            ("Vzdelávanie", "#7452D1", ["anki.exe", "kindle.exe", "obsidian.exe"]),
            ("Ostatné", "#8A919E", []),
        };
        for (var i = 0; i < cats.Length; i++)
        {
            Exec(c, "INSERT INTO categories(name, color, is_builtin, sort_order) VALUES ($n, $c, 1, $o);", tx,
                ("$n", cats[i].Name), ("$c", cats[i].Color), ("$o", i));
            var id = (long)Scalar(c, "SELECT last_insert_rowid();", tx)!;
            foreach (var p in cats[i].Patterns)
                Exec(c, "INSERT INTO rules(pattern, kind, category_id, is_user) VALUES ($p, 'app', $c, 0);", tx,
                    ("$p", p), ("$c", id));
        }

        Exec(c, "INSERT INTO devices(id, name, kind, created_utc) VALUES ($id, $n, 'pc', $t);", tx,
            ("$id", Guid.NewGuid().ToString("N")), ("$n", Environment.MachineName),
            ("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        Exec(c, $"PRAGMA user_version = {SchemaVersion};", tx);
        tx.Commit();
    }

    internal static void Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string, object?)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    internal static object? Scalar(SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string, object?)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return cmd.ExecuteScalar();
    }
}
