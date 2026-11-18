using GuardiA.Admin.Models;
using GuardiA.Admin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GuardiA.Admin.Services;

public class GuardiASentinelWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GuardiASentinelWorker> _logger;
    
    // Define o intervalo de execução autônoma (a cada 15 minutos)
    private readonly TimeSpan _intervalo = TimeSpan.FromMinutes(15);

    public GuardiASentinelWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<GuardiASentinelWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🛡️ GuardiA Sentinel Autônomo iniciado e operando em segundo plano.");

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        using var timer = new PeriodicTimer(_intervalo);

        do
        {
            try
            {
                await ExecutarCicloAutonomoAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Falha durante o ciclo de monitoramento autônomo do GuardiA.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));

        _logger.LogInformation("🛑 GuardiA Sentinel Autônomo encerrado.");
    }

    private async Task ExecutarCicloAutonomoAsync(CancellationToken ct)
    {
        _logger.LogInformation("🔍 [Ciclo Autônomo] Iniciando varredura de alvos às {Hora}", DateTimeOffset.Now);

        using var scope = _scopeFactory.CreateScope();
        var targetService = scope.ServiceProvider.GetRequiredService<TargetService>();

        try
        {
            var alvosMonitorados = await targetService.GetTargetsAsync();

            if (alvosMonitorados == null || !alvosMonitorados.Any())
            {
                _logger.LogInformation("ℹ️ [Ciclo Autônomo] Nenhum alvo cadastrado para varredura no momento.");
                return;
            }

            _logger.LogInformation("📊 [Ciclo Autônomo] Total de {Total} alvos encontrados para verificação.", alvosMonitorados.Count);

            foreach (var alvo in alvosMonitorados)
            {
                if (ct.IsCancellationRequested) break;

                _logger.LogInformation("🔎 Analisando alvo ID: {Id} ({Url})", alvo.Id, alvo.Url);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao processar os alvos durante a varredura autônoma.");
        }

        _logger.LogInformation("✅ [Ciclo Autônomo] Varredura concluída com sucesso.");
    }
}