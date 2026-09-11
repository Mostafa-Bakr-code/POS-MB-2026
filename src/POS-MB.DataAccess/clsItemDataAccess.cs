using Dapper;
using Microsoft.Data.SqlClient;
using POS_MB.DataAccess.Models;

namespace POS_MB.DataAccess;

public class clsItemDataAccess(ISqlConnectionFactory connectionFactory)
{
    public async Task<IEnumerable<Item>> GetAllAsync(bool includeInactive = false, int? categoryId = null, bool availableOnly = false)
    {
        using var connection = connectionFactory.CreateConnection();

        var query = "SELECT * FROM Items WHERE 1 = 1";
        if (!includeInactive) query += " AND IsActive = 1";
        if (availableOnly) query += " AND IsAvailable = 1";
        if (categoryId is not null) query += " AND CategoryId = @CategoryId";

        return await connection.QueryAsync<Item>(query, new { CategoryId = categoryId });
    }

    public async Task<Item?> GetByIdAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = "SELECT * FROM Items WHERE ItemId = @Id";

        return await connection.QuerySingleOrDefaultAsync<Item>(query, new { Id = id });
    }

    public async Task<bool> ExistsAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = "SELECT COUNT(1) FROM Items WHERE ItemId = @Id";

        return await connection.ExecuteScalarAsync<int>(query, new { Id = id }) > 0;
    }

    public async Task<int> AddAsync(string name, int categoryId, decimal price, decimal taxRate, string? description = null)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            INSERT INTO Items (ItemName, CategoryId, Price, TaxRate, Description)
            OUTPUT INSERTED.ItemId
            VALUES (@Name, @CategoryId, @Price, @TaxRate, @Description);";

        return await connection.ExecuteScalarAsync<int>(
            query, new { Name = name, CategoryId = categoryId, Price = price, TaxRate = taxRate, Description = description });
    }

    // Wraps the Items UPDATE and the (conditional) ItemPriceHistory insert in
    // one transaction - found live: these previously ran on two separate
    // connections with no shared transaction, so a transient failure between
    // them (a dropped connection, a pool reset) could leave the price/tax
    // rate changed with no matching audit row. GET .../price-history is open
    // to every authenticated user specifically as that audit trail, so a
    // silently missing entry there is unrecoverable. oldPrice/oldTaxRate are
    // null when the caller has nothing to compare against (never happens in
    // practice - clsItemBusiness always has the existing row - but keeps this
    // method safely callable without a price-history side effect if that
    // ever changes).
    public async Task<bool> UpdateAsync(int id, string name, int categoryId, decimal price, decimal taxRate, string? description, decimal? oldPrice, decimal? oldTaxRate, int? changedByUserId)
    {
        using var connection = (SqlConnection)connectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            const string updateQuery = @"
                UPDATE Items
                SET ItemName = @Name,
                    CategoryId = @CategoryId,
                    Price = @Price,
                    TaxRate = @TaxRate,
                    Description = @Description,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE ItemId = @Id";

            var rowsAffected = await connection.ExecuteAsync(
                updateQuery, new { Id = id, Name = name, CategoryId = categoryId, Price = price, TaxRate = taxRate, Description = description }, transaction);

            if (rowsAffected > 0 && oldPrice is not null && oldTaxRate is not null && (oldPrice != price || oldTaxRate != taxRate))
            {
                const string historyQuery = @"
                    INSERT INTO ItemPriceHistory (ItemId, OldPrice, NewPrice, OldTaxRate, NewTaxRate, ChangedByUserId)
                    VALUES (@ItemId, @OldPrice, @NewPrice, @OldTaxRate, @NewTaxRate, @ChangedByUserId);";

                await connection.ExecuteAsync(historyQuery, new
                {
                    ItemId = id,
                    OldPrice = oldPrice,
                    NewPrice = price,
                    OldTaxRate = oldTaxRate,
                    NewTaxRate = taxRate,
                    ChangedByUserId = changedByUserId
                }, transaction);
            }

            transaction.Commit();
            return rowsAffected > 0;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<bool> DeactivateAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Items
            SET IsActive = 0,
                UpdatedAt = SYSUTCDATETIME()
            WHERE ItemId = @Id";

        var rowsAffected = await connection.ExecuteAsync(query, new { Id = id });

        return rowsAffected > 0;
    }

    public async Task<bool> ReactivateAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Items
            SET IsActive = 1,
                UpdatedAt = SYSUTCDATETIME()
            WHERE ItemId = @Id";

        var rowsAffected = await connection.ExecuteAsync(query, new { Id = id });

        return rowsAffected > 0;
    }

    public async Task<bool> SetAvailabilityAsync(int id, bool isAvailable)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Items
            SET IsAvailable = @IsAvailable,
                UpdatedAt = SYSUTCDATETIME()
            WHERE ItemId = @Id";

        var rowsAffected = await connection.ExecuteAsync(query, new { Id = id, IsAvailable = isAvailable });

        return rowsAffected > 0;
    }

    public async Task<bool> SetImageUrlAsync(int id, string? imageUrl)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Items
            SET ImageUrl = @ImageUrl,
                UpdatedAt = SYSUTCDATETIME()
            WHERE ItemId = @Id";

        var rowsAffected = await connection.ExecuteAsync(query, new { Id = id, ImageUrl = imageUrl });

        return rowsAffected > 0;
    }

    public async Task<IEnumerable<ItemPriceHistory>> GetPriceHistoryAsync(int itemId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            SELECT h.*, u.UserName AS ChangedByUserName
            FROM ItemPriceHistory h
            LEFT JOIN Users u ON u.UserId = h.ChangedByUserId
            WHERE h.ItemId = @ItemId
            ORDER BY h.ChangedAt DESC";

        return await connection.QueryAsync<ItemPriceHistory>(query, new { ItemId = itemId });
    }
}
