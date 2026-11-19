using HomeFocus.Core.Data;
using Microsoft.Data.Sqlite;

namespace HomeFocus.Tests;

public sealed class PaletteMigrationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"homefocus-pal-{Guid.NewGuid():N}.db");

    [Fact]
    public void Old_colors_move_to_the_new_palette_without_double_mapping()
    {
        var db = new Database(_path);
        db.Initialize();
        using (var c = db.Open())
        {
            // pretend to be a v3 database: old palette colors, incl. one custom category using the old cyan
            Exec(c, "UPDATE categories SET color = '#0E8F80' WHERE key = 'communication';");
            Exec(c, "INSERT INTO categories(name, color, is_builtin, sort_order) VALUES ('Mine', '#1F8FB8', 0, 99);");
            Exec(c, "INSERT INTO categories(name, color, is_builtin, sort_order) VALUES ('Picked', '#123456', 0, 100);");
            Exec(c, "PRAGMA user_version = 3;");
        }
        db.Initialize();

        var cats = new CategoryStore(db).GetAll();
        Assert.Equal("#66CCEE", cats.Single(x => x.Key == "communication").Color); // old teal → sky blue
        Assert.Equal("#0077BB", cats.Single(x => x.Name == "Mine").Color);        // old cyan → its own slot, never via teal
        Assert.Equal("#123456", cats.Single(x => x.Name == "Picked").Color);      // own color kept
        Assert.Equal("#4477AA", cats.Single(x => x.Key == "work").Color);
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}
