using GuardiA.Admin.Components;
using GuardiA.Admin.Services;

var builder = WebApplication.CreateBuilder(args);

var apiBaseUrl = builder.Configuration["GuardiaApi:BaseUrl"];
if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    apiBaseUrl = "http://localhost:8080/";
}
else if (!apiBaseUrl.EndsWith("/"))
{
    apiBaseUrl += "/";
}

var baseUri = new Uri(apiBaseUrl);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient("GuardiaApi", client =>
{
    client.BaseAddress = baseUri;
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddTransient(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var client = factory.CreateClient("GuardiaApi");
    client.BaseAddress ??= baseUri;
    return client;
});

builder.Services.AddSingleton<GuardiaTelemetryService>();
builder.Services.AddScoped<TargetService>();
builder.Services.AddHostedService<GuardiASentinelWorker>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();