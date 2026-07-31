using System.Globalization;

namespace POS_MB.Business;

// Orders.Date is stored in UTC (deliberately - see project notes on why), but date-range
// filters from clients (Order History, Reports, Daily Summary) mean "my local calendar day".
// Without this conversion, anything placed in the first few hours after local midnight gets
// silently filed under the *previous* UTC day and disappears from "today" filters.
internal static class TimeZoneHelper
{
    private const string TimeZoneOffsetSettingKey = "TimeZoneOffsetHours";

    /// <summary>
    /// Converts a local calendar date range into the equivalent UTC instant range
    /// (end is exclusive - the instant just after the end date's local day finishes).
    /// </summary>
    public static async Task<(DateTime? UtcStart, DateTime? UtcEndExclusive)> ResolveUtcRangeAsync(
        clsSettingsBusiness settingsBusiness, DateTime? localStartDate, DateTime? localEndDate)
    {
        if (localStartDate is null && localEndDate is null)
            return (null, null);

        var offsetHours = (double)await GetOffsetHoursAsync(settingsBusiness);

        var utcStart = localStartDate?.Date.AddHours(-offsetHours);
        var utcEndExclusive = localEndDate?.Date.AddDays(1).AddHours(-offsetHours);

        return (utcStart, utcEndExclusive);
    }

    public static async Task<decimal> GetOffsetHoursAsync(clsSettingsBusiness settingsBusiness)
    {
        var setting = await settingsBusiness.GetByKeyAsync(TimeZoneOffsetSettingKey);

        if (setting?.Value is not null && decimal.TryParse(setting.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var offset))
            return offset;

        return 0;
    }
}
