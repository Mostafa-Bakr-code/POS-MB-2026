using POS_MB.WinformsApp.Models;

namespace POS_MB.WinformsApp.Session;

// Client-side only - who's currently using this terminal, and the Logs session
// tracking their shift. Not a security boundary (see Permission.cs).
public static class AppSession
{
    public static UserDto? CurrentUser { get; set; }
    public static int? LogId { get; set; }

    public static bool HasPermission(Permission permission) =>
        CurrentUser is not null && ((Permission)CurrentUser.Permissions & permission) == permission;

    public static void Clear()
    {
        CurrentUser = null;
        LogId = null;
    }
}
