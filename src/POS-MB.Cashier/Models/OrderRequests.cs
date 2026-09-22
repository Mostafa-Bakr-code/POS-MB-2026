namespace POS_MB.Cashier.Models;

public record NewOrderItemRequest(int ItemId, int Quantity, string? Comment);
public record CreateOrderRequest(OrderSource OrderSource, int? UserId, bool IsComplimentary, List<NewOrderItemRequest> Items);
