namespace POS_MB.DataAccess.Models;

public class Log
{
    public int LogId { get; set; }
    public int UserId { get; set; }
    public DateTime LogIn { get; set; }
    public DateTime? LogOut { get; set; }
}
