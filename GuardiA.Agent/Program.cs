// Program.cs
using GuardiA.Agent;
using GuardiA.Agent.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "GuardiA.Agent";
});

var rawBaseUrl = builder.Configuration["AgentConfig:ApiBaseUrl"]
    ?? builder.Configuration["API_BASE_URL"]
    ?? "http://localhost:5005/";
var normalizedBaseUrl = rawBaseUrl.EndsWith('/') ? rawBaseUrl : $"{rawBaseUrl}/";

builder.Services.AddHttpClient("GuardiaApi", client =>
{
    client.BaseAddress = new Uri(normalizedBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddSingleton<SystemTelemetryCollector>();
builder.Services.AddSingleton<DownloadRiskWatcher>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
