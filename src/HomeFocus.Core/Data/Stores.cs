using System.Globalization;
using Microsoft.Data.Sqlite;

namespace HomeFocus.Core.Data;

public interface IActivitySink
{
    void Upsert(ActivityRecord record);
    void Delete(string id);
}

public sealed class ActivityStore(Database db) : IActivitySink
{
    public void Upsert(ActivityRecord r)
    {
        using var c = db.Open();
        Database.Exec(c, """
            INSERT INTO activity_log(id, device_id, kind, app_key, display_name, category_id, start_utc, end_utc, duration_ms)
            VALUES ($id, $dev, $kind, $key, $name, $cat, $s, $e, $d)
            ON CONFLICT(id) DO UPDATE SET end_utc = $e, duration_ms = $d, display_name = $name;
            """, null,
            ("$id", r.Id), ("$dev", r.DeviceId), ("$kind", KindToText(r.Kind)), ("$key", r.AppKey),
            ("$name", r.DisplayName), ("$cat", r.CategoryOverride), ("$s", ToMs(r.StartUtc)), ("$e", ToMs(r.EndUtc)),
            ("$d", (long)r.Duration.TotalMilliseconds));
    }

    public void Delete(string id)
    {
        using var c = db.Open();
        Database.Exec(c, "DELETE FROM activity_log WHERE id = $id;", null, ("$id", id));
    }

    /// <summary>Records that overlap [fromUtc, toUtc).</summary>
    public List<ActivityRecord> Query(DateTime fromUtc, DateTime toUtc)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id, device_id, kind, app_key, display_name, category_id, start_utc, end_utc
            FROM activity_log WHERE start_utc < $to AND end_utc > $from ORDER BY start_utc;
            """;
        cmd.Parameters.AddWithValue("$from", ToMs(fromUtc));
        cmd.Parameters.AddWithValue("$to", ToMs(toUtc));
        using var rd = cmd.ExecuteReader();
        var list = new List<ActivityRecord>();
        while (rd.Read())
            list.Add(new ActivityRecord(rd.GetString(0), rd.GetString(1), TextToKind(rd.GetString(2)), rd.GetString(3),
                rd.GetString(4), rd.IsDBNull(5) ? null : rd.GetInt64(5), FromMs(rd.GetInt64(6)), FromMs(rd.GetInt64(7))));
        return list;
    }

    public void DeleteAll()
    {
        using var c = db.Open();
        Database.Exec(c, "DELETE FROM activity_log;");
        Database.Exec(c, "VACUUM;");
    }

    public static long ToMs(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    public static DateTime FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    private static string KindToText(ActivityKind k) => k == ActivityKind.Idle ? "idle" : "app";
    private static ActivityKind TextToKind(string s) => s == "idle" ? ActivityKind.Idle : ActivityKind.App;
}

public sealed class CategoryStore(Database db)
{
    public List<Category> GetAll()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, name, color, is_builtin, sort_order, key FROM categories ORDER BY sort_order, id;";
        using var rd = cmd.ExecuteReader();
        var list = new List<Category>();
        while (rd.Read())
            list.Add(new Category(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetInt64(3) != 0, (int)rd.GetInt64(4),
                rd.IsDBNull(5) ? null : rd.GetString(5)));
        return list;
    }

    public long Add(string name, string color)
    {
        using var c = db.Open();
        var order = Convert.ToInt32(Database.Scalar(c, "SELECT COALESCE(MAX(sort_order), 0) FROM categories;"));
        // keep the "Other" fallback last: new categories go before the highest built-in order
        Database.Exec(c, "UPDATE categories SET sort_order = sort_order + 1 WHERE sort_order = $o;", null, ("$o", order));
        Database.Exec(c, "INSERT INTO categories(name, color, is_builtin, sort_order) VALUES ($n, $c, 0, $o);", null,
            ("$n", name), ("$c", color), ("$o", order));
        return (long)Database.Scalar(c, "SELECT last_insert_rowid();")!;
    }

    public void Update(long id, string name, string color)
    {
        using var c = db.Open();
        Database.Exec(c, "UPDATE categories SET name = $n, color = $c WHERE id = $id;", null, ("$n", name), ("$c", color), ("$id", id));
    }

    /// <summary>Deletes a user category; its rules are moved to <paramref name="fallbackId"/>.</summary>
    public void Delete(long id, long fallbackId)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        Database.Exec(c, "UPDATE rules SET category_id = $f WHERE category_id = $id;", tx, ("$f", fallbackId), ("$id", id));
        Database.Exec(c, "DELETE FROM categories WHERE id = $id AND is_builtin = 0;", tx, ("$id", id));
        tx.Commit();
    }
}

public sealed class RuleStore(Database db)
{
    public List<Rule> GetAll()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, pattern, category_id, is_user FROM rules WHERE kind = 'app' ORDER BY pattern;";
        using var rd = cmd.ExecuteReader();
        var list = new List<Rule>();
        while (rd.Read()) list.Add(new Rule(rd.GetInt64(0), rd.GetString(1), rd.GetInt64(2), rd.GetInt64(3) != 0));
        return list;
    }

    /// <summary>Adds a user rule, or re-points an existing rule with the same pattern.</summary>
    public void Set(string pattern, long categoryId)
    {
        pattern = pattern.Trim().ToLowerInvariant();
        using var c = db.Open();
        var existing = Database.Scalar(c, "SELECT id FROM rules WHERE pattern = $p AND kind = 'app';", null, ("$p", pattern));
        if (existing is long id)
            Database.Exec(c, "UPDATE rules SET category_id = $c, is_user = 1 WHERE id = $id;", null, ("$c", categoryId), ("$id", id));
        else
            Database.Exec(c, "INSERT INTO rules(pattern, kind, category_id, is_user) VALUES ($p, 'app', $c, 1);", null,
                ("$p", pattern), ("$c", categoryId));
    }

    public void Delete(long id)
    {
        using var c = db.Open();
        Database.Exec(c, "DELETE FROM rules WHERE id = $id;", null, ("$id", id));
    }
}

public sealed class SettingsStore(Database db)
{
    public string? Get(string key)
    {
        using var c = db.Open();
        return Database.Scalar(c, "SELECT value FROM settings WHERE key = $k;", null, ("$k", key)) as string;
    }

    public void Set(string key, string value)
    {
        using var c = db.Open();
        Database.Exec(c, "INSERT INTO settings(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v;", null,
            ("$k", key), ("$v", value));
    }

    public int GetInt(string key, int fallback) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public bool GetBool(string key, bool fallback) => Get(key) switch { "1" => true, "0" => false, _ => fallback };
    public void SetBool(string key, bool value) => Set(key, value ? "1" : "0");
    public void SetInt(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));

    public string DeviceId()
    {
        using var c = db.Open();
        return (string)Database.Scalar(c, "SELECT id FROM devices WHERE kind = 'pc' ORDER BY created_utc LIMIT 1;")!;
    }
}
