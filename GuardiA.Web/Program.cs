using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port) && int.TryParse(port, out var parsedPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{parsedPort}");
}

builder.Services.AddSingleton<GuardiaWebStore>();
builder.Services.AddHttpClient("guardia-probe", client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GuardiA-Monitor/1.0");
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "Online", service = "GuardiA.Web", utc = DateTime.UtcNow }));

app.MapPost("/api/v1/auth/register", (AuthRegisterRequest req, GuardiaWebStore store) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "E-mail e senha são obrigatórios." });

    var created = store.Register(req.Name ?? "Usuário", req.Email.Trim(), req.Password);
    if (!created)
        return Results.Conflict(new { error = "Já existe uma conta com este e-mail." });

    return Results.Ok(new { ok = true, message = "Conta criada com sucesso." });
});

app.MapPost("/api/v1/auth/login", (AuthLoginRequest req, GuardiaWebStore store) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "E-mail e senha são obrigatórios." });

    var session = store.Login(req.Email.Trim(), req.Password);
    if (session is null)
        return Results.Unauthorized();

    return Results.Ok(new
    {
        sessionKey = session.Value.SessionKey,
        user = new { name = session.Value.User.Name, email = session.Value.User.Email }
    });
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

    if (string.IsNullOrWhiteSpace(req.Url) || !Uri.TryCreate(req.Url.Trim(), UriKind.Absolute, out var uri) ||
        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
    {
        return Results.BadRequest(new { error = "Informe uma URL válida começando com http:// ou https://." });
    }

    var item = store.AddMonitor(user.Email, uri.ToString(), string.IsNullOrWhiteSpace(req.Label) ? uri.Host : req.Label.Trim());
    return Results.Created("/api/v1/monitors", item);
});

app.MapPost("/api/v1/monitors/{id}/check", async (string id, HttpRequest http, IHttpClientFactory httpClientFactory, GuardiaWebStore store) =>
{
    var sessionKey = http.Headers["X-Session-Key"].FirstOrDefault();
    var user = store.FindUserBySessionKey(sessionKey);
    if (user is null) return Results.Unauthorized();

    var monitor = store.GetMonitor(id, user.Email);
    if (monitor is null) return Results.NotFound(new { error = "Alvo não encontrado." });

    var sw = Stopwatch.StartNew();
    string statusLabel;
    int? statusCode = null;
    string riskSummary = "Sem risco aparente";
    bool hasRisk = false;

    try
    {
        var client = httpClientFactory.CreateClient("guardia-probe");
        using var resp = await client.GetAsync(monitor.Url, HttpCompletionOption.ResponseHeadersRead);
        sw.Stop();

        statusCode = (int)resp.StatusCode;
        var isHttps = monitor.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var finalHost = resp.RequestMessage?.RequestUri?.Host ?? "";
        var origHost = new Uri(monitor.Url).Host;
        var hostChanged = !string.Equals(finalHost, origHost, StringComparison.OrdinalIgnoreCase);

        if (!isHttps)
        {
            hasRisk = true;
            riskSummary = "Conexão sem HTTPS (tráfego não criptografado)";
        }
        else if (hostChanged)
        {
            hasRisk = true;
            riskSummary = $"Redirecionamento externo para {finalHost}";
        }
        else if ((int)resp.StatusCode >= 400)
        {
            hasRisk = true;
            riskSummary = $"Resposta HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase})";
        }

        statusLabel = resp.IsSuccessStatusCode ? $"Online ({statusCode})" : $"Alerta ({statusCode})";
    }
    catch (Exception ex)
    {
        sw.Stop();
        hasRisk = true;
        statusLabel = "Falha / Offline";
        riskSummary = $"Falha ao acessar URL: {ex.Message}";
    }

    var updated = store.RecordCheck(id, user.Email, statusLabel, statusCode, sw.ElapsedMilliseconds, hasRisk, riskSummary);
    return Results.Ok(new
    {
        id = updated?.Id ?? id,
        status = statusLabel,
        lastStatus = statusLabel,
        statusCode,
        latencyMs = sw.ElapsedMilliseconds,
        hasRisk,
        riskSummary,
        monitor = updated
    });
});

