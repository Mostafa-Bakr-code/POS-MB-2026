using POS_MB.DataAccess;
using POS_MB.DataAccess.Models.Reports;

namespace POS_MB.Business;

public class clsReportingBusiness(clsReportingDataAccess dataAccess, clsSettingsBusiness settingsBusiness)
{
    public async Task<SalesSummary> GetSalesSummaryAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        var (utcStart, utcEndExclusive) = await TimeZoneHelper.ResolveUtcRangeAsync(settingsBusiness, startDate, endDate);
        return await dataAccess.GetSalesSummaryAsync(utcStart, utcEndExclusive);
    }

    public async Task<IEnumerable<ItemSalesRow>> GetItemSalesAsync(DateTime? startDate = null, DateTime? endDate = null, bool groupByDay = false, bool groupByPrice = false)
    {
        var (utcStart, utcEndExclusive) = await TimeZoneHelper.ResolveUtcRangeAsync(settingsBusiness, startDate, endDate);
        var offsetHours = groupByDay ? await TimeZoneHelper.GetOffsetHoursAsync(settingsBusiness) : 0m;
        return await dataAccess.GetItemSalesAsync(utcStart, utcEndExclusive, groupByDay, groupByPrice, offsetHours);
    }

    public async Task<IEnumerable<ItemSalesRow>> GetTopSellersAsync(DateTime? startDate = null, DateTime? endDate = null, TopSellersSortBy sortBy = TopSellersSortBy.Quantity, int take = 10)
    {
        var (utcStart, utcEndExclusive) = await TimeZoneHelper.ResolveUtcRangeAsync(settingsBusiness, startDate, endDate);
        return await dataAccess.GetTopSellersAsync(utcStart, utcEndExclusive, sortBy, take);
    }

    public async Task<IEnumerable<StaffPerformanceRow>> GetStaffPerformanceAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        var (utcStart, utcEndExclusive) = await TimeZoneHelper.ResolveUtcRangeAsync(settingsBusiness, startDate, endDate);
        return await dataAccess.GetStaffPerformanceAsync(utcStart, utcEndExclusive);
    }
}
