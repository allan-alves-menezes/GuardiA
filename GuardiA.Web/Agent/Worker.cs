// Worker.cs
using System.Net.Http;
using System.Net.Http.Json;
using GuardiA.Agent.Services;

namespace GuardiA.Agent;

public sealed class Worker(
    ILogger<Worker> logger,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    SystemTelemetryCollector collector) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var clientId = configuration["AgentConfig:ClientId"] ?? "cliente-padrao";
        var intervalSeconds = configuration.GetValue("AgentConfig:HeartbeatIntervalSeconds", 30);
        if (intervalSeconds < 5) intervalSeconds = 5;

        logger.LogInformation("GuardiA.Agent iniciado para o cliente '{ClientId}' (intervalo: {Interval}s).", clientId, intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var payload = await collector.CollectAsync(clientId, stoppingToken);
                var client = httpClientFactory.CreateClient("GuardiaApi");

                var token = configuration["AgentConfig:AgentToken"];
                if (!string.IsNullOrWhiteSpace(token))
                {
                    client.DefaultRequestHeaders.Remove("X-Guardia-Agent-Token");
                    client.DefaultRequestHeaders.Add("X-Guardia-Agent-Token", token);
                }

                var response = await client.PostAsJsonAsync("/api/v1/agent/heartbeat", payload, stoppingToken);
                logger.LogInformation("Heartbeat enviado para {Machine} | Status HTTP: {StatusCode}",
                    payload.MachineName, (int)response.StatusCode);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao coletar ou enviar heartbeat do GuardiA.Agent.");
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }
}