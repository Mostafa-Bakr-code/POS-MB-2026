using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using POS_MB.API;
using POS_MB.API.Auth;
using POS_MB.Business;
using POS_MB.DataAccess;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Rolling daily file, kept alongside the source tree (not bin/) so a rebuild or
// dotnet clean never wipes audit history. Security-relevant events (failed
// logins, permission denials, account changes, order cancellations, rate-limit
// trips) are logged explicitly at their source below; this just gives every
// ILogger call - including GlobalExceptionHandler's existing crash logging - a
// persistent home instead of only the console window.
builder.Host.UseSerilog((context, configuration) => configuration
    .MinimumLevel.Information()
    // The framework's own per-request logging ("Request starting", "Executing
    // action", ...) is useful for live debugging but drowns out the actual
    // audit signal in a persisted file - quieted to warnings/errors only.
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", "pos-mb-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30));

// Add services to the container.

// AddProblemDetails is required alongside AddExceptionHandler for the
// parameterless UseExceptionHandler() below - it's used as ASP.NET Core's own
// fallback only if GlobalExceptionHandler ever doesn't handle something (it
// always does), but the framework requires it to be registered regardless.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
builder.Services.AddSingleton<ISqlConnectionFactory>(new SqlConnectionFactory(connectionString));

builder.Services.AddScoped<clsCategoryDataAccess>();
builder.Services.AddScoped<clsCategoryBusiness>();
builder.Services.AddScoped<clsItemDataAccess>();
builder.Services.AddScoped<clsItemBusiness>();
builder.Services.AddScoped<clsOrderDataAccess>();
builder.Services.AddScoped<clsOrderBusiness>();
builder.Services.AddScoped<clsUserDataAccess>();
builder.Services.AddScoped<clsUserBusiness>();
builder.Services.AddScoped<clsRefreshTokenDataAccess>();
builder.Services.AddScoped<clsRefreshTokenBusiness>();
builder.Services.AddScoped<clsSettingsDataAccess>();
builder.Services.AddScoped<clsSettingsBusiness>();
builder.Services.AddScoped<clsLogsDataAccess>();
builder.Services.AddScoped<clsLogsBusiness>();
builder.Services.AddScoped<clsReportingDataAccess>();
builder.Services.AddScoped<clsReportingBusiness>();

builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured.");

// User Secrets (Development) always overrides the CHANGE_ME placeholders in
// appsettings.json, so this never fires locally - it's specifically a
// production/staging safety net for the day this gets deployed somewhere,
// catching the single most common deployment mistake (forgetting to set real
// secrets via environment variables or a secrets vault) before it can run with
// a publicly-known, guessable configuration.
if (!builder.Environment.IsDevelopment())
{
    if (connectionString.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(
            "ConnectionStrings:DefaultConnection is still the CHANGE_ME placeholder from appsettings.json - " +
            "set a real value via an environment variable or secrets store before running outside Development.");

    if (jwtKey.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
        throw new InvalidOperationException(
            "Jwt:Key is missing, still the CHANGE_ME placeholder, or too short (needs at least 32 bytes) - " +
            "set a strong random value via an environment variable or secrets store before running outside Development.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

// Secure by default: every endpoint requires a valid token unless explicitly
// marked [AllowAnonymous] (currently just the login endpoint) - so a future
// controller added without thinking about auth is protected automatically
// instead of silently wide open.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = (context, cancellationToken) =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("Rate limit exceeded for {RemoteIp} on {Path}",
            context.HttpContext.Connection.RemoteIpAddress, context.HttpContext.Request.Path);
        return ValueTask.CompletedTask;
    };

    // Baseline for every endpoint - general abuse/DoS protection, not aimed at
    // brute-force specifically (that's the tighter "login" policy below).
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Applied to verify-credentials and refresh-token - the two endpoints that
    // accept a guessable secret without already requiring a valid token. A
    // handful of genuine typos is fine; hundreds of attempts per minute from
    // one IP is a brute-force script.
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Locked down by default (Cors:AllowedOrigins is empty in appsettings.json) -
// the chef tablet is served same-origin from this same API in production, so
// CORS doesn't need to allow anything there. Development.json adds the local
// preview server's origin so the tablet can be built/tested against the API
// before it's deployed alongside it.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
// Registered first so it wraps everything below it - any unhandled exception
// from any endpoint gets caught here instead of crashing raw.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // Tells browsers to remember (for the default 30 days) that this host is
    // HTTPS-only, so even a typed/bookmarked http:// link never gets sent in
    // plain text - HttpsRedirection alone still lets that one first request
    // go out unencrypted before the redirect happens.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Ahead of auth on purpose: a flood of login attempts should be rejected before
// spending CPU on password verification, not after.
app.UseRateLimiter();

app.UseCors("Default");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
