namespace POS_MB.WinformsApp.Models;

// Mirrors the bit values stored in Users.Permissions on the server.
// WinForms only uses this to hide/show UI - the API does not enforce it yet.
[Flags]
public enum Permission
{
    None = 0,
    Categories = 1,
    Items = 2,
    Orders = 4,
    Users = 8,
    Reports = 16,
    OrderHistory = 32,
    DailySummary = 64,
    Settings = 128,
    Logs = 256,
    Complimentary = 512,
    FullAccess = Categories | Items | Orders | Users | Reports | OrderHistory | DailySummary | Settings | Logs | Complimentary
}
