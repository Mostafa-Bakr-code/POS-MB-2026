using POS_MB.Cashier.Models;

namespace POS_MB.Cashier.Session;

// Ported from POS_MB.WinformsApp.Session.AppSession - client-side only, who's
// currently using this device and the Logs session tracking their shift. Not
// a security boundary (see Models/Permission.cs).
public static class AppSession
{
    public static UserDto? CurrentUser { get; set; }
    public static int? LogId { get; set; }
    public static string? Token { get; set; }
    public static string? RefreshToken { get; set; }

    // Orders/timestamps come from the API in UTC. Loaded at login from the
    // TimeZoneOffsetHours setting so displayed times match the local
    // calendar day the server-side filtering/reporting already uses - not
    // this device's own OS timezone, which may not match.
    public static decimal TimeZoneOffsetHours { get; set; } = 0m;

    public static bool HasPermission(Permission permission) =>
        CurrentUser is not null && ((Permission)CurrentUser.Permissions & permission) == permission;

    public static DateTime ToLocalDisplay(DateTime utc) =>
        utc.AddHours((double)TimeZoneOffsetHours);

    public static DateTime LocalToday => ToLocalDisplay(DateTime.UtcNow).Date;

    public static void Clear()
    {
        CurrentUser = null;
        LogId = null;
        Token = null;
        RefreshToken = null;
        TimeZoneOffsetHours = 0m;
    }
}
