using Microsoft.Extensions.Logging.Abstractions;
using POS_MB.Business;
using POS_MB.Business.Payments;
using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// Covers storing/removing a saved card (clsStudentBusiness) and actually
// using one at checkout (clsOrderBusiness.CreateStudentOrderAsync). Uses a
// fake PaymobClient - never touches the real network, same reasoning as
// every other Paymob test in this project.
public class PaymobSavedCardTests : DatabaseTestBase
{
    private class FakePaymobClient : PaymobClient
    {
        public FakePaymobClient() : base(new HttpClient(), new PaymobOptions()) { }

        public int CreateIntentionCallCount { get; private set; }
        public string? LastSavedCardToken { get; private set; }

        public override Task<PaymobIntentionResult> CreateIntentionAsync(
            decimal amountEgp, string specialReference, string customerEmail, int expirationSeconds,
            IReadOnlyList<PaymobItemLine>? items = null, string? savedCardToken = null)
        {
            CreateIntentionCallCount++;
            LastSavedCardToken = savedCardToken;
            return Task.FromResult(new PaymobIntentionResult("fake_client_secret", 123456789));
        }
    }

    private clsOrderBusiness CreateOrderBusinessWith(FakePaymobClient fakeClient) =>
        new(new POS_MB.DataAccess.clsOrderDataAccess(ConnectionFactory), SettingsBusiness, fakeClient, NullLogger<clsOrderBusiness>.Instance, StudentBusiness);

    [Fact]
    public async Task SaveCardTokenAsync_StoresTheCardOnTheMatchingStudent()
    {
        var email = $"saved-card-{Guid.NewGuid():N}@example.com";
        var studentId = await CreateStudentAsync(email);

        await StudentBusiness.SaveCardTokenAsync(email, "tok_abc123", "xxxx-xxxx-xxxx-2346", "MasterCard");

        var student = await StudentBusiness.GetByIdAsync(studentId);
        Assert.Equal("tok_abc123", student!.SavedCardToken);
        Assert.Equal("xxxx-xxxx-xxxx-2346", student.SavedCardMaskedPan);
        Assert.Equal("MasterCard", student.SavedCardSubtype);
    }

    [Fact]
    public async Task SaveCardTokenAsync_DoesNothing_ForAnUnknownEmail()
    {
        // A webhook has no legitimate reason to fail loudly over an email
        // that doesn't match a real account - must not throw.
        await StudentBusiness.SaveCardTokenAsync("nobody-here@example.com", "tok_xyz", null, null);
    }

    [Fact]
    public async Task RemoveSavedCardAsync_ClearsAllThreeFields()
    {
        var email = $"saved-card-{Guid.NewGuid():N}@example.com";
        var studentId = await CreateStudentAsync(email);
        await StudentBusiness.SaveCardTokenAsync(email, "tok_abc123", "xxxx-xxxx-xxxx-2346", "MasterCard");

        await StudentBusiness.RemoveSavedCardAsync(studentId);

        var student = await StudentBusiness.GetByIdAsync(studentId);
        Assert.Null(student!.SavedCardToken);
        Assert.Null(student.SavedCardMaskedPan);
        Assert.Null(student.SavedCardSubtype);
    }

    [Fact]
    public async Task CreateStudentOrder_WithUseSavedCard_PassesTheTokenToPaymob()
    {
        var email = $"saved-card-{Guid.NewGuid():N}@example.com";
        var studentId = await CreateStudentAsync(email);
        await StudentBusiness.SaveCardTokenAsync(email, "tok_abc123", "xxxx-xxxx-xxxx-2346", "MasterCard");

        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var fake = new FakePaymobClient();
        var orderBusiness = CreateOrderBusinessWith(fake);

        await orderBusiness.CreateStudentOrderAsync(studentId, email, [new NewOrderItem(itemId, 1, null)], useSavedCard: true);

        Assert.Equal(1, fake.CreateIntentionCallCount);
        Assert.Equal("tok_abc123", fake.LastSavedCardToken);
    }

    [Fact]
    public async Task CreateStudentOrder_WithUseSavedCard_ButNoCardSaved_ProceedsWithoutOne()
    {
        var email = $"saved-card-{Guid.NewGuid():N}@example.com";
        var studentId = await CreateStudentAsync(email);
        // No SaveCardTokenAsync call - nothing saved.

        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var fake = new FakePaymobClient();
        var orderBusiness = CreateOrderBusinessWith(fake);

        await orderBusiness.CreateStudentOrderAsync(studentId, email, [new NewOrderItem(itemId, 1, null)], useSavedCard: true);

        Assert.Equal(1, fake.CreateIntentionCallCount);
        Assert.Null(fake.LastSavedCardToken); // falls back to a normal checkout, not an error
    }

    [Fact]
    public async Task CreateStudentOrder_WithoutUseSavedCard_NeverSendsATokenEvenIfOneExists()
    {
        var email = $"saved-card-{Guid.NewGuid():N}@example.com";
        var studentId = await CreateStudentAsync(email);
        await StudentBusiness.SaveCardTokenAsync(email, "tok_abc123", "xxxx-xxxx-xxxx-2346", "MasterCard");

        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var fake = new FakePaymobClient();
        var orderBusiness = CreateOrderBusinessWith(fake);

        // useSavedCard defaults to false - a student must explicitly choose
        // to use it, never assumed silently just because one exists.
        await orderBusiness.CreateStudentOrderAsync(studentId, email, [new NewOrderItem(itemId, 1, null)]);

        Assert.Null(fake.LastSavedCardToken);
    }
}
