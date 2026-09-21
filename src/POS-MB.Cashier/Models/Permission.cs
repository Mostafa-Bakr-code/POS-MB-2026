namespace POS_MB.Cashier.Models;

// Mirrors POS_MB.WinformsApp.Models.Permission exactly - both are the
// client-side mirror of the bit values stored in Users.Permissions on the
// server. Only used to hide/show UI here too; the API is what actually
// enforces access.
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