app.MapDelete("/api/v1/monitors/{id}", (string id, HttpRequest http, GuardiaWebStore store) =>
{
    var sessionKey = http.Headers["X-Session-Key"].FirstOrDefault();
    var user = store.FindUserBySessionKey(sessionKey);
    if (user is null) return Results.Unauthorized();

    var removed = store.RemoveMonitor(id, user.Email);
    return removed ? Results.Ok(new { removed = true, id }) : Results.NotFound(new { error = "Alvo não encontrado." });
});

app.MapGet("/api/v1/monitors/history", (HttpRequest http, GuardiaWebStore store) =>
{
    var sessionKey = http.Headers["X-Session-Key"].FirstOrDefault();
    var user = store.FindUserBySessionKey(sessionKey);
    if (user is null) return Results.Unauthorized();

    return Results.Ok(store.GetHistory(user.Email));
});

app.MapGet("/api/v1/agent/devices", (GuardiaWebStore store) => Results.Ok(store.GetDevices()));
app.MapPost("/api/v1/agent/heartbeat", (AgentHeartbeatPayload payload, GuardiaWebStore store) => Results.Ok(store.RecordHeartbeat(payload)));

app.Run();

record AuthRegisterRequest(string? Name, string Email, string Password);
record AuthLoginRequest(string Email, string Password);
record MonitorCreateRequest(string Url, string? Label);
record AgentHeartbeatPayload(string? DeviceId, string? Hostname, string? Os, string? Status);

record UserRecord(string Name, string Email, string PasswordHash, string SaltBase64, string SessionKey);

record MonitorEntry(
    string Id,
    string OwnerEmail,
    string Url,
    string Label,
    string LastStatus,
    int? LastStatusCode,
    long? LastLatencyMs,
    bool HasRisk,
    string? RiskSummary,
    DateTime? LastCheckedAtUtc,
    DateTime CreatedAtUtc
);

record MonitorHistoryEntry(
    string Id,
    string OwnerEmail,
    string MonitorId,
    string Url,
    string Label,
    string Status,
    int? StatusCode,
    long LatencyMs,
    bool HasRisk,
    string? RiskSummary,
    DateTime CheckedAtUtc
);

record AgentDeviceEntry(string DeviceId, string Hostname, string Os, string Status, DateTime LastSeenUtc);

sealed class GuardiaWebStore
{
    private readonly object _sync = new();
    private readonly string _storageFile;
    private readonly ConcurrentDictionary<string, AgentDeviceEntry> _devices = new();
    private readonly ConcurrentDictionary<string, UserRecord> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MonitorEntry> _monitors = new();
    private readonly List<MonitorHistoryEntry> _history = new();

    public GuardiaWebStore(IWebHostEnvironment env)
    {
        var customPath = Environment.GetEnvironmentVariable("GUARDIA_DATA_DIR");
        var dataDir = !string.IsNullOrWhiteSpace(customPath)
            ? customPath
            : Path.Combine(env.ContentRootPath, "Data");

        Directory.CreateDirectory(dataDir);
        _storageFile = Path.Combine(dataDir, "guardia-web-state.json");
        Load();
    }

    public bool Register(string name, string email, string password)
    {
        lock (_sync)
        {
            if (_users.ContainsKey(email)) return false;

            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32));
            var sessionKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

