using GuardiA.Admin.Components;
using GuardiA.Admin.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configuração flexível do HttpClient (Lê variável de ambiente do Render ou usa localhost como padrão)
var apiBaseUrl = builder.Configuration["API_BASE_URL"] ?? "http://localhost:5005/";
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });

// Registro do Serviço de Alvos
builder.Services.AddScoped<TargetService>();

// 👉 REGISTRO DO WORKER AUTÔNOMO (Roda em segundo plano contínuo)
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