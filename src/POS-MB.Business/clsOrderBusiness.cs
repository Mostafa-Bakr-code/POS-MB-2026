using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Business;

public class clsOrderBusiness(clsOrderDataAccess dataAccess)
{
    public async Task<int> CreateOrderAsync(int userId, bool isComplimentary, IReadOnlyList<NewOrderItem> items)
    {
        if (items.Count == 0)
            throw new ArgumentException("An order must have at least one item.", nameof(items));

        foreach (var item in items)
        {
            if (item.Quantity <= 0)
                throw new ArgumentException($"Quantity for item {item.ItemId} must be greater than zero.", nameof(items));
        }

        return await dataAccess.CreateOrderAsync(userId, isComplimentary, items);
    }

    public Task<Order?> GetByIdAsync(int id) =>
        dataAccess.GetByIdAsync(id);

    public Task<IEnumerable<OrderItem>> GetItemsByOrderIdAsync(int orderId) =>
        dataAccess.GetItemsByOrderIdAsync(orderId);

    public Task<IEnumerable<Order>> GetAllAsync(DateTime? startDate = null, DateTime? endDate = null) =>
        dataAccess.GetAllAsync(startDate, endDate);

    public Task<bool> UpdateStatusAsync(int id, OrderStatus status) =>
        dataAccess.UpdateStatusAsync(id, status);

    public Task<bool> CancelAsync(int id) =>
        dataAccess.UpdateStatusAsync(id, OrderStatus.Cancelled);
}
