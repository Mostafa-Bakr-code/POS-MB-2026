namespace POS_MB.DataAccess.Models;

public class Order
{
    public int OrderId { get; set; }
    public DateTime Date { get; set; }
    public decimal Total { get; set; }
    public int? SerialNumber { get; set; }
    public int UserId { get; set; }
    public OrderStatus Status { get; set; }
    public bool IsComplimentary { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
