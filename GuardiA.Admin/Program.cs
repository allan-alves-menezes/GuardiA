using GuardiA.Admin.Components;
using GuardiA.Admin.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configuração do HttpClient apontando para o Backend API (porta 5005)
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("http://localhost:5005/") });

// Registro do Serviço de Alvos
builder.Services.AddScoped<TargetService>();

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