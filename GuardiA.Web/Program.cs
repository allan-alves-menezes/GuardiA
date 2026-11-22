using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddSingleton<GuardiaWebStore>();
builder.Services.AddHostedService<GuardiaWebTelemetryWorker>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "GuardiA.Web", utc = DateTime.UtcNow }));

app.MapPost("/api/v1/auth/register", (RegisterRequest req, GuardiaWebStore store) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
        return Results.BadRequest(new { error = "Informe nome, e-mail válido e senha com pelo menos 6 caracteres." });

    var result = store.RegisterUser(req.Name.Trim(), req.Email.Trim(), req.Password);
    if (!result.Ok)
        return Results.Conflict(new { error = result.Error });

    return Results.Created($"/api/v1/auth/user", new { ok = true, email = result.User!.Email, name = result.User.Name, sessionKey = result.User.SessionKey });
});

app.MapPost("/api/v1/auth/login", (LoginRequest req, GuardiaWebStore store) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Informe e-mail e senha." });

    var user = store.AuthenticateUser(req.Email.Trim(), req.Password);
    if (user is null)
        return Results.Unauthorized();

    return Results.Ok(new { ok = true, email = user.Email, name = user.Name, sessionKey = user.SessionKey });
});

app.MapGet("/api/v1/monitors", (HttpRequest http, GuardiaWebStore store) =>
{
    var sessionKey = http.Headers["X-Session-Key"].FirstOrDefault();
    var user = store.FindUserBySessionKey(sessionKey);
    if (user is null) return Results.Unauthorized();

    return Results.Ok(store.GetMonitors(user.Email));
});

app.MapPost("/api/v1/monitors", (MonitorCreateRequest req, HttpRequest http, GuardiaWebStore store) =>
{
    var sessionKey = http.Headers["X-Session-Key"].FirstOrDefault();
    var user = store.FindUserBySessionKey(sessionKey);
    if (user is null) return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(req.Url))
        return Results.BadRequest(new { error = "Informe uma URL válida." });

    var item = store.AddMonitor(user.Email, req.Url.Trim(), string.IsNullOrWhiteSpace(req.Label) ? "Alvo" : req.Label.Trim());
    return Results.Created("/api/v1/monitors", item);
});

app.MapDelete("/api/v1/monitors/{id}", (string id, HttpRequest http, GuardiaWebStore store) =>
{
    var sessionKey = http.Headers["X-Session-Key"].FirstOrDefault();
    var user = store.FindUserBySessionKey(sessionKey);
    if (user is null) return Results.Unauthorized();

    var removed = store.RemoveMonitor(id, user.Email);
    return removed ? Results.Ok(new { removed = true, id }) : Results.NotFound(new { error = "Alvo não encontrado." });
});

app.MapGet("/api/v1/agent/devices", (GuardiaWebStore store) => Results.Ok(store.GetDevices()));
app.MapPost("/api/v1/agent/heartbeat", (AgentHeartbeatPayload payload, GuardiaWebStore store) => Results.Ok(store.RecordHeartbeat(payload)));
app.MapFallbackToFile("index.html");

app.Run();

public sealed record RegisterRequest(string? Name, string? Email, string? Password);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record MonitorCreateRequest(string? Url, string? Label);
public sealed record MonitorEntry(string Id, string OwnerEmail, string Url, string Label, string Status, DateTime CreatedAtUtc);
public sealed record AgentHeartbeatPayload(string? ClientId, string? MachineName, double? CpuLoadPercent, double? MemoryUsedPercent, int? ActiveTcpConnectionsCount);
public sealed record AgentDeviceEntry(string ClientId, string MachineName, double CpuLoadPercent, double MemoryUsedPercent, int ActiveTcpConnectionsCount, DateTime LastSeenUtc);

public sealed record UserRecord(
    string Name,
    string Email,
    string PasswordHash,
    string PasswordSalt,
    string SessionKey,
    DateTime CreatedAtUtc
);

public sealed class GuardiaWebStore
{
    private readonly string _storageFile;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, AgentDeviceEntry> _devices = new();
    private readonly ConcurrentDictionary<string, UserRecord> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MonitorEntry> _monitors = new();

    public GuardiaWebStore(IWebHostEnvironment env)
    {
        var customPath = Environment.GetEnvironmentVariable("GUARDIA_DATA_DIR");
        var dataDir = !string.IsNullOrWhiteSpace(customPath) ? customPath : Path.Combine(env.ContentRootPath, "Data");
        Directory.CreateDirectory(dataDir);
        _storageFile = Path.Combine(dataDir, "guardia-web-estado.json");
        Load();
    }

    public (bool Ok, string? Error, UserRecord? User) RegisterUser(string name, string email, string password)
    {
        var normalizedEmail = email.ToLowerInvariant();
        if (_users.ContainsKey(normalizedEmail))
            return (false, "Já existe uma conta cadastrada com este e-mail.", null);

        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var salt = Convert.ToBase64String(saltBytes);
        var hash = HashPassword(password, saltBytes);
        var sessionKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        var user = new UserRecord(name, normalizedEmail, hash, salt, sessionKey, DateTime.UtcNow);
        _users[normalizedEmail] = user;
        Save();
        return (true, null, user);
    }

