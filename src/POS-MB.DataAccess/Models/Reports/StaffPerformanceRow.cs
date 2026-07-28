namespace POS_MB.DataAccess.Models.Reports;

public class StaffPerformanceRow
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal Revenue { get; set; }
}
