using Microsoft.AspNetCore.SignalR;
using OptionTradesParser.Dashboard.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

string databasePath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "trade_alerts.db"));
string configPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "AppConfig.json"));
builder.Services.AddSingleton(new DashboardRepository($"Data Source={databasePath}"));
builder.Services.AddSingleton(AccountConfiguration.Load(configPath));
builder.Services.AddHostedService<DashboardChangeNotifier>();

var app = builder.Build();
app.UseCors();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapGet("/api/dashboard", (DashboardRepository repository, AccountConfiguration account, int? year, int? month) =>
    Results.Ok(repository.GetDashboard(account.AccountType, year ?? DateTimeOffset.UtcNow.Year, month ?? DateTimeOffset.UtcNow.Month)));
app.MapGet("/api/trades", (DashboardRepository repository, int? days, string? date, string? startDate, string? endDate) =>
    Results.Ok(!string.IsNullOrWhiteSpace(startDate) && !string.IsNullOrWhiteSpace(endDate)
        ? repository.GetTradesForRange(startDate, endDate)
        : string.IsNullOrWhiteSpace(date)
            ? repository.GetTrades(Math.Clamp(days ?? 30, 1, 365))
            : repository.GetTradesForDate(date)));
app.MapGet("/api/calendar", (DashboardRepository repository, int year, int month) =>
    Results.Ok(repository.GetCalendar(year, month)));
app.MapGet("/api/settings/selected-trade-date", (DashboardRepository repository) =>
    Results.Ok(new SelectedTradeDateResponse(repository.GetSelectedTradeDate())));
app.MapPost("/api/settings/selected-trade-date", (SelectedTradeDateRequest request, DashboardRepository repository) =>
{
    repository.SetSelectedTradeDate(request.Date);
    return Results.Ok(new SelectedTradeDateResponse(request.Date));
});
app.MapGet("/api/trades/{id:long}", (long id, DashboardRepository repository) =>
    repository.GetTrade(id) is { } trade ? Results.Ok(trade) : Results.NotFound());
app.MapGet("/api/account", (DashboardRepository repository, AccountConfiguration account) =>
    Results.Ok(repository.GetAccount(account.AccountType)));
app.MapGet("/api/approvals/pending", (DashboardRepository repository) => Results.Ok(repository.GetPendingApprovals()));
app.MapPost("/api/approvals/{id:long}/approve", (long id, ApprovalRequest request, DashboardRepository repository) =>
{
    ApprovalActionResult result = repository.Approve(id, request.Budget);
    return result.Success ? Results.Ok(result) : Results.Conflict(result);
});
app.MapPost("/api/approvals/{id:long}/reject", (long id, DashboardRepository repository) =>
{
    ApprovalActionResult result = repository.Reject(id);
    return result.Success ? Results.Ok(result) : Results.Conflict(result);
});
app.MapHub<TradeHub>("/hubs/trades");

app.Run("http://localhost:5086");
