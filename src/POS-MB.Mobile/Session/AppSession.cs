using POS_MB.Mobile.Models;

namespace POS_MB.Mobile.Session;

// In-memory only for now - lost when the app closes, meaning a student has to
// log in again each time they open the app. Persisting this securely across
// app restarts (MAUI's SecureStorage, backed by Keychain on iOS / Keystore on
// Android) is deliberately deferred until this basic login flow works end to
// end - see project memory on mobile security to-dos.
public static class AppSession
{
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

    public static void Clear()
    {
        Token = null;
        RefreshToken = null;
        CurrentStudent = null;
        TimeZoneOffsetHours = 0m;
    }
}
