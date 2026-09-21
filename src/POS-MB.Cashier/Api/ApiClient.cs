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

    private record StartSessionResponse(int LogId);
    private record SettingDto(int Id, string Key, string? Value);
}
