namespace HomeFocus.Core;

public enum ActivityKind
{
    App,
    Idle,
    /// <summary>A website in the browser's active tab; AppKey is the domain (e.g. "youtube.com").</summary>
    Web,
}

/// <summary>A category such as "Work". Built-in ones carry a stable <paramref name="Key"/> so the UI can translate them.</summary>
public sealed record Category(long Id, string Name, string Color, bool IsBuiltIn, int SortOrder, string? Key = null);

/// <summary>
/// Maps a pattern to a category: a process name for apps ("devenv.exe", "steam*.exe")
/// or a domain for websites ("youtube.com" also covers its subdomains).
/// </summary>
public sealed record Rule(long Id, string Pattern, long CategoryId, bool IsUser, ActivityKind Kind = ActivityKind.App);

/// <summary>One continuous stretch of activity (or idle time) on one device.</summary>
public sealed record ActivityRecord(
    string Id,
    string DeviceId,
    ActivityKind Kind,
    string AppKey,
    string DisplayName,
    long? CategoryOverride,
    DateTime StartUtc,
    DateTime EndUtc)
{
    public TimeSpan Duration => EndUtc - StartUtc;
}

/// <summary>What the user is looking at right now.</summary>
public sealed record ForegroundApp(string AppKey, string DisplayName);
