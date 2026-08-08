namespace POS_MB.DataAccess.Models;

public class Order
{
    public int OrderId { get; set; }
    public DateTime Date { get; set; }
    public decimal Total { get; set; }
    public int? SerialNumber { get; set; }
    public int? UserId { get; set; }
    public int? StudentId { get; set; }

    // Resolved via a LEFT JOIN in clsOrderDataAccess, not stored - same pattern
    // as ItemPriceHistory.ChangedByUserName, so callers (WinForms' order
    // history) never need to separately fetch and join a Users/Students list
    // client-side just to show who placed an order.
    public string? CashierName { get; set; }
    public string? StudentEmail { get; set; }
    public OrderSource OrderSource { get; set; }
    public OrderStatus Status { get; set; }
    public bool IsComplimentary { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
