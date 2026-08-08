using POS_MB.Mobile.Models;

namespace POS_MB.Mobile.Session;

// Token/CurrentStudent/TimeZoneOffsetHours are in-memory only, rebuilt fresh
// every time the app opens (via TryRestoreAsync or a fresh login) - only the
// refresh token itself is persisted, in SecureStorage (Keychain on iOS,
// Keystore on Android), never in a plain file. This mirrors how a short-lived
// access token normally isn't worth persisting - it's the long-lived refresh
// token that lets the app sign a student back in silently on next launch.
public static class AppSession
{
    private const string RefreshTokenKey = "student_refresh_token";

    public static string? Token { get; set; }
    public static string? RefreshToken { get; set; }
    public static StudentDto? CurrentStudent { get; set; }

    // Orders/timestamps come from the API in UTC. Loaded at login from the
    // TimeZoneOffsetHours setting, same reasoning and same source of truth as
    // WinForms' AppSession - without this, order times would show the server's
    // UTC clock instead of the local time a student actually expects to see.
    public static decimal TimeZoneOffsetHours { get; set; } = 0m;

    public static DateTime ToLocalDisplay(DateTime utc) =>
        utc.AddHours((double)TimeZoneOffsetHours);

    public static async Task PersistRefreshTokenAsync()
    {
        if (RefreshToken is null) return;
        await SecureStorage.Default.SetAsync(RefreshTokenKey, RefreshToken);
    }

    // SecureStorage.GetAsync can throw on Android if the OS-level key used to
    // encrypt the store was invalidated (e.g. the user changed their device
    // lock screen) - treated the same as "no saved session" rather than
    // crashing the app on launch.
    public static async Task<string?> GetPersistedRefreshTokenAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(RefreshTokenKey);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Clear()
    {
        Token = null;
        RefreshToken = null;
        CurrentStudent = null;
        TimeZoneOffsetHours = 0m;
    }

    public static void ClearPersisted()
    {
        SecureStorage.Default.Remove(RefreshTokenKey);
    }
}
