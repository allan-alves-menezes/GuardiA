using GuardiA.Admin.Components;
using GuardiA.Admin.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configuração flexível da URL base da API (Render / variáveis de ambiente ou localhost)
var rawBaseUrl = builder.Configuration["API_BASE_URL"]
    ?? builder.Configuration["GuardiaApi:BaseUrl"]
    ?? "http://localhost:5005/";

var normalizedBaseUrl = rawBaseUrl.EndsWith('/') ? rawBaseUrl : $"{rawBaseUrl}/";

builder.Services.AddHttpClient("GuardiaApi", client =>
{
    client.BaseAddress = new Uri(normalizedBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("GuardiaApi");
});

// Serviços da aplicação
builder.Services.AddScoped<TargetService>();
builder.Services.AddSingleton<GuardiaTelemetryService>();

// Worker autônomo em segundo plano
builder.Services.AddHostedService<GuardiASentinelWorker>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();