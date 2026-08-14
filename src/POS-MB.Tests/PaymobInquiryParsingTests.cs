using System.Text.Json;
using POS_MB.Business.Payments;

namespace POS_MB.Tests;

// Covers PaymobClient.ParseInquiryResponse - pure JSON parsing, no network
// call. The refunded-order payload below is not invented - it's the real
// response captured live from Paymob's Transaction Inquiry API for a
// genuinely refunded order (their "last transaction for this reference" is
// the refund itself, not the original payment), which is exactly the
// surprise that made this parsing logic necessary in the first place.
public class PaymobInquiryParsingTests
{
    [Fact]
    public void ParseInquiryResponse_ReportsSuccess_ForAGenuineUnrefundedPayment()
    {
        var json = """
            {
              "id": 514160442,
              "success": true,
              "is_refund": false,
              "is_void": false,
              "amount_cents": 15000
            }
            """;

        var result = PaymobClient.ParseInquiryResponse(JsonDocument.Parse(json).RootElement);

        Assert.True(result.Found);
        Assert.True(result.Success);
        Assert.Equal(514160442, result.TransactionId);
        Assert.Equal(150m, result.AmountEgp);
    }

    [Fact]
    public void ParseInquiryResponse_ReportsNotSuccessful_WhenTheLastTransactionIsARefund()
    {
        // Real response captured live: an order paid then cancelled/refunded -
        // Paymob's "last transaction for this reference" is the refund
        // itself (514153253), not the original payment (514150822, only
        // reachable via parent_transaction). success: true here describes
        // the refund succeeding, not a new payment - must not be read as
        // "this order was just paid".
        var json = """
            {
              "id": 514153253,
              "success": true,
              "is_refund": true,
              "is_void": false,
              "has_parent_transaction": true,
              "parent_transaction": 514150822,
              "amount_cents": 20000
            }
            """;

        var result = PaymobClient.ParseInquiryResponse(JsonDocument.Parse(json).RootElement);

        Assert.True(result.Found); // a transaction genuinely exists...
        Assert.False(result.Success); // ...but it's not a successful new payment
    }

    [Fact]
    public void ParseInquiryResponse_ReportsNotSuccessful_WhenTheLastTransactionWasVoided()
    {
        var json = """
            {
              "id": 514200000,
              "success": true,
              "is_refund": false,
              "is_void": true,
              "amount_cents": 15000
            }
            """;

        var result = PaymobClient.ParseInquiryResponse(JsonDocument.Parse(json).RootElement);

        Assert.True(result.Found);
        Assert.False(result.Success);
    }

    [Fact]
    public void ParseInquiryResponse_ReportsNotFound_WhenThereIsNoIdAtAll()
    {
        var json = "{}";

        var result = PaymobClient.ParseInquiryResponse(JsonDocument.Parse(json).RootElement);

        Assert.False(result.Found);
        Assert.False(result.Success);
        Assert.Null(result.TransactionId);
    }
}
