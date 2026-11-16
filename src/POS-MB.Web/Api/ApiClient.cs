using System.Net.Http.Json;
using System.Text.Json;
using POS_MB.Web.Models;

namespace POS_MB.Web.Api;

// Mirrors POS-MB.Mobile's Api/ApiClient.cs - same endpoints, same error
// handling, since this is the same API contract, just called from a browser
// instead of a MAUI app.
public class ApiClient
{
    private readonly HttpClient _httpClient = new(new AuthHeaderHandler()) { BaseAddress = new Uri(ApiConfig.BaseUrl) };

    public Task<(StudentLoginResponse? Result, string? Error)> SignUpAsync(string email, string password) =>
        PostAuthAsync("api/students/signup", email, password);

    public Task<(StudentLoginResponse? Result, string? Error)> LoginAsync(string email, string password) =>
        PostAuthAsync("api/students/login", email, password);

    public async Task<StudentLoginResponse?> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/students/refresh-token", new { RefreshToken = refreshToken });
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<StudentLoginResponse>()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task LogoutAsync(string refreshToken)
    {
        try
        {
            await _httpClient.PostAsJsonAsync("api/students/logout", new { RefreshToken = refreshToken });
        }
        catch (Exception)
        {
            // ignored - client-side logout must proceed regardless of server reachability
        }
    }

    public async Task<List<CategoryDto>> GetCategoriesAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<List<CategoryDto>>("api/categories");
        return result ?? [];
    }

    public async Task<List<ItemDto>> GetItemsAsync(int? categoryId = null, bool availableOnly = true, bool includeInactive = false)
    {
        var url = $"api/items?availableOnly={availableOnly}&includeInactive={includeInactive}";
        if (categoryId is not null) url += $"&categoryId={categoryId}";

        var result = await _httpClient.GetFromJsonAsync<List<ItemDto>>(url);
        return result ?? [];
    }

    public async Task<(OrderDetailDto? Result, string? Error, List<UnavailableItemDto> UnavailableItems)> PlaceOrderAsync(List<OrderItemLineDto> items, bool useSavedCard = false)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("api/students/orders", new PlaceOrderRequest(items, useSavedCard));
        }
        catch (Exception)
        {
            return (null, "Could not reach the server. Check your connection and try again.", []);
        }

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<OrderDetailDto>(), null, []);

        var (error, unavailableItems) = await ExtractOrderErrorAsync(response);
        return (null, error, unavailableItems);
    }

    public async Task<List<OrderSummaryDto>> GetMyOrdersAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        var url = "api/students/orders";
        if (startDate is not null && endDate is not null)
            url += $"?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}";

        var result = await _httpClient.GetFromJsonAsync<List<OrderSummaryDto>>(url);
        return result ?? [];
    }

    public async Task<OrderDetailDto?> GetMyOrderAsync(int orderId)
    {
        var response = await _httpClient.GetAsync($"api/students/orders/{orderId}");
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<OrderDetailDto>()
            : null;
    }

    public async Task<(bool Success, string? Error)> CancelOrderAsync(int orderId)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync($"api/students/orders/{orderId}/cancel", null);
        }
        catch (Exception)
        {
            return (false, "Could not reach the server. Check your connection and try again.");
        }

        if (response.IsSuccessStatusCode) return (true, null);
        return (false, await ExtractErrorAsync(response));
    }

    public async Task<(string? CheckoutUrl, bool AlreadyPaid, string? Error)> ResumeCheckoutAsync(int orderId, bool useSavedCard = false)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync($"api/students/orders/{orderId}/resume-checkout", new { UseSavedCard = useSavedCard });
        }
        catch (Exception)
        {
            return (null, false, "Could not reach the server. Check your connection and try again.");
        }

        if (!response.IsSuccessStatusCode)
            return (null, false, await ExtractErrorAsync(response));

        var result = await response.Content.ReadFromJsonAsync<ResumeCheckoutResponse>();
        return (result?.CheckoutUrl, result?.AlreadyPaid ?? false, null);
    }

    public async Task<(bool Success, string? Error)> ForgotPasswordAsync(string email)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("api/students/forgot-password", new { Email = email });
        }
        catch (Exception)
        {
            return (false, "Could not reach the server. Check your connection and try again.");
        }

        return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
    }

    public async Task<(bool Success, string? Error)> ResetPasswordAsync(string email, string code, string newPassword)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("api/students/reset-password", new { Email = email, Code = code, NewPassword = newPassword });
        }
        catch (Exception)
        {
            return (false, "Could not reach the server. Check your connection and try again.");
        }

        return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("api/students/change-password", new { CurrentPassword = currentPassword, NewPassword = newPassword });
        }
        catch (Exception)
        {
            return (false, "Could not reach the server. Check your connection and try again.");
        }

        return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
    }

    public async Task<(bool Success, string? Error)> RemoveSavedCardAsync()
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.DeleteAsync("api/students/saved-card");
        }
        catch (Exception)
        {
            return (false, "Could not reach the server. Check your connection and try again.");
        }

        return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractErrorAsync(response));
    }

    public async Task<string?> GetSettingValueAsync(string key)
    {
        var response = await _httpClient.GetAsync($"api/settings/{key}");
        if (!response.IsSuccessStatusCode) return null;

        var setting = await response.Content.ReadFromJsonAsync<SettingDto>();
        return setting?.Value;
    }

    public async Task<(bool IsAccepting, string? Reason)> GetAcceptingOnlineOrdersStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("api/settings/accepting-online-orders-status");
            if (!response.IsSuccessStatusCode) return (true, null);

            var status = await response.Content.ReadFromJsonAsync<AcceptingOnlineOrdersStatusDto>();
            return status is null ? (true, null) : (status.IsAccepting, status.Reason);
        }
        catch (Exception)
        {
            return (true, null);
        }
    }

    private record SettingDto(int Id, string Key, string? Value);
    private record ResumeCheckoutResponse(string? CheckoutUrl, bool AlreadyPaid);
    private record AcceptingOnlineOrdersStatusDto(bool IsAccepting, string? Reason);

    private async Task<(StudentLoginResponse? Result, string? Error)> PostAuthAsync(string path, string email, string password)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(path, new { Email = email, Password = password });
        }
        catch (Exception ex)
        {
            return (null, $"Could not reach the server: {ex.Message}");
        }

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<StudentLoginResponse>(), null);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return (null, "Invalid email or password.");

        return (null, await ExtractErrorAsync(response));
    }

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

    private static async Task<(string Error, List<UnavailableItemDto> UnavailableItems)> ExtractOrderErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

            var message = json.TryGetProperty("error", out var errorProp)
                ? errorProp.GetString() ?? "Something went wrong."
                : "Something went wrong.";

            if (json.TryGetProperty("unavailableItems", out var itemsProp))
            {
                var items = JsonSerializer.Deserialize<List<UnavailableItemDto>>(
                    itemsProp.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                return (message, items);
            }

            if (json.TryGetProperty("errors", out var errorsProp))
            {
                var firstField = errorsProp.EnumerateObject().FirstOrDefault();
                var firstMessage = firstField.Value.EnumerateArray().FirstOrDefault().GetString();
                return (firstMessage ?? "Please check your input.", []);
            }

            return (message, []);
        }
        catch (Exception)
        {
            return ("Something went wrong. Please try again.", []);
        }
    }
}
