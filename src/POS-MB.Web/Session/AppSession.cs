using Microsoft.JSInterop;
using POS_MB.Web.Models;

namespace POS_MB.Web.Session;

// Mirrors POS-MB.Mobile's Session/AppSession.cs - Token/CurrentStudent/
// TimeZoneOffsetHours are in-memory only, rebuilt fresh on every page load.
// Only the refresh token is persisted - in the browser's localStorage here,
// where Mobile uses SecureStorage (Keychain/Keystore). localStorage has no
// OS-level encryption the way SecureStorage does, but it's the only
// client-side persistence a browser offers; the refresh token is still
// short-lived and server-revocable, same as Mobile's.
//
// A static class can't hold an IJSRuntime of its own (no global JS accessor
// in Blazor), so the persistence methods take one in as a parameter from
// whichever component already has it injected.
public static class AppSession
{
    private const string RefreshTokenKey = "student_refresh_token";

    public static string? Token { get; set; }
    public static string? RefreshToken { get; set; }
    public static StudentDto? CurrentStudent { get; set; }

    public static decimal TimeZoneOffsetHours { get; set; } = 0m;

    public static DateTime ToLocalDisplay(DateTime utc) =>
        utc.AddHours((double)TimeZoneOffsetHours);

    public static async Task PersistRefreshTokenAsync(IJSRuntime js)
    {
        if (RefreshToken is null) return;
        await js.InvokeVoidAsync("localStorage.setItem", RefreshTokenKey, RefreshToken);
    }

    public static async Task<string?> GetPersistedRefreshTokenAsync(IJSRuntime js)
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", RefreshTokenKey);
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

    public static async Task ClearPersistedAsync(IJSRuntime js)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", RefreshTokenKey);
        }
        catch (Exception)
        {
            // ignored - best-effort cleanup, matches Mobile's ClearPersisted
        }
    }
}
