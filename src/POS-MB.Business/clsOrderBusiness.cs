using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Business;

public class clsOrderBusiness(clsOrderDataAccess dataAccess, clsSettingsBusiness settingsBusiness)
{
    public async Task<int> CreateOrderAsync(OrderSource orderSource, int? userId, int? studentId, bool isComplimentary, IReadOnlyList<NewOrderItem> items)
    {
        if (items.Count == 0)
            throw new ArgumentException("An order must have at least one item.", nameof(items));

        foreach (var item in items)
        {
            if (item.Quantity <= 0)
                throw new ArgumentException($"Quantity for item {item.ItemId} must be greater than zero.", nameof(items));
        }

        if (orderSource == OrderSource.Cashier && userId is null)
            throw new ArgumentException("A cashier order must specify which staff user placed it.", nameof(userId));
        if (orderSource == OrderSource.Mobile && studentId is null)
            throw new ArgumentException("A mobile order must specify which student placed it.", nameof(studentId));

        return await dataAccess.CreateOrderAsync(orderSource, userId, studentId, isComplimentary, items);
    }

    // Complimentary (free) orders are a staff-only concept - a student ordering
    // for themselves is never "complimentary", that would mean giving away food
    // for free with no staff decision behind it.
    public Task<int> CreateStudentOrderAsync(int studentId, IReadOnlyList<NewOrderItem> items) =>
        CreateOrderAsync(OrderSource.Mobile, userId: null, studentId, isComplimentary: false, items);

    // No date filtering for v1 - a student's own order history is short enough
    // that "everything" is simpler and correct, rather than reusing the staff
    // GetAllAsync's timezone-resolution logic for a filter that isn't needed yet.
    public Task<IEnumerable<Order>> GetAllForStudentAsync(int studentId) =>
        dataAccess.GetAllForStudentAsync(studentId);

    public Task<Order?> GetByIdForStudentAsync(int orderId, int studentId) =>
        dataAccess.GetByIdForStudentAsync(orderId, studentId);

    public Task<bool> CancelForStudentAsync(int orderId, int studentId) =>
        dataAccess.CancelForStudentAsync(orderId, studentId);

    public Task<Order?> GetByIdAsync(int id) =>
        dataAccess.GetByIdAsync(id);

    public Task<IEnumerable<OrderItem>> GetItemsByOrderIdAsync(int orderId) =>
        dataAccess.GetItemsByOrderIdAsync(orderId);

    public async Task<IEnumerable<Order>> GetAllAsync(DateTime? startDate = null, DateTime? endDate = null, OrderSource? orderSource = null)
    {
        var (utcStart, utcEndExclusive) = await TimeZoneHelper.ResolveUtcRangeAsync(settingsBusiness, startDate, endDate);

        return await dataAccess.GetAllAsync(utcStart, utcEndExclusive, orderSource);
    }

    public Task<bool> UpdateStatusAsync(int id, OrderStatus status) =>
        dataAccess.UpdateStatusAsync(id, status);

    public Task<bool> CancelAsync(int id) =>
        dataAccess.UpdateStatusAsync(id, OrderStatus.Cancelled);
}
