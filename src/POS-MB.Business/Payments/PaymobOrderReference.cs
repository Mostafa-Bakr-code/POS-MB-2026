using System.Globalization;

namespace POS_MB.Business.Payments;

// The special_reference sent to Paymob (and echoed back in the webhook as
// merchant_order_id) - deliberately NOT the raw OrderId. Paymob's own
// auto-generated customer emails display this value as "the order number",
// and a raw ever-climbing database primary key isn't something a student
// should see on their receipt - it also reveals the business's total
// lifetime order volume to anyone reading their own email, which isn't
// something worth exposing either.
//
// {OrderDate}-{SerialNumber} is just as globally unique (the database
// already enforces no two orders on the same day can share a SerialNumber -
// see UQ_Orders_Date_SerialNumber) and matches what the app already shows
// customers everywhere else, instead of a meaningless internal number.
public static class PaymobOrderReference
{
    public static string Build(DateTime orderDateUtc, int serialNumber) =>
        $"{orderDateUtc:yyyyMMdd}-{serialNumber}";

    // Paymob requires special_reference to be unique per intention - reusing
    // the exact same reference on a retry (ResumeCheckoutAsync starting a
    // second checkout attempt for a still-unpaid order) gets the second
    // CreateIntention call rejected outright. Appending a time-based suffix
    // keeps every attempt unique while staying short and still parseable
    // back to the same order - unlike a random GUID, it doesn't turn the
    // reference Paymob's own email shows the student into gibberish.
    public static string BuildRetry(DateTime orderDateUtc, int serialNumber) =>
        $"{Build(orderDateUtc, serialNumber)}-{DateTime.UtcNow:HHmmss}";

    public static bool TryParse(string reference, out DateTime orderDateUtc, out int serialNumber)
    {
        orderDateUtc = default;
        serialNumber = 0;

        var parts = reference.Split('-', 3);
        if (parts.Length < 2) return false;

        return DateTime.TryParseExact(parts[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out orderDateUtc)
            && int.TryParse(parts[1], out serialNumber);
    }
}
