using System.Transactions;
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
    private const string ConnectionString =
        "Server=.;Database=POS-MB-Test;User Id=sa;Password=sa123456;TrustServerCertificate=True;";

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

        CategoryBusiness = new clsCategoryBusiness(new clsCategoryDataAccess(ConnectionFactory));
        ItemBusiness = new clsItemBusiness(new clsItemDataAccess(ConnectionFactory), SettingsBusiness);
        UserBusiness = new clsUserBusiness(new clsUserDataAccess(ConnectionFactory));
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

    public void Dispose()
    {
        _scope.Dispose();
        GC.SuppressFinalize(this);
    }
}
