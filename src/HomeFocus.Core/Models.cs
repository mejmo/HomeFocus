namespace HomeFocus.Core;

public enum ActivityKind
{
    App,
    Idle,
}

/// <summary>A category such as "Práca" with its display color (#RRGGBB).</summary>
public sealed record Category(long Id, string Name, string Color, bool IsBuiltIn, int SortOrder);

/// <summary>Maps an app key pattern (e.g. "devenv.exe", "steam*.exe") to a category.</summary>
public sealed record Rule(long Id, string Pattern, long CategoryId, bool IsUser);

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
