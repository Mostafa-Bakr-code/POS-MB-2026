namespace POS_MB.Cashier.Models;

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
    Cancelled = 4,
    AwaitingPayment = 5
}

public class OrderItemDto
{
    public int OrderItemId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal TotalItemsPrice { get; set; }
    public decimal TaxRate { get; set; }
    public string? Comment { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTime Date { get; set; }
    public decimal Total { get; set; }
    public int? SerialNumber { get; set; }
    public int? UserId { get; set; }
    public int? StudentId { get; set; }
    public string? CashierName { get; set; }
    public string? StudentEmail { get; set; }
    public OrderSource OrderSource { get; set; }
    public OrderStatus Status { get; set; }
    public bool IsComplimentary { get; set; }
    public long? PaymobTransactionId { get; set; }
    public DateTime? RefundedAt { get; set; }
    public long? RefundTransactionId { get; set; }
    public string? CancelledBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<OrderItemDto> Items { get; set; } = [];
}

public class LogDto
{
    public int LogId { get; set; }
    public int UserId { get; set; }
    public DateTime LogIn { get; set; }
    public DateTime? LogOut { get; set; }
}
