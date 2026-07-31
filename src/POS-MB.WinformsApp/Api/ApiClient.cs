using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using POS_MB.WinformsApp.Models;

namespace POS_MB.WinformsApp.Api;

public class ApiClient
{
    private readonly HttpClient _httpClient;

    public ApiClient()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var baseUrl = configuration["ApiBaseUrl"]
            ?? throw new InvalidOperationException("ApiBaseUrl is not configured in appsettings.json.");

        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public async Task<UserDto?> VerifyCredentialsAsync(string userName, string password)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/users/verify-credentials", new VerifyCredentialsRequest(userName, password));

        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<UserDto>();
    }

    public async Task<int> StartSessionAsync(int userId)
    {
        var response = await _httpClient.PostAsJsonAsync("api/logs/start", new { UserId = userId });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<StartSessionResponse>();
        return result!.LogId;
    }

    public async Task EndSessionAsync(int logId)
    {
        await _httpClient.PostAsync($"api/logs/{logId}/end", null);
    }

    public async Task<List<CategoryDto>> GetCategoriesAsync(bool includeInactive = false)
    {
        var url = $"api/categories?includeInactive={includeInactive}";
        var result = await _httpClient.GetFromJsonAsync<List<CategoryDto>>(url);
        return result ?? [];
    }

    public async Task CreateCategoryAsync(string name)
    {
        var response = await _httpClient.PostAsJsonAsync("api/categories", new { Name = name });
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateCategoryAsync(int categoryId, string name)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/categories/{categoryId}", new { Name = name });
        response.EnsureSuccessStatusCode();
    }

    public async Task DeactivateCategoryAsync(int categoryId)
    {
        var response = await _httpClient.PostAsync($"api/categories/{categoryId}/deactivate", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<ItemDto>> GetItemsAsync(int? categoryId = null, bool includeInactive = false, bool availableOnly = false)
    {
        var url = $"api/items?includeInactive={includeInactive}&availableOnly={availableOnly}";
        if (categoryId is not null) url += $"&categoryId={categoryId}";
        var result = await _httpClient.GetFromJsonAsync<List<ItemDto>>(url);
        return result ?? [];
    }

    public async Task CreateItemAsync(string name, int categoryId, decimal price)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/items", new { Name = name, CategoryId = categoryId, Price = price });
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateItemAsync(int itemId, string name, int categoryId, decimal price)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"api/items/{itemId}", new { Name = name, CategoryId = categoryId, Price = price });
        response.EnsureSuccessStatusCode();
    }

    public async Task DeactivateItemAsync(int itemId)
    {
        var response = await _httpClient.PostAsync($"api/items/{itemId}/deactivate", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetItemAvailabilityAsync(int itemId, bool isAvailable)
    {
        var response = await _httpClient.PostAsJsonAsync($"api/items/{itemId}/availability", new { IsAvailable = isAvailable });
        response.EnsureSuccessStatusCode();
    }

    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/orders", request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    public async Task<List<OrderDto>> GetOrdersAsync(DateTime? startDate, DateTime? endDate, OrderSource? orderSource)
    {
        var url = "api/orders" + DateQuery(startDate, endDate);
        if (orderSource is not null) url += $"&orderSource={orderSource}";
        var result = await _httpClient.GetFromJsonAsync<List<OrderDto>>(url);
        return result ?? [];
    }

    public async Task<OrderDto?> GetOrderByIdAsync(int orderId)
    {
        var response = await _httpClient.GetAsync($"api/orders/{orderId}");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<OrderDto>();
    }

    public async Task<List<UserDto>> GetUsersAsync(bool includeInactive = false)
    {
        var url = $"api/users?includeInactive={includeInactive}";
        var result = await _httpClient.GetFromJsonAsync<List<UserDto>>(url);
        return result ?? [];
    }

    public async Task CreateUserAsync(string userName, string password, int permissions)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/users", new { UserName = userName, Password = password, Permissions = permissions });
        response.EnsureSuccessStatusCode();
    }

    // password: null/blank keeps the user's existing password unchanged.
    public async Task UpdateUserAsync(int userId, string userName, string? password, int permissions)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"api/users/{userId}", new { UserName = userName, Password = password, Permissions = permissions });
        response.EnsureSuccessStatusCode();
    }

    public async Task DeactivateUserAsync(int userId)
    {
        var response = await _httpClient.PostAsync($"api/users/{userId}/deactivate", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<SalesSummaryDto> GetSalesSummaryAsync(DateTime? startDate, DateTime? endDate)
    {
        var url = "api/reports/sales-summary" + DateQuery(startDate, endDate);
        return (await _httpClient.GetFromJsonAsync<SalesSummaryDto>(url))!;
    }

    public async Task<List<ItemSalesRowDto>> GetItemSalesAsync(DateTime? startDate, DateTime? endDate, bool groupByDay)
    {
        var url = "api/reports/item-sales" + DateQuery(startDate, endDate) + $"&groupByDay={groupByDay}";
        var result = await _httpClient.GetFromJsonAsync<List<ItemSalesRowDto>>(url);
        return result ?? [];
    }

    public async Task<List<ItemSalesRowDto>> GetTopSellersAsync(DateTime? startDate, DateTime? endDate, TopSellersSortBy sortBy, int take)
    {
        var url = "api/reports/top-sellers" + DateQuery(startDate, endDate) + $"&sortBy={sortBy}&take={take}";
        var result = await _httpClient.GetFromJsonAsync<List<ItemSalesRowDto>>(url);
        return result ?? [];
    }

    public async Task<List<StaffPerformanceRowDto>> GetStaffPerformanceAsync(DateTime? startDate, DateTime? endDate)
    {
        var url = "api/reports/staff-performance" + DateQuery(startDate, endDate);
        var result = await _httpClient.GetFromJsonAsync<List<StaffPerformanceRowDto>>(url);
        return result ?? [];
    }

    public async Task<byte[]> DownloadReportExcelAsync(string reportPath, DateTime? startDate, DateTime? endDate, string extraParams = "")
    {
        var url = $"api/reports/{reportPath}" + DateQuery(startDate, endDate) + "&format=excel" + extraParams;
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<string?> GetSettingValueAsync(string key)
    {
        var response = await _httpClient.GetAsync($"api/settings/{key}");
        if (!response.IsSuccessStatusCode) return null;

        var setting = await response.Content.ReadFromJsonAsync<SettingDto>();
        return setting?.Value;
    }

    public async Task SetSettingValueAsync(string key, string value)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/settings/{key}", new { Value = value });
        response.EnsureSuccessStatusCode();
    }

    private static string DateQuery(DateTime? startDate, DateTime? endDate)
    {
        var query = "?x=1";
        if (startDate is not null) query += $"&startDate={startDate:yyyy-MM-dd}";
        if (endDate is not null) query += $"&endDate={endDate:yyyy-MM-dd}";
        return query;
    }

    private record StartSessionResponse(int LogId);
    private record SettingDto(int Id, string Key, string? Value);
}
