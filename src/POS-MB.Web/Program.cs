using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using POS_MB.Web;
using POS_MB.Web.Api;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// One ApiClient per browser tab (Scoped == one WASM app instance) - same
// single-client pattern as Mobile, just DI-registered instead of
// constructed inline, since components get their dependencies via @inject.
builder.Services.AddScoped<ApiClient>();

await builder.Build().RunAsync();
