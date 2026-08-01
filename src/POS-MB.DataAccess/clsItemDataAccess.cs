using Dapper;
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

    public async Task<int> AddAsync(string name, int categoryId, decimal price, decimal taxRate)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            INSERT INTO Items (ItemName, CategoryId, Price, TaxRate)
            OUTPUT INSERTED.ItemId
            VALUES (@Name, @CategoryId, @Price, @TaxRate);";

        return await connection.ExecuteScalarAsync<int>(
            query, new { Name = name, CategoryId = categoryId, Price = price, TaxRate = taxRate });
    }

    public async Task<bool> UpdateAsync(int id, string name, int categoryId, decimal price, decimal taxRate)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Items
            SET ItemName = @Name,
                CategoryId = @CategoryId,
                Price = @Price,
                TaxRate = @TaxRate,
                UpdatedAt = SYSUTCDATETIME()
            WHERE ItemId = @Id";

        var rowsAffected = await connection.ExecuteAsync(
            query, new { Id = id, Name = name, CategoryId = categoryId, Price = price, TaxRate = taxRate });

        return rowsAffected > 0;
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

    public async Task LogPriceChangeAsync(int itemId, decimal oldPrice, decimal newPrice, decimal oldTaxRate, decimal newTaxRate, int? changedByUserId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            INSERT INTO ItemPriceHistory (ItemId, OldPrice, NewPrice, OldTaxRate, NewTaxRate, ChangedByUserId)
            VALUES (@ItemId, @OldPrice, @NewPrice, @OldTaxRate, @NewTaxRate, @ChangedByUserId);";

        await connection.ExecuteAsync(query, new
        {
            ItemId = itemId,
            OldPrice = oldPrice,
            NewPrice = newPrice,
            OldTaxRate = oldTaxRate,
            NewTaxRate = newTaxRate,
            ChangedByUserId = changedByUserId
        });
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
