namespace POS_MB.Web.Models;

// Mirrors POS-MB.Mobile's Models/OrderDtos.cs bit-for-bit (same server
// contract, same OrderStatus enum values) - duplicated rather than shared,
// same convention as every other client-side DTO mirror in this codebase.
public enum OrderStatus { Placed = 0, Preparing = 1, Ready = 2, Completed = 3, Cancelled = 4, AwaitingPayment = 5 }

public record OrderItemLineDto(int ItemId, int Quantity, string? Comment);
public record PlaceOrderRequest(List<OrderItemLineDto> Items, bool UseSavedCard = false);

public record UnavailableItemDto(int ItemId, string ItemName, string Reason);

public record OrderSummaryDto(int OrderId, DateTime Date, decimal Total, int? SerialNumber, OrderStatus Status, decimal ServiceFee = 0);

public record OrderItemDetailDto(int ItemId, int Quantity, decimal Price, decimal TotalItemsPrice, string? Comment);
public record OrderDetailDto(int OrderId, DateTime Date, decimal Total, int? SerialNumber, OrderStatus Status, List<OrderItemDetailDto> Items, string? CheckoutUrl = null, decimal ServiceFee = 0);
