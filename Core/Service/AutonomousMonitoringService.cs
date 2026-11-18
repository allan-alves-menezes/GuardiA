using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration; // Necessário para ler o appsettings.json
using System.Net.Http.Json;

namespace GuardiAProjeto.Services;

public class AutonomousMonitoringService : BackgroundService
{
    private readonly ILogger<AutonomousMonitoringService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(2);

    public AutonomousMonitoringService(
        ILogger<AutonomousMonitoringService> logger,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Lê o papel configurado no appsettings.json (Padrão é "Server" se não encontrar)
        var role = _configuration["GuardiaConfig:Role"] ?? "Server";
        _logger.LogInformation("AutonomousMonitoringService iniciado no modo: {Role}", role);

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (role.Equals("Agent", StringComparison.OrdinalIgnoreCase))
                {
                    // Comportamento de AGENTE: Coleta dados locais e envia para a Central
                    await SendAgentTelemetryAsync(stoppingToken);
                }
                else
                {
                    // Comportamento de SERVIDOR: Executa a checagem padrão dos alvos web
                    _logger.LogInformation("Iniciando ciclo de verificação de saúde dos alvos (Servidor)...");
                    await CheckTargetsAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ocorreu um erro crítico durante o ciclo de monitoramento.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    // --- MODO SERVIDOR (Código que você já tinha) ---
    private async Task CheckTargetsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GuardiaDbContext>();
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        var targets = await dbContext.MonitoredTargets
            .Where(t => t.IsActive)
            .ToListAsync(stoppingToken);

        if (!targets.Any())
        {
            _logger.LogInformation("Nenhum alvo ativo encontrado para monitoramento.");
            return;
        }

        foreach (var target in targets)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                _logger.LogInformation("Testando alvo: {Name} ({Url})", target.Name, target.Url);

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var response = await client.GetAsync(target.Url, stoppingToken);
                stopwatch.Stop();

                target.LastStatus = response.IsSuccessStatusCode ? "Online" : "Degraded/Error";
                target.StatusCode = (int)response.StatusCode;
                target.LastCheckedAt = DateTime.UtcNow;

                _logger.LogInformation("Alvo {Name} respondeu com status {Status} em {ElapsedMs}ms", 
                    target.Name, target.StatusCode, stopwatch.ElapsedMilliseconds);
            }
            catch (HttpRequestException ex)
            {
                target.LastStatus = "Offline (Network Error)";
                target.StatusCode = null;
                target.LastCheckedAt = DateTime.UtcNow;

                _logger.LogWarning("Falha de rede ao contatar o alvo {Name}: {Message}", target.Name, ex.Message);
            }
            catch (TaskCanceledException)
            {
                target.LastStatus = "Timeout";
                target.StatusCode = null;
                target.LastCheckedAt = DateTime.UtcNow;

                _logger.LogWarning("Timeout ao tentar acessar o alvo {Name}", target.Name);
            }
        }

        await dbContext.SaveChangesAsync(stoppingToken);
        _logger.LogInformation("Ciclo de verificação concluído e resultados salvos no banco.");
    }

    // --- MODO AGENTE (Novo comportamento para o computador remoto) ---
    private async Task SendAgentTelemetryAsync(CancellationToken stoppingToken)
    {
        var centralUrl = _configuration["GuardiaConfig:CentralApiUrl"];
        if (string.IsNullOrEmpty(centralUrl))
        {
            _logger.LogWarning("CentralApiUrl não está configurada no appsettings.json do Agente.");
            return;
        }

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        // Monta o pacote de telemetria da máquina local
        var telemetryPayload = new
        {
            HostName = Environment.MachineName,
            Timestamp = DateTime.UtcNow,
            Status = "Healthy",
            AvailableMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024)
        };

        try
        {
            _logger.LogInformation("Enviando telemetria da máquina {HostName} para a central: {Url}", telemetryPayload.HostName, centralUrl);

            var response = await client.PostAsJsonAsync(centralUrl, telemetryPayload, stoppingToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Telemetria enviada com sucesso para o Servidor Central.");
            }
            else
            {
                _logger.LogWarning("O Servidor Central recusou a telemetria. Status code: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro de conexão ao tentar enviar telemetria para o Servidor Central ({Url}).", centralUrl);
        }
    }
}