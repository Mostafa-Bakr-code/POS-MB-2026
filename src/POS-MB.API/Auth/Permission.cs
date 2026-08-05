namespace POS_MB.API.Auth;

// Mirrors POS_MB.WinformsApp.Models.Permission bit-for-bit. Duplicated
// deliberately, not shared via a project reference - WinForms only ever talks
// to this API over HTTP like any other client would, so the two sides share
// the contract (the bit values), not the code.
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
