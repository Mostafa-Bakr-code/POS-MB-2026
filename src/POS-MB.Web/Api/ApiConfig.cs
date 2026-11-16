namespace POS_MB.Web.Api;

public static class ApiConfig
{
    // TEMPORARY while building/testing locally - points at the local API
    // (same URL Mobile's non-device #else branch uses) instead of Azure, so
    // nothing needs to be touched in Azure's production CORS config until
    // this is actually ready to deploy. Switch to Mobile's Azure URL
    // (https://posmb-api-2026-cbe4ceb0dxgvfne6.uaenorth-01.azurewebsites.net/)
    // once this is deployed alongside the API as static files under the same
    // App Service (wwwroot/order) - at that point it's same-origin and no
    // CORS entry is needed at all in production.
    public const string BaseUrl = "https://localhost:7295/";
}
