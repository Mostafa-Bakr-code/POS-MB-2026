using POS_MB.DataAccess;
using POS_MB.DataAccess.Models.Reports;

namespace POS_MB.Business;

public class clsReportingBusiness(clsReportingDataAccess dataAccess)
{
    public Task<SalesSummary> GetSalesSummaryAsync(DateTime? startDate = null, DateTime? endDate = null) =>
        dataAccess.GetSalesSummaryAsync(startDate, endDate);

    public Task<IEnumerable<ItemSalesRow>> GetItemSalesAsync(DateTime? startDate = null, DateTime? endDate = null, bool groupByDay = false, bool groupByPrice = false) =>
        dataAccess.GetItemSalesAsync(startDate, endDate, groupByDay, groupByPrice);

    public Task<IEnumerable<ItemSalesRow>> GetTopSellersAsync(DateTime? startDate = null, DateTime? endDate = null, TopSellersSortBy sortBy = TopSellersSortBy.Quantity, int take = 10) =>
        dataAccess.GetTopSellersAsync(startDate, endDate, sortBy, take);

    public Task<IEnumerable<StaffPerformanceRow>> GetStaffPerformanceAsync(DateTime? startDate = null, DateTime? endDate = null) =>
        dataAccess.GetStaffPerformanceAsync(startDate, endDate);
}
