namespace POS_MB.WinformsApp.Models;

public class CategoryDto
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class ItemDto
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public decimal Price { get; set; }
    public decimal TaxRate { get; set; }
    public bool IsActive { get; set; }
    public bool IsAvailable { get; set; }
}

public class ItemPriceHistoryDto
{
    public int ItemPriceHistoryId { get; set; }
    public int ItemId { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public decimal OldTaxRate { get; set; }
    public decimal NewTaxRate { get; set; }
    public int? ChangedByUserId { get; set; }
    public string? ChangedByUserName { get; set; }
    public DateTime ChangedAt { get; set; }
}

public class UserDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int Permissions { get; set; }
    public bool IsActive { get; set; }
}

public class LogDto
{
    public int LogId { get; set; }
    public int UserId { get; set; }
    public DateTime LogIn { get; set; }
    public DateTime? LogOut { get; set; }
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
    public List<OrderItemDto> Items { get; set; } = [];
}

public record VerifyCredentialsRequest(string UserName, string Password);
public record LoginResponse(string Token, string RefreshToken, UserDto User);

public record NewOrderItemRequest(int ItemId, int Quantity, string? Comment);

public record CreateOrderRequest(OrderSource OrderSource, int? UserId, bool IsComplimentary, List<NewOrderItemRequest> Items);
