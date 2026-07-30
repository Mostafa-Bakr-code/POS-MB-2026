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

    public async Task<List<ItemDto>> GetItemsAsync(int? categoryId = null, bool includeInactive = false)
    {
        var url = $"api/items?includeInactive={includeInactive}";
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

    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/orders", request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<OrderDto>())!;
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

    private record StartSessionResponse(int LogId);
}