            _users[email] = new UserRecord(name, email, hash, Convert.ToBase64String(salt), sessionKey);
            Save();
            return true;
        }
    }

    public (UserRecord User, string SessionKey)? Login(string email, string password)
    {
        lock (_sync)
        {
            if (!_users.TryGetValue(email, out var user)) return null;

            var salt = Convert.FromBase64String(user.SaltBase64);
            var candidate = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32));
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(user.PasswordHash), Encoding.UTF8.GetBytes(candidate)))
                return null;

            var newSession = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var updated = user with { SessionKey = newSession };
            _users[email] = updated;
            Save();
            return (updated, newSession);
        }
    }

    public UserRecord? FindUserBySessionKey(string? sessionKey)
    {
        if (string.IsNullOrWhiteSpace(sessionKey)) return null;
        lock (_sync)
        {
            return _users.Values.FirstOrDefault(u => string.Equals(u.SessionKey, sessionKey, StringComparison.Ordinal));
        }
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

    public MonitorEntry? GetMonitor(string id, string ownerEmail)
    {
        lock (_sync)
        {
            return _monitors.FirstOrDefault(m =>
                string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.OwnerEmail, ownerEmail, StringComparison.OrdinalIgnoreCase));
        }
    }

    public MonitorEntry AddMonitor(string ownerEmail, string url, string label)
    {
        lock (_sync)
        {
            var entry = new MonitorEntry(
                Guid.NewGuid().ToString("N")[..12],
                ownerEmail,
                url,
                label,
                "Pendente",
                null,
                null,
                false,
                null,
                null,
                DateTime.UtcNow
            );
            _monitors.Add(entry);
            Save();
            return entry;
        }
    }

    public MonitorEntry? RecordCheck(string id, string ownerEmail, string status, int? statusCode, long latencyMs, bool hasRisk, string? riskSummary)
    {
        lock (_sync)
        {
            var idx = _monitors.FindIndex(m =>
                string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.OwnerEmail, ownerEmail, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return null;

            var now = DateTime.UtcNow;
            var current = _monitors[idx];
            var updated = current with
            {
                LastStatus = status,
                LastStatusCode = statusCode,
                LastLatencyMs = latencyMs,
                HasRisk = hasRisk,
                RiskSummary = riskSummary,
                LastCheckedAtUtc = now
            };
            _monitors[idx] = updated;

            _history.Insert(0, new MonitorHistoryEntry(
                Guid.NewGuid().ToString("N")[..12],
                ownerEmail,
                current.Id,
                current.Url,
                current.Label,
                status,
                statusCode,
                latencyMs,
                hasRisk,
                riskSummary,
                now
            ));

            if (_history.Count > 200)
                _history.RemoveRange(200, _history.Count - 200);

            Save();
            return updated;
        }
    }

    public IEnumerable<MonitorHistoryEntry> GetHistory(string ownerEmail)
    {
        lock (_sync)
        {
            return _history
                .Where(h => string.Equals(h.OwnerEmail, ownerEmail, StringComparison.OrdinalIgnoreCase))
                .Take(100)
                .ToList();
        }
    }

    public IEnumerable<AgentDeviceEntry> GetDevices() => _devices.Values.OrderByDescending(d => d.LastSeenUtc).ToList();

    public AgentDeviceEntry RecordHeartbeat(AgentHeartbeatPayload payload)
    {
        var id = string.IsNullOrWhiteSpace(payload.DeviceId) ? Guid.NewGuid().ToString("N")[..10] : payload.DeviceId.Trim();
        var entry = new AgentDeviceEntry(
            id,
            string.IsNullOrWhiteSpace(payload.Hostname) ? "endpoint-unknown" : payload.Hostname.Trim(),
            string.IsNullOrWhiteSpace(payload.Os) ? "Unknown OS" : payload.Os.Trim(),
            string.IsNullOrWhiteSpace(payload.Status) ? "Online" : payload.Status.Trim(),
            DateTime.UtcNow
        );
        _devices[id] = entry;
        lock (_sync) { Save(); }
        return entry;
    }

    private sealed class PersistedState
    {
        public List<AgentDeviceEntry> Devices { get; set; } = new();
        public List<UserRecord> Users { get; set; } = new();
        public List<MonitorEntry> Monitors { get; set; } = new();
        public List<MonitorHistoryEntry> History { get; set; } = new();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_storageFile)) return;
            var json = File.ReadAllText(_storageFile);
            var state = JsonSerializer.Deserialize<PersistedState>(json);
            if (state is null) return;

            foreach (var dev in state.Devices)
                if (!string.IsNullOrWhiteSpace(dev.DeviceId)) _devices[dev.DeviceId] = dev;

            foreach (var user in state.Users)
                if (!string.IsNullOrWhiteSpace(user.Email)) _users[user.Email] = user;

            _monitors.Clear();
            _monitors.AddRange(state.Monitors);

            _history.Clear();
            _history.AddRange(state.History);
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            var state = new PersistedState
            {
                Devices = _devices.Values.OrderByDescending(d => d.LastSeenUtc).ToList(),
                Users = _users.Values.OrderBy(u => u.Email).ToList(),
                Monitors = _monitors.OrderByDescending(m => m.CreatedAtUtc).ToList(),
                History = _history.Take(200).ToList()
            };
            File.WriteAllText(_storageFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
