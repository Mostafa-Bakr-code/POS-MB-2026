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

    public static void Clear()
    {
        Token = null;
        RefreshToken = null;
        CurrentStudent = null;
    }
}
