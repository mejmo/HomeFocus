using Microsoft.Data.Sqlite;

namespace HomeFocus.Core.Data;

/// <summary>Opens the SQLite file and keeps its schema up to date.</summary>
public sealed class Database
{
    public const int SchemaVersion = 4;

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

    /// <summary>
    /// %LOCALAPPDATA%\HomeFocus\data.db. Local, not Roaming: a roaming profile would copy the history to a
    /// domain file server, and the data must never leave this PC.
    /// </summary>
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HomeFocus", "data.db");

    /// <summary>Moves a database left in %APPDATA% by version 0.1.0 to <see cref="DefaultPath"/>.</summary>
    public static void MoveLegacyDatabase()
    {
        var legacyDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HomeFocus");
        var legacy = System.IO.Path.Combine(legacyDir, "data.db");
        if (!File.Exists(legacy) || File.Exists(DefaultPath)) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DefaultPath)!);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(legacy + suffix)) File.Move(legacy + suffix, DefaultPath + suffix);
        try { Directory.Delete(legacyDir); } catch (IOException) { /* not empty: leave it */ }
    }

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
        if (version < 2) MigrateV2(c);
        if (version < 3) MigrateV3(c);
        if (version < 4) MigrateV4(c);
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

        var cats = new (string Key, string Name, string Color, string[] Patterns)[]
        {
            ("work", "Work", "#3B82F6", ["devenv.exe", "code.exe", "rider64.exe", "idea64.exe", "winword.exe", "excel.exe",
                "powerpnt.exe", "onenote.exe", "windowsterminal.exe", "wt.exe", "powershell.exe", "pwsh.exe", "cmd.exe",
                "ssms.exe", "notepad++.exe", "figma.exe", "postman.exe", "github*.exe"]),
            ("communication", "Communication", "#14B8A6", ["outlook.exe", "olk.exe", "ms-teams.exe", "teams.exe", "slack.exe", "zoom.exe",
                "thunderbird.exe", "signal.exe", "telegram.exe", "whatsapp*.exe"]),
            ("entertainment", "Entertainment", "#F97316", ["spotify.exe", "steam.exe", "steamwebhelper.exe", "epicgameslauncher.exe", "vlc.exe",
                "netflix.exe", "battle.net.exe", "leagueclient*.exe", "riotclient*.exe"]),
            ("social", "Social media", "#EC4899", ["discord.exe", "messenger.exe"]),
            ("education", "Learning", "#8B5CF6", ["anki.exe", "kindle.exe", "obsidian.exe"]),
            ("other", "Other", "#94A3B8", []),
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
        Exec(c, "PRAGMA user_version = 1;", tx);
        tx.Commit();
    }

    /// <summary>v2: built-in categories get a stable key so their names can be shown in the UI language.</summary>
    private static void MigrateV2(SqliteConnection c)
    {
        using var tx = c.BeginTransaction();
        Exec(c, "ALTER TABLE categories ADD COLUMN key TEXT NULL;", tx);
        string[] keys = ["work", "communication", "entertainment", "social", "education", "other"];
        for (var i = 0; i < keys.Length; i++)
            Exec(c, "UPDATE categories SET key = $k WHERE is_builtin = 1 AND sort_order = $o;", tx, ("$k", keys[i]), ("$o", i));
        // "Other" may have been pushed down by user categories; it is the last built-in one.
        Exec(c, "UPDATE categories SET key = 'other' WHERE id = (SELECT id FROM categories WHERE is_builtin = 1 ORDER BY sort_order DESC LIMIT 1);", tx);
        Exec(c, "PRAGMA user_version = 2;", tx);
        tx.Commit();
    }

    /// <summary>v3: default rules for websites reported by the browser extension.</summary>
    private static void MigrateV3(SqliteConnection c)
    {
        using var tx = c.BeginTransaction();
        var web = new (string Key, string[] Domains)[]
        {
            ("work", ["docs.google.com", "drive.google.com", "github.com", "gitlab.com", "stackoverflow.com", "atlassian.net",
                "figma.com", "notion.so", "learn.microsoft.com", "portal.azure.com", "dev.azure.com", "office.com", "sharepoint.com"]),
            ("communication", ["mail.google.com", "outlook.office.com", "outlook.live.com", "teams.microsoft.com", "slack.com",
                "web.whatsapp.com", "web.telegram.org", "meet.google.com", "zoom.us"]),
            ("entertainment", ["youtube.com", "netflix.com", "twitch.tv", "open.spotify.com", "disneyplus.com", "max.com",
                "primevideo.com", "9gag.com", "imdb.com"]),
            ("social", ["facebook.com", "instagram.com", "x.com", "twitter.com", "reddit.com", "tiktok.com", "linkedin.com",
                "messenger.com", "threads.net", "pinterest.com"]),
            ("education", ["wikipedia.org", "coursera.org", "udemy.com", "khanacademy.org", "edx.org", "duolingo.com"]),
        };
        foreach (var (key, domains) in web)
        {
            if (Scalar(c, "SELECT id FROM categories WHERE key = $k;", tx, ("$k", key)) is not long cat) continue;
            foreach (var d in domains)
                Exec(c, "INSERT INTO rules(pattern, kind, category_id, is_user) VALUES ($p, 'web', $c, 0);", tx, ("$p", d), ("$c", cat));
        }
        Exec(c, "PRAGMA user_version = 3;", tx);
        tx.Commit();
    }

    /// <summary>v4: livelier category colors (one palette); colors you picked outside the old palette are kept.</summary>
    private static void MigrateV4(SqliteConnection c)
    {
        using var tx = c.BeginTransaction();
        (string Old, string New)[] map =
        [
            ("#2F6FEB", "#3B82F6"),
            ("#0E8F80", "#14B8A6"),
            ("#E07A2E", "#F97316"),
            ("#C9437A", "#EC4899"),
            ("#7452D1", "#8B5CF6"),
            ("#3C9A3C", "#22C55E"),
            ("#C2A21A", "#EAB308"),
            ("#D9534F", "#EF4444"),
            ("#1F8FB8", "#06B6D4"),
            ("#8A919E", "#94A3B8"),
        ];
        foreach (var (old, @new) in map)
            Exec(c, "UPDATE categories SET color = $n WHERE upper(color) = $o;", tx, ("$n", @new), ("$o", old));
        Exec(c, "PRAGMA user_version = 4;", tx);
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