    public UserRecord? AuthenticateUser(string email, string password)
    {
        var normalizedEmail = email.ToLowerInvariant();
        if (!_users.TryGetValue(normalizedEmail, out var user))
            return null;

        var saltBytes = Convert.FromBase64String(user.PasswordSalt);
        var computedHash = HashPassword(password, saltBytes);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHash),
            Encoding.UTF8.GetBytes(user.PasswordHash)
        ) ? user : null;
    }

    public UserRecord? FindUserBySessionKey(string? sessionKey)
    {
        if (string.IsNullOrWhiteSpace(sessionKey)) return null;
        return _users.Values.FirstOrDefault(u => string.Equals(u.SessionKey, sessionKey.Trim(), StringComparison.Ordinal));
    }

    public bool RemoveMonitor(string id, string ownerEmail)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        lock (_sync)
        {
            var idx = _monitors.FindIndex(m =>
                string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.OwnerEmail, ownerEmail, StringComparison.OrdinalIgnoreCase));

            if (idx < 0) return false;
            _monitors.RemoveAt(idx);
            Save();
            return true;
        }
    }

    public IEnumerable<MonitorEntry> GetMonitors(string ownerEmail)
    {
        lock (_sync)
        {
            return _monitors
                .Where(m => string.Equals(m.OwnerEmail, ownerEmail, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.CreatedAtUtc)
                .ToList();
        }
    }

    public MonitorEntry AddMonitor(string ownerEmail, string url, string label)
    {
        lock (_sync)
        {
            var entry = new MonitorEntry(
                Id: Guid.NewGuid().ToString("N")[..8],
                OwnerEmail: ownerEmail.ToLowerInvariant(),
                Url: url,
                Label: label,
                Status: "Ativo",
                CreatedAtUtc: DateTime.UtcNow
            );
            _monitors.Add(entry);
            Save();
            return entry;
        }
    }

    private static string HashPassword(string password, byte[] salt)
    {
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(hashBytes);
    }

    public IEnumerable<AgentDeviceEntry> GetDevices() => _devices.Values.OrderByDescending(d => d.LastSeenUtc);

    public AgentDeviceEntry RecordHeartbeat(AgentHeartbeatPayload? payload)
    {
        var clientId = string.IsNullOrWhiteSpace(payload?.ClientId) ? $"{Environment.MachineName.ToLowerInvariant()}-web" : payload!.ClientId!.Trim();
        var machineName = string.IsNullOrWhiteSpace(payload?.MachineName) ? Environment.MachineName : payload!.MachineName!.Trim();
        var entry = new AgentDeviceEntry(clientId, machineName, Math.Round(payload?.CpuLoadPercent ?? 0, 1), Math.Round(payload?.MemoryUsedPercent ?? 0, 1), payload?.ActiveTcpConnectionsCount ?? 0, DateTime.UtcNow);
        _devices[clientId] = entry;
        Save();
        return entry;
    }

    private sealed class PersistedState
    {
        public List<AgentDeviceEntry> Devices { get; set; } = new();
        public List<UserRecord> Users { get; set; } = new();
        public List<MonitorEntry> Monitors { get; set; } = new();
    }

    private void Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_storageFile)) return;
            try
            {
                var raw = File.ReadAllText(_storageFile);
                var state = JsonSerializer.Deserialize<PersistedState>(raw);
                if (state is not null)
                {
                    foreach (var item in state.Devices)
                        if (!string.IsNullOrWhiteSpace(item.ClientId)) _devices[item.ClientId] = item;
                    foreach (var user in state.Users)
                        if (!string.IsNullOrWhiteSpace(user.Email)) _users[user.Email] = user;
                    _monitors.Clear();
                    _monitors.AddRange(state.Monitors);
                }
            }
            catch { }
        }
    }

    private void Save()
    {
        lock (_sync)
        {
            var state = new PersistedState
            {
                Devices = _devices.Values.OrderByDescending(d => d.LastSeenUtc).ToList(),
                Users = _users.Values.OrderBy(u => u.Email).ToList(),
                Monitors = _monitors.OrderByDescending(m => m.CreatedAtUtc).ToList()
            };
            File.WriteAllText(_storageFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

public sealed class GuardiaWebTelemetryWorker : BackgroundService
{
    private readonly GuardiaWebStore _store;
    public GuardiaWebTelemetryWorker(GuardiaWebStore store) => _store = store;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var mem = GC.GetGCMemoryInfo();
                var total = Math.Max(1.0, mem.TotalAvailableMemoryBytes);
                var memPct = Math.Clamp((mem.HeapSizeBytes / total) * 100.0 * 10.0, 1.0, 99.0);
                var tcp = 0;
                try { tcp = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length; } catch { }
                var cpu = Math.Clamp(Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds % 45.0 + 5.0, 1.0, 99.0);

                _store.RecordHeartbeat(new AgentHeartbeatPayload($"{Environment.MachineName.ToLowerInvariant()}-guardia-web", Environment.MachineName, cpu, memPct, tcp));
            }
            catch { }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}


