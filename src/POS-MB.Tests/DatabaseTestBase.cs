using System.Transactions;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using POS_MB.Business;
using POS_MB.DataAccess;

namespace POS_MB.Tests;

// Every test that inherits this runs inside a TransactionScope that is never
// Complete()d, so whatever it inserts/updates is automatically rolled back when the
// test ends - no manual cleanup, and no test can ever interfere with another. This
// points at a separate POS-MB-Test database (created from the same schema.sql as
// the real one) - the real POS-MB database is never touched by any test.
public abstract class DatabaseTestBase : IDisposable
{
    // Read from local user secrets (dotnet user-secrets, scoped to this project),
    // never hardcoded - same reasoning as the API project's connection string, even
    // though this only ever points at the throwaway test database.
    private static readonly string ConnectionString = new ConfigurationBuilder()
        .AddUserSecrets<DatabaseTestBase>()
        .Build()
        .GetConnectionString("TestConnection")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:TestConnection is not set. Run: dotnet user-secrets set \"ConnectionStrings:TestConnection\" \"...\" in POS-MB.Tests.");

    private readonly TransactionScope _scope;

    protected ISqlConnectionFactory ConnectionFactory { get; } = new SqlConnectionFactory(ConnectionString);

    protected clsCategoryBusiness CategoryBusiness { get; }
    protected clsItemBusiness ItemBusiness { get; }
    protected clsUserBusiness UserBusiness { get; }
    protected clsOrderBusiness OrderBusiness { get; }
    protected clsReportingBusiness ReportingBusiness { get; }
    protected clsSettingsBusiness SettingsBusiness { get; }

    protected DatabaseTestBase()
    {
        _scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        var settingsDataAccess = new clsSettingsDataAccess(ConnectionFactory);
        SettingsBusiness = new clsSettingsBusiness(settingsDataAccess);

        var refreshTokenBusiness = new clsRefreshTokenBusiness(
            new clsRefreshTokenDataAccess(ConnectionFactory), NullLogger<clsRefreshTokenBusiness>.Instance);

        CategoryBusiness = new clsCategoryBusiness(new clsCategoryDataAccess(ConnectionFactory));
        ItemBusiness = new clsItemBusiness(new clsItemDataAccess(ConnectionFactory), SettingsBusiness);
        UserBusiness = new clsUserBusiness(new clsUserDataAccess(ConnectionFactory), refreshTokenBusiness);
        OrderBusiness = new clsOrderBusiness(new clsOrderDataAccess(ConnectionFactory), SettingsBusiness);
        ReportingBusiness = new clsReportingBusiness(new clsReportingDataAccess(ConnectionFactory), SettingsBusiness);
    }

    // Round test data, matching the hand-verifiable "Test - Verify" style already
    // used for manual testing - so expected totals can be checked by hand, not just
    // trusted because the test says so.
    protected async Task<int> CreateCategoryAsync(string name = "Test Category") =>
        await CategoryBusiness.CreateAsync(name);

    protected async Task<int> CreateItemAsync(int categoryId, string name, decimal price, decimal taxRate = 14m) =>
        await ItemBusiness.CreateAsync(name, categoryId, price, taxRate);

    protected async Task<int> CreateUserAsync(string userName = "test-cashier") =>
        await UserBusiness.CreateAsync(userName, "password123", permissions: 0);

    // CreateOrderAsync always stamps Date as DateTime.UtcNow (correctly - see project
    // notes on why), which makes "an order placed right after local midnight" only
    // reproducible at whatever moment the test happens to run. Backdating it directly
    // lets the timezone-boundary tests be deterministic regardless of when they run.
    protected async Task SetOrderDateAsync(int orderId, DateTime utcDate)
    {
        using var connection = ConnectionFactory.CreateConnection();
        await connection.ExecuteAsync("UPDATE Orders SET [Date] = @Date WHERE OrderId = @OrderId",
            new { Date = utcDate, OrderId = orderId });
    }

    public void Dispose()
    {
        _scope.Dispose();
        GC.SuppressFinalize(this);
    }
}
