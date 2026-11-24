using System.Net.Http.Json;
using GuardiA.Agent.Services;

namespace GuardiA.Agent;

public sealed class Worker(
    ILogger<Worker> logger,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    SystemTelemetryCollector collector,
    DownloadRiskWatcher downloadWatcher) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var clientId = configuration["AgentConfig:ClientId"] ?? "cliente-padrao";
        var intervalSeconds = configuration.GetValue("AgentConfig:HeartbeatIntervalSeconds", 20);
        if (intervalSeconds < 5) intervalSeconds = 5;

        downloadWatcher.Start();
        logger.LogInformation("GuardiA.Agent iniciado para '{ClientId}' (intervalo: {Interval}s).", clientId, intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = httpClientFactory.CreateClient("GuardiaApi");
                var token = configuration["AgentConfig:AgentToken"];
                if (!string.IsNullOrWhiteSpace(token))
                {
                    client.DefaultRequestHeaders.Remove("X-Guardia-Agent-Token");
                    client.DefaultRequestHeaders.Add("X-Guardia-Agent-Token", token);
                }

                var payload = await collector.CollectAsync(clientId, stoppingToken);
                var response = await client.PostAsJsonAsync("/api/v1/agent/heartbeat", payload, stoppingToken);
                logger.LogInformation("Heartbeat enviado para {Machine} | HTTP: {StatusCode}",
                logger.LogInformation("Heartbeat enviado para {Machine} | HTTP: {StatusCode}",
                    payload.Hostname, (int)response.StatusCode);

                foreach (var ev in downloadWatcher.DrainEvents())
                {
                    var evResp = await client.PostAsJsonAsync("/api/v1/agent/event", ev, stoppingToken);
                    logger.LogWarning("Evento ({File}) | Risco: {HasRisk} | HTTP: {Code}",
                        ev.TargetOrFile, ev.HasRisk, (int)evResp.StatusCode);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Falha ao enviar telemetria do GuardiA.Agent."); }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }
}

