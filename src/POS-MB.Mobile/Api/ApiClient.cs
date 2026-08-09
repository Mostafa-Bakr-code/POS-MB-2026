using System.Net.Http.Json;
using System.Text.Json;
using POS_MB.Mobile.Models;

namespace POS_MB.Mobile.Api;

public class ApiClient
{
    private readonly HttpClient _httpClient = new(new AuthHeaderHandler()) { BaseAddress = new Uri(ApiConfig.BaseUrl) };

    public Task<(StudentLoginResponse? Result, string? Error)> SignUpAsync(string email, string password) =>
        PostAuthAsync("api/students/signup", email, password);

    public Task<(StudentLoginResponse? Result, string? Error)> LoginAsync(string email, string password) =>
        PostAuthAsync("api/students/login", email, password);

    // Used both by the visible "log in again" flow (access token expired mid-session)
    // and, more importantly, by the silent auto-login attempted at app startup from
    // a refresh token alone - AppSession.Token is empty at that point, so this must
    // not depend on AuthHeaderHandler attaching a bearer token (the endpoint doesn't
    // need one - the refresh token itself is the credential).
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

    // Best-effort - if this fails (no connection, token already expired) the
    // client-side logout must still proceed, since the whole point is to get
    // the student signed out of the device regardless of server reachability.
    public async Task LogoutAsync(string refreshToken)
    {
        try
        {
            await _httpClient.PostAsJsonAsync("api/students/logout", new { RefreshToken = refreshToken });
        }
        catch (Exception)
        {
            // ignored - see comment above
        }
    }

    public async Task<List<CategoryDto>> GetCategoriesAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<List<CategoryDto>>("api/categories");
        return result ?? [];
    }

    // availableOnly/includeInactive default to the menu's own needs (only show
    // what a student could actually order right now); order-history item-name
    // resolution needs the opposite (includeInactive: true) since a past order
    // can reference an item that's since gone unavailable or been retired.
    public async Task<List<ItemDto>> GetItemsAsync(int? categoryId = null, bool availableOnly = true, bool includeInactive = false)
    {
        var url = $"api/items?availableOnly={availableOnly}&includeInactive={includeInactive}";
        if (categoryId is not null) url += $"&categoryId={categoryId}";

        var result = await _httpClient.GetFromJsonAsync<List<ItemDto>>(url);
        return result ?? [];
    }

    public async Task<(OrderDetailDto? Result, string? Error, List<UnavailableItemDto> UnavailableItems)> PlaceOrderAsync(List<OrderItemLineDto> items)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("api/students/orders", new PlaceOrderRequest(items));
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

        // 404 (order gone/not theirs) has no useful message to show beyond the
        // generic fallback; a 400 (order already past Placed) carries the real
        // reason from clsOrderBusiness.CancelForStudentAsync and should be
        // shown as-is rather than a generic "could not cancel".
        return (false, await ExtractErrorAsync(response));
    }

    public async Task<string?> GetSettingValueAsync(string key)
    {
        var response = await _httpClient.GetAsync($"api/settings/{key}");
        if (!response.IsSuccessStatusCode) return null;

        var setting = await response.Content.ReadFromJsonAsync<SettingDto>();
        return setting?.Value;
    }

    // Combines the manual "Accepting Online Orders" toggle and the
    // heartbeat-derived offline check into one answer - same logic
    // CreateOrderAsync itself enforces, so the banner can never claim
    // something different from what actually happens at checkout. Defaults
    // to accepting on a network failure - a transient error reading this
    // status shouldn't itself put up a false "closed" banner.
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
    private record AcceptingOnlineOrdersStatusDto(bool IsAccepting, string? Reason);

    private async Task<(StudentLoginResponse? Result, string? Error)> PostAuthAsync(string path, string email, string password)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(path, new { Email = email, Password = password });
        }
        catch (Exception)
        {
            return (null, "Could not reach the server. Check your connection and try again.");
        }

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<StudentLoginResponse>(), null);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return (null, "Invalid email or password.");

        return (null, await ExtractErrorAsync(response));
    }

    // The API returns two different error shapes depending on what rejected the
    // request: DataAnnotations validation failures come back as ProblemDetails
    // with an "errors" object (field name -> message list); business-rule
    // rejections (ArgumentException, e.g. "email already exists") come back as
    // a plain {"error": "..."} object via GlobalExceptionHandler.
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

    // Order placement can fail with the extra "unavailableItems" field (see
    // ItemsUnavailableException/GlobalExceptionHandler) so the cart can
    // auto-remove exactly the offending lines instead of the plain string
    // ExtractErrorAsync returns for every other error shape.
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
