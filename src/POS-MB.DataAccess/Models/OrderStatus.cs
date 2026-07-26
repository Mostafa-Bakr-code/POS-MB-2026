namespace POS_MB.DataAccess.Models;

public enum OrderStatus : byte
{
    Placed = 0,
    Preparing = 1,
    Ready = 2,
    Completed = 3,
    Cancelled = 4
}
