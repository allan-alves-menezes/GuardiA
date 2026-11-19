using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GuardiA.Admin.Services;

public class GuardiASentinelWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GuardiaTelemetryService _telemetry;
    private readonly ILogger<GuardiASentinelWorker> _logger;
    private readonly TimeSpan _intervalo = TimeSpan.FromMinutes(15);

    public GuardiASentinelWorker(
        IServiceScopeFactory scopeFactory,
        GuardiaTelemetryService telemetry,
        ILogger<GuardiASentinelWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _telemetry = telemetry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GuardiA Sentinel Autônomo iniciado e operando em segundo plano.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var cicloInicio = DateTimeOffset.Now;
            _logger.LogInformation("[Ciclo Autônomo] Iniciando varredura de alvos às {Hora}", cicloInicio);

            try
            {
                await _telemetry.CheckApiHealthAsync(stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var targetService = scope.ServiceProvider.GetRequiredService<TargetService>();

                var targets = await targetService.GetTargetsAsync(stoppingToken);
                var totalTargets = targets?.Count ?? 0;

                if (totalTargets == 0)
                {
                    _logger.LogInformation("[Ciclo Autônomo] Nenhum alvo cadastrado para varredura no momento.");
                    _telemetry.UpdateScore(0, 0);
                }
                else
                {
                    var alertasAltosOuCriticos = targets!.Count(t =>
                        !t.Active ||
                        t.SslDays <= 15 ||
                        t.StatusCode >= 500 ||
                        string.Equals(t.LastStatus, "Critical", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(t.LastStatus, "High", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(t.LastStatus, "Crítico", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(t.LastStatus, "Alto", StringComparison.OrdinalIgnoreCase));

                    _telemetry.UpdateScore(totalTargets, alertasAltosOuCriticos);
                    _logger.LogInformation(
                        "[Ciclo Autônomo] {Total} alvo(s) avaliado(s) ({Alertas} alto(s)/crítico(s)).",
                        totalTargets,
                        alertasAltosOuCriticos);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Erro durante o ciclo autônomo do Sentinel.");
                _telemetry.UpdateScore(0, 0);
            }

            _telemetry.RecordScanCycle(cicloInicio, _intervalo);

            try
            {
                await Task.Delay(_intervalo, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}