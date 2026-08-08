using System.Net.Http.Json;
using System.Text.Json;
using POS_MB.Mobile.Models;

namespace POS_MB.Mobile.Api;

public class ApiClient
{
    private readonly HttpClient _httpClient = new() { BaseAddress = new Uri(ApiConfig.BaseUrl) };

    public Task<(StudentLoginResponse? Result, string? Error)> SignUpAsync(string email, string password) =>
        PostAuthAsync("api/students/signup", email, password);

    public Task<(StudentLoginResponse? Result, string? Error)> LoginAsync(string email, string password) =>
        PostAuthAsync("api/students/login", email, password);

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
}
