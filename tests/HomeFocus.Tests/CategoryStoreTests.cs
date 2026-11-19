using HomeFocus.Core.Data;

namespace HomeFocus.Tests;

public sealed class CategoryStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"homefocus-cat-{Guid.NewGuid():N}.db");
    private readonly Database _db;

    public CategoryStoreTests()
    {
        _db = new Database(_path);
        _db.Initialize();
    }

    [Fact]
    public void New_category_goes_before_Other_and_can_be_deleted()
    {
        var store = new CategoryStore(_db);
        var other = store.GetAll().Single(c => c.Key == "other");
        var id = store.Add("Side project", "#3C9A3C");

        var all = store.GetAll();
        Assert.Equal("other", all[^1].Key);           // "Other" stays last
        Assert.Equal(id, all[^2].Id);
        Assert.False(all[^2].IsBuiltIn);

        new RuleStore(_db).Set("myapp.exe", id);
        store.Delete(id, other.Id);
        Assert.DoesNotContain(store.GetAll(), c => c.Id == id);
        Assert.Equal(other.Id, new RuleStore(_db).GetAll().Single(r => r.Pattern == "myapp.exe").CategoryId);
    }

    [Fact]
    public void Built_in_categories_cannot_be_deleted()
    {
        var store = new CategoryStore(_db);
        var work = store.GetAll().Single(c => c.Key == "work");
        var other = store.GetAll().Single(c => c.Key == "other");
        store.Delete(work.Id, other.Id);
        Assert.Contains(store.GetAll(), c => c.Id == work.Id);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}
