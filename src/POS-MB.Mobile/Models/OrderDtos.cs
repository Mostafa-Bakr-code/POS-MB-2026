namespace POS_MB.Mobile.Models;

// Mirrors POS_MB.DataAccess.Models.OrderStatus bit-for-bit (0=Placed,1=Preparing,
// 2=Ready,3=Completed,4=Cancelled) - deliberately duplicated, not shared, same
// reasoning as every other client-side mirror of a server enum in this project
// (WinForms' Permission enum, etc.). Naming the members this way lets a plain
// {Binding Status} show a readable word directly, no converter needed.
public enum OrderStatus { Placed = 0, Preparing = 1, Ready = 2, Completed = 3, Cancelled = 4 }

public record OrderItemLineDto(int ItemId, int Quantity, string? Comment);
public record PlaceOrderRequest(List<OrderItemLineDto> Items);

public record UnavailableItemDto(int ItemId, string ItemName, string Reason);

public record OrderSummaryDto(int OrderId, DateTime Date, decimal Total, int? SerialNumber, OrderStatus Status);

public record OrderItemDetailDto(int ItemId, int Quantity, decimal Price, decimal TotalItemsPrice, string? Comment);
public record OrderDetailDto(int OrderId, DateTime Date, decimal Total, int? SerialNumber, OrderStatus Status, List<OrderItemDetailDto> Items);

// Display-ready version of OrderItemDetailDto with the item name already
// resolved (OrderItemDetailDto only has ItemId) - built in OrderDetailPage's
// code-behind rather than via an XAML converter, keeping with this app's
// plain-code-behind approach so far.
public class OrderItemDisplayDto
{
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal TotalItemsPrice { get; set; }
    public string? Comment { get; set; }
    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);
}
