using System.Text.Json;
using POS_MB.Business.Payments;

namespace POS_MB.Tests;

// Verifies the HMAC field-concatenation logic against Paymob's own documented
// worked example (their "HMAC Transaction Callback" reference page) - not a
// DB test, doesn't need DatabaseTestBase, this is pure string-building logic.
// Deliberately doesn't try to verify the actual HMAC-SHA512 hash output itself
// (Paymob's docs never disclose which secret produced their sample hash, only
// the concatenated string), but the hashing step is .NET's own well-tested
// HMACSHA512 - the part actually worth proving correct here is that this code
// pulls the right fields, in the right order, in the right string format.
public class PaymobHmacTests
{
    [Fact]
    public void BuildHmacConcatenatedString_MatchesPaymobsDocumentedExample()
    {
        var payload = JsonDocument.Parse("""
        {
          "type": "TRANSACTION",
          "obj": {
            "id": 192036465,
            "pending": false,
            "amount_cents": 100000,
            "success": true,
            "is_auth": false,
            "is_capture": false,
            "is_standalone_payment": true,
            "is_voided": false,
            "is_refunded": false,
            "is_3d_secure": true,
            "integration_id": 4097558,
            "has_parent_transaction": false,
            "order": {
              "id": 217503754
            },
            "created_at": "2024-06-13T11:33:44.592345",
            "currency": "EGP",
            "source_data": {
              "pan": "2346",
              "type": "card",
              "sub_type": "MasterCard"
            },
            "error_occured": false,
            "owner": 302852
          }
        }
        """).RootElement;

        var concatenated = PaymobClient.BuildHmacConcatenatedString(payload);

        Assert.Equal(
            "1000002024-06-13T11:33:44.592345EGPfalsefalse1920364654097558truefalsefalsefalsetruefalse217503754302852false2346MasterCardcardtrue",
            concatenated);
    }

    // Same idea, for the separate "a card was saved" callback - Paymob's
    // "HMAC Card Token Callback" reference page, a completely different
    // field list/shape from the transaction callback above.
    [Fact]
    public void BuildCardTokenHmacConcatenatedString_MatchesPaymobsDocumentedExample()
    {
        var payload = JsonDocument.Parse("""
        {
          "type": "TOKEN",
          "obj": {
            "id": 8555026,
            "token": "e98aceb96f5a370ddf46460db9d555f88bf12448f80e1839b39f78ab",
            "masked_pan": "xxxx-xxxx-xxxx-2346",
            "merchant_id": 246628,
            "card_subtype": "MasterCard",
            "created_at": "2024-11-13T12:32:23.859982",
            "email": "test@test.com",
            "order_id": "264064419",
            "user_added": false,
            "next_payment_intention": "pi_test_2a9c29ead1734ce8ad09ae4936019992"
          }
        }
        """).RootElement;

        var concatenated = PaymobClient.BuildCardTokenHmacConcatenatedString(payload);

        Assert.Equal(
            "MasterCard2024-11-13T12:32:23.859982test@test.com8555026xxxx-xxxx-xxxx-2346246628264064419e98aceb96f5a370ddf46460db9d555f88bf12448f80e1839b39f78ab",
            concatenated);
    }
}
