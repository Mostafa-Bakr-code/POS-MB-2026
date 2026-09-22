using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using POS_MB.Cashier.Models;

namespace POS_MB.Cashier.Api;

// Ported from POS_MB.WinformsApp.Api.ApiClient, but reshaped to match
// POS-MB.Mobile's style: (Result, Error) tuple returns instead of throwing,
// one HttpClient per instance, constructed fresh (new ApiClient()) wherever
// a page needs one - same conventions as Mobile, for consistency across
// this codebase's two MAUI apps. Endpoint paths/DTOs stay identical to
// WinForms', since both talk to the exact same POS-MB.API.
//
// Only the methods Phase 2 (login/session/shell) needs are here so far -
// more get added alongside whichever later phase's screen first needs them,
// rather than porting all ~40 WinForms methods speculatively up front.
public class ApiClient
{
    private readonly HttpClient _httpClient = new(new AuthHeaderHandler()) { BaseAddress = new Uri(ApiConfig.BaseUrl) };

    public async Task<(LoginResponse? Result, string? Error)> VerifyCredentialsAsync(string userName, string password)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(
                "api/users/verify-credentials", new VerifyCredentialsRequest(userName, password));
        }
        catch (Exception ex)
        {
            return (null, $"Could not reach the server: {ex.Message}");
        }

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<LoginResponse>(), null);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return (null, "Invalid username or password.");

        return (null, await ExtractErrorAsync(response));
    }

    // Used both by an explicit re-login and by TokenRefreshTimer's silent
    // background renewal - must not depend on AuthHeaderHandler attaching a
    // bearer token, since the refresh token itself is the credential here.
    public async Task<LoginResponse?> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/users/refresh-token", new { RefreshToken = refreshToken });
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<LoginResponse>()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Best-effort - if this fails (no connection, token already expired) the
    // client-side logout must still proceed, since the whole point is to
    // get the cashier signed out of this device regardless of server
    // reachability.
    public async Task LogoutAsync(string refreshToken)
    {
        try { await _httpClient.PostAsJsonAsync("api/users/logout", new { RefreshToken = refreshToken }); }
        catch (Exception) { /* best-effort */ }
    }

    // The API derives who's starting a session from the caller's own token,
    // not a client-supplied id - nothing needs to be sent in the body.
    public async Task<int?> StartSessionAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("api/logs/start", null);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<StartSessionResponse>();
            return result?.LogId;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task EndSessionAsync(int logId)
    {
        try { await _httpClient.PostAsync($"api/logs/{logId}/end", null); }
        catch (Exception) { /* best-effort */ }
    }

    public async Task<string?> GetSettingValueAsync(string key)
    {
        try
        {
            var response = await _httpClient.GetAsync($"api/settings/{key}");
            if (!response.IsSuccessStatusCode) return null;

            var setting = await response.Content.ReadFromJsonAsync<SettingDto>();
            return setting?.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Best-effort - a failed heartbeat just means this tick doesn't count as
    // "the shop is watching"; it should never surface an error to the
    // cashier or disrupt anything else the screen is doing.
    public async Task<bool> SendHeartbeatAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("api/settings/heartbeat", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<List<CategoryDto>> GetCategoriesAsync(bool includeInactive = false)
    {
        try
        {
            var url = $"api/categories?includeInactive={includeInactive}";
            var result = await _httpClient.GetFromJsonAsync<List<CategoryDto>>(url);
            return result ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async Task<(int? CategoryId, string? Error)> CreateCategoryAsync(string name)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("api/categories", new { Name = name });
        }
        catch (Exception ex)
        {
            return (null, $"Could not reach the server: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode) return (null, await ExtractErrorAsync(response));

        var created = await response.Content.ReadFromJsonAsync<CategoryDto>();
        return (created!.CategoryId, null);
    }

    public async Task<(bool Success, string? Error)> UpdateCategoryAsync(int categoryId, string name)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PutAsJsonAsync($"api/categories/{categoryId}", new { Name = name });
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server: {ex.Message}");
        }

        return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
    }

    // Takes a Stream + file name rather than a file path, unlike WinForms'
    // equivalent - a picked file on Android comes back as a content:// URI
    // with a readable stream, not a real filesystem path, so a path-based
    // API wouldn't work cross-platform.
    public async Task<(bool Success, string? Error)> UploadCategoryImageAsync(int categoryId, Stream fileStream, string fileName)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(fileStream);
            content.Add(streamContent, "file", fileName);

            var response = await _httpClient.PostAsync($"api/categories/{categoryId}/image", content);
            return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server: {ex.Message}");
        }
    }

    public async Task RemoveCategoryImageAsync(int categoryId)
    {
        try { await _httpClient.DeleteAsync($"api/categories/{categoryId}/image"); }
        catch (Exception) { /* best-effort */ }
    }

    public async Task<(bool Success, string? Error)> DeactivateCategoryAsync(int categoryId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"api/categories/{categoryId}/deactivate", null);
            return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? Error)> ReactivateCategoryAsync(int categoryId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"api/categories/{categoryId}/reactivate", null);
            return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server: {ex.Message}");
        }
    }

    public string ResolveImageUrl(string relativeUrl) => new Uri(_httpClient.BaseAddress!, relativeUrl).ToString();

    public async Task<List<ItemDto>> GetItemsAsync(int? categoryId = null, bool includeInactive = false, bool availableOnly = false)
    {
        try
        {
            var url = $"api/items?includeInactive={includeInactive}&availableOnly={availableOnly}";
            if (categoryId is not null) url += $"&categoryId={categoryId}";
            var result = await _httpClient.GetFromJsonAsync<List<ItemDto>>(url);
            return result ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async Task<(int? ItemId, string? Error)> CreateItemAsync(string name, int categoryId, decimal price, string? description = null)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(
                "api/items", new { Name = name, CategoryId = categoryId, Price = price, Description = description });
        }
        catch (Exception ex)
        {
            return (null, $"Could not reach the server: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode) return (null, await ExtractErrorAsync(response));

        var created = await response.Content.ReadFromJsonAsync<ItemDto>();
        return (created!.ItemId, null);
    }

    public async Task<(bool Success, string? Error)> UpdateItemAsync(int itemId, string name, int categoryId, decimal price, string? description = null)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PutAsJsonAsync(
                $"api/items/{itemId}", new { Name = name, CategoryId = categoryId, Price = price, Description = description });
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server: {ex.Message}");
        }

        return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
    }

    // Takes a Stream + file name rather than a file path - same reasoning
    // as UploadCategoryImageAsync.
    public async Task<(bool Success, string? Error)> UploadItemImageAsync(int itemId, Stream fileStream, string fileName)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(fileStream);
            content.Add(streamContent, "file", fileName);

            var response = await _httpClient.PostAsync($"api/items/{itemId}/image", content);
            return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server: {ex.Message}");
        }
    }

    public async Task RemoveItemImageAsync(int itemId)
    {
        try { await _httpClient.DeleteAsync($"api/items/{itemId}/image"); }
        catch (Exception) { /* best-effort */ }
    }

    public async Task<List<ItemPriceHistoryDto>> GetItemPriceHistoryAsync(int itemId)
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<List<ItemPriceHistoryDto>>($"api/items/{itemId}/price-history");
            return result ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async Task<(bool Success, string? Error)> DeactivateItemAsync(int itemId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"api/items/{itemId}/deactivate", null);
            return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? Error)> ReactivateItemAsync(int itemId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"api/items/{itemId}/reactivate", null);
            return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server: {ex.Message}");
        }
    }

    public async Task<List<UserDto>> GetUsersAsync(bool includeInactive = false)
    {
        try
        {
            var url = $"api/users?includeInactive={includeInactive}";
            var result = await _httpClient.GetFromJsonAsync<List<UserDto>>(url);
            return result ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async Task<(bool Success, string? Error)> CreateUserAsync(string userName, string password, int permissions)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "api/users", new { UserName = userName, Password = password, Permissions = permissions });
            return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server: {ex.Message}");
        }
    }

    // password: null/blank keeps the user's existing password unchanged.
    public async Task<(bool Success, string? Error)> UpdateUserAsync(int userId, string userName, string? password, int permissions)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync(
                $"api/users/{userId}", new { UserName = userName, Password = password, Permissions = permissions });
            return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? Error)> DeactivateUserAsync(int userId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"api/users/{userId}/deactivate", null);
            return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? Error)> ReactivateUserAsync(int userId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"api/users/{userId}/reactivate", null);
            return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? Error)> SetItemAvailabilityAsync(int itemId, bool isAvailable)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"api/items/{itemId}/availability", new { IsAvailable = isAvailable });
            return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server: {ex.Message}");
        }
    }

    // The API returns two different error shapes depending on what
    // rejected the request: DataAnnotations validation failures come back
    // as ProblemDetails with an "errors" object (field name -> message
    // list); business-rule rejections come back as a plain
    // {"error": "..."} object via GlobalExceptionHandler.
    private static async Task<string> ExtractErrorAsync(HttpResponseMessage response)
    {
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            var json = await JsonSerializer.DeserializeAsync<JsonElement>(stream);

            if (json.TryGetProperty("error", out var errorProp))
                return errorProp.GetString() ?? "Something went wrong.";

            if (json.TryGetProperty("errors", out var errorsProp))
            {
                var firstField = errorsProp.EnumerateObject().FirstOrDefault();
                var firstMessage = firstField.Value.EnumerateArray().FirstOrDefault().GetString();
                return firstMessage ?? "Please check your input.";
            }
        }
        catch (Exception)
        {
            // fall through to the generic message below
        }

        return "Something went wrong. Please try again.";
    }

    public async Task<List<OrderDto>> GetOrdersAsync(DateTime? startDate, DateTime? endDate, OrderSource? orderSource)
    {
        try
        {
            var url = "api/orders" + DateQuery(startDate, endDate);
            if (orderSource is not null) url += $"&orderSource={orderSource}";
            var result = await _httpClient.GetFromJsonAsync<List<OrderDto>>(url);
            return result ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async Task<OrderDto?> GetOrderByIdAsync(int orderId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"api/orders/{orderId}");
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<OrderDto>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<List<LogDto>> GetLogsAsync(DateTime? startDate, DateTime? endDate)
    {
        try
        {
            var url = "api/logs" + DateQuery(startDate, endDate);
            var result = await _httpClient.GetFromJsonAsync<List<LogDto>>(url);
            return result ?? [];
        }
        catch (Exception)
        {
            return [];
        }
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
