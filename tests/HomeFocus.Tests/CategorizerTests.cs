using HomeFocus.Core;

namespace HomeFocus.Tests;

public class CategorizerTests
{
    private const long Work = 1, Fun = 2, Other = 9;

    [Theory]
    [InlineData("steam*.exe", "steamwebhelper.exe", true)]
    [InlineData("steam*.exe", "STEAM.EXE", true)]
    [InlineData("steam.exe", "steamwebhelper.exe", false)]
    [InlineData("code?.exe", "code2.exe", true)]
    [InlineData("notepad++.exe", "notepad++.exe", true)]
    [InlineData("a.b.exe", "axb.exe", false)]
    public void Matches_wildcards_case_insensitively(string pattern, string value, bool expected) =>
        Assert.Equal(expected, Categorizer.Matches(pattern, value));

    [Fact]
    public void Unknown_app_falls_back()
    {
        var c = new Categorizer([new Rule(1, "devenv.exe", Work, false)], Other);
        Assert.Equal(Other, c.Resolve("unknown.exe"));
        Assert.Null(c.TryMatch("unknown.exe"));
    }

    [Fact]
    public void Exact_rule_beats_wildcard()
    {
        var c = new Categorizer([new Rule(1, "steam*.exe", Fun, false), new Rule(2, "steamworks.exe", Work, false)], Other);
        Assert.Equal(Work, c.Resolve("steamworks.exe"));
        Assert.Equal(Fun, c.Resolve("steam.exe"));
    }

    [Fact]
    public void User_rule_beats_builtin_rule()
    {
        var c = new Categorizer([new Rule(1, "spotify.exe", Fun, false), new Rule(2, "spot*.exe", Work, true)], Other);
        Assert.Equal(Work, c.Resolve("spotify.exe"));
    }
}
