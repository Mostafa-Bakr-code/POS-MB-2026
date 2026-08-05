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

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
