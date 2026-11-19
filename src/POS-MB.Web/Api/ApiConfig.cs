namespace POS_MB.Web.Api;

public static class ApiConfig
{
    // Same Azure URL Mobile talks to - this app is published as static
    // files under the API's own wwwroot/order (same pattern as wwwroot/chef,
    // the chef tablet page), so it's served from this exact domain and this
    // is a same-origin call in production, no CORS entry needed there at
    // all. Local development instead points at the local API - see
    // appsettings.Development.json's Cors:AllowedOrigins for the matching
    // local-only CORS entries this needs while running via `dotnet run`.
#if DEBUG
    public const string BaseUrl = "https://localhost:7295/";
#else
    public const string BaseUrl = "https://posmb-api-2026-cbe4ceb0dxgvfne6.uaenorth-01.azurewebsites.net/";
#endif
}
