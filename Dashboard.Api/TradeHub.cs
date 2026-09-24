using Microsoft.AspNetCore.SignalR;

namespace OptionTradesParser.Dashboard.Api;

public sealed class TradeHub : Hub;

public sealed class DashboardChangeNotifier(
    DashboardRepository repository,
    IHubContext<TradeHub> hubContext) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long previousVersion = -1;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            long currentVersion = repository.GetDataVersion();
            if (previousVersion >= 0 && currentVersion != previousVersion)
                await hubContext.Clients.All.SendAsync("tradesChanged", cancellationToken: stoppingToken);
            previousVersion = currentVersion;
        }
    }
}
