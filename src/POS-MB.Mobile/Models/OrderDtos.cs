namespace POS_MB.Mobile.Models;

public record OrderItemLineDto(int ItemId, int Quantity, string? Comment);
public record PlaceOrderRequest(List<OrderItemLineDto> Items);

public record OrderResponse(int OrderId, DateTime Date, decimal Total, int? SerialNumber, int Status, DateTime CreatedAt, DateTime UpdatedAt);
