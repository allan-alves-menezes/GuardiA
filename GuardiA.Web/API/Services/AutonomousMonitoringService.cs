using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GuardiA.API.Services;

public class AutonomousMonitoringService : BackgroundService
{
    private readonly ILogger<AutonomousMonitoringService> _logger;

    public AutonomousMonitoringService(ILogger<AutonomousMonitoringService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Serviço de monitoramento autônomo iniciado.");
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}