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

    public async Task<List<CategoryDto>> GetCategoriesAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<List<CategoryDto>>("api/categories");
        return result ?? [];
    }

    public async Task<List<ItemDto>> GetItemsAsync(int? categoryId = null)
    {
        var url = categoryId is null ? "api/items" : $"api/items?categoryId={categoryId}";
        var result = await _httpClient.GetFromJsonAsync<List<ItemDto>>(url);
        return result ?? [];
    }

    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/orders", request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    private record StartSessionResponse(int LogId);
}
