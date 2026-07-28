namespace POS_MB.WinformsApp.Models;

public enum OrderSource : byte
{
    Cashier = 0,
    Mobile = 1
}

public enum OrderStatus : byte
{
    Placed = 0,
    Preparing = 1,
    Ready = 2,
    Completed = 3,
    Cancelled = 4
}
