using POS_MB.API;
using POS_MB.Business;
using POS_MB.DataAccess;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<clsSettingsDataAccess>();
builder.Services.AddScoped<clsSettingsBusiness>();
builder.Services.AddScoped<clsLogsDataAccess>();
builder.Services.AddScoped<clsLogsBusiness>();
builder.Services.AddScoped<clsReportingDataAccess>();
builder.Services.AddScoped<clsReportingBusiness>();

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

app.UseCors("Default");

app.UseAuthorization();

app.MapControllers();

app.Run();
