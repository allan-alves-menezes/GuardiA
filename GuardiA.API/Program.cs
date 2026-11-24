using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GuardiA.API;
using GuardiA.API.Data;
using GuardiA.API.Endpoints;
using GuardiA.API.Models;
using GuardiA.API.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Scalar.AspNetCore;

Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

// Garante que os arquivos estáticos de wwwroot (como index.html) estejam disponíveis no diretório de execução
var sourceWwwRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"));
var targetWwwRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
if (Directory.Exists(sourceWwwRoot) && !string.Equals(sourceWwwRoot, targetWwwRoot, StringComparison.OrdinalIgnoreCase))
{
    Directory.CreateDirectory(targetWwwRoot);
    foreach (var file in Directory.GetFiles(sourceWwwRoot))
    {
        var destFile = Path.Combine(targetWwwRoot, Path.GetFileName(file));
        if (!File.Exists(destFile) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(destFile))
        {
            File.Copy(file, destFile, overwrite: true);
        }
    }
}

string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "guardia-.txt");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 15,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .CreateLogger();

try
{
    Log.Information("Iniciando o serviço GuardIA API...");

    var builder = WebApplication.CreateBuilder(args);

    // Permite configurar o caminho do banco via appsettings/env (ex.: Database__Path=/var/data/guardia_vault.db na nuvem)
    string configuredDbPath = builder.Configuration["Database:Path"]
        ?? Environment.GetEnvironmentVariable("GUARDIA_DB_PATH")
        ?? Path.Combine(AppContext.BaseDirectory, "guardia_vault.db");

    string dbDir = Path.GetDirectoryName(configuredDbPath);
    if (!string.IsNullOrWhiteSpace(dbDir) && !Directory.Exists(dbDir))
    {
        Directory.CreateDirectory(dbDir);
    }

    string dbPath = configuredDbPath;
    string sqliteConnectionString = $"Data Source={dbPath}";
    ProgramHelpers.InitDatabase(dbPath);

    // Caminho raiz para o armazenamento segregado (Clients/clients.json e Credentials/passwords.json)
    string storageRoot = builder.Configuration["Storage:Path"]
        ?? Environment.GetEnvironmentVariable("GUARDIA_STORAGE_PATH")
        ?? Path.Combine(string.IsNullOrWhiteSpace(dbDir) ? AppContext.BaseDirectory : dbDir, "Storage");

    var credentialStore = new ClientCredentialStore(storageRoot);

    // Garante que as tabelas e colunas esperadas existam no SQLite
    using (var connection = new SqliteConnection(sqliteConnectionString))
    {
        connection.Open();

        var createTargetsCmd = connection.CreateCommand();
        createTargetsCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS MonitoredTargets (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Url TEXT NOT NULL,
                TargetBrand TEXT NOT NULL,
                HttpStatus INTEGER DEFAULT 0,
                LastStatus TEXT,
                IsActive INTEGER DEFAULT 1,
                LastChecked TEXT,
                UserId INTEGER
            );
        ";
        createTargetsCmd.ExecuteNonQuery();

        await ProgramAuthHelpers.EnsureColumnExistsAsync(connection, "MonitoredTargets", "HttpStatus", "INTEGER DEFAULT 0");
        await ProgramAuthHelpers.EnsureColumnExistsAsync(connection, "MonitoredTargets", "LastStatus", "TEXT");
        await ProgramAuthHelpers.EnsureColumnExistsAsync(connection, "MonitoredTargets", "IsActive", "INTEGER DEFAULT 1");
        await ProgramAuthHelpers.EnsureColumnExistsAsync(connection, "MonitoredTargets", "LastChecked", "TEXT");
        await ProgramAuthHelpers.EnsureColumnExistsAsync(connection, "MonitoredTargets", "UserId", "INTEGER");

        var createUsersCmd = connection.CreateCommand();
        createUsersCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Email TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                ApiKey TEXT NOT NULL UNIQUE,
                CreatedAt TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1
            );
        ";
        createUsersCmd.ExecuteNonQuery();

        var createAgentsCmd = connection.CreateCommand();
        createAgentsCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS MonitoredAgents (
                DeviceKey TEXT PRIMARY KEY,
                ClientId TEXT NOT NULL,
                MachineName TEXT NOT NULL,
                OsDescription TEXT,
                AgentVersion TEXT,
                LastSeenUtc TEXT NOT NULL,
                CpuLoadPercent REAL DEFAULT 0,
                MemoryUsedPercent REAL DEFAULT 0,
                ActiveTcpConnectionsCount INTEGER DEFAULT 0,
                WindowsFirewallDomainEnabled INTEGER,
                WindowsFirewallPrivateEnabled INTEGER,
                WindowsFirewallPublicEnabled INTEGER,
                DefenderRealTimeStatus TEXT
            );
        ";
        createAgentsCmd.ExecuteNonQuery();
    }

    builder.Services.AddDbContext<GuardiaDbContext>(options =>
        options.UseSqlite(sqliteConnectionString));

    builder.Services.AddSingleton(credentialStore);
    builder.Services.AddSingleton(new AgentTelemetryRepository(dbPath));
    builder.Services.AddHostedService<AutonomousMonitoringService>();

    if (OperatingSystem.IsWindows())
    {
        builder.Host.UseWindowsService();
    }

    builder.Host.UseSerilog();

    // Respeita PORT do Render / ASPNETCORE_URLS do Docker, com fallback para 8080
    string? portEnv = Environment.GetEnvironmentVariable("PORT");
    string? aspnetUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

    if (string.IsNullOrWhiteSpace(aspnetUrls))
    {
        int port = int.TryParse(portEnv, out var parsedPort) ? parsedPort : 8080;
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
    }

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod());
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApi();
    builder.Services.AddControllers();
    builder.Services.AddHttpClient();
    builder.Services.AddHttpContextAccessor();

    var app = builder.Build();

    app.UseCors();
    app.UseDefaultFiles();
    app.UseStaticFiles();

    // ==========================================
    // MIDDLEWARE DE SEGURANÇA: Restrição de IP em /api/v1/vault
    // ==========================================
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/v1/vault", StringComparison.OrdinalIgnoreCase))
        {
            var remoteIp = context.Connection.RemoteIpAddress?.ToString();

            if (remoteIp == "::1") remoteIp = "127.0.0.1";

            bool ipAutorizado = false;
            try
            {
                using var connection = new SqliteConnection(sqliteConnectionString);
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM AdminUsers WHERE AssociatedIp = @ip;";
                cmd.Parameters.AddWithValue("@ip", (object?)remoteIp ?? DBNull.Value);

                long count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
                if (count > 0 || remoteIp == "127.0.0.1")
                {
                    ipAutorizado = true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Erro ao validar restrição de IP no middleware.");
            }

            if (!ipAutorizado)
            {
                Log.Warning("Acesso negado para o IP {Ip} na rota protegida {Path}", remoteIp, path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    erro = $"Acesso negado. O IP da sua máquina ({remoteIp}) não está vinculado à ficha de nenhum colaborador autorizado."
                });
                return;
            }
        }

        await next();
    });

    app.MapOpenApi();
    app.MapScalarApiReference();
    app.MapControllers();

    const string ExpectedApiKey = "guardia-secret-key-2026";

    // ==========================================
    // HEALTH CHECK E AUTOATENDIMENTO (AUTH)
    // ==========================================
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "Healthy",
        service = "GuardiA.API",
        timestampUtc = DateTimeOffset.UtcNow
    }))
    .WithName("HealthCheck")
    .WithTags("Saúde");

    app.MapPost("/api/v1/auth/register", async (
        RegisterUserRequest request,
        ClientCredentialStore store,
        CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { erro = "Nome, e-mail e senha são obrigatórios." });
        }

        if (request.Password.Length < 6)
        {
            return Results.BadRequest(new { erro = "A senha deve ter pelo menos 6 caracteres." });
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (!ProgramAuthHelpers.IsValidEmail(email))
        {
            return Results.BadRequest(new { erro = "Informe um endereço de e-mail válido." });
        }

        var existingInStore = await store.GetClientByEmailAsync(email, ct);
        if (existingInStore is not null)
        {
            return Results.Conflict(new { erro = "Já existe uma conta cadastrada com este e-mail." });
        }

        var name = request.Name.Trim();
        var passwordHash = ProgramAuthHelpers.HashPassword(request.Password);
        var apiKey = ProgramAuthHelpers.GenerateApiKey();
        var createdAt = DateTime.UtcNow.ToString("o");

        try
        {
            using var connection = new SqliteConnection(sqliteConnectionString);
            await connection.OpenAsync(ct);

            var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = "SELECT Id FROM Users WHERE lower(Email) = $email LIMIT 1;";
            checkCmd.Parameters.AddWithValue("$email", email);
            var existing = await checkCmd.ExecuteScalarAsync(ct);
            if (existing is not null)
            {
                return Results.Conflict(new { erro = "Já existe uma conta cadastrada com este e-mail." });
            }

            // Mantém o registro de referência no SQLite sem gravar o hash real da senha (que fica apenas em Credentials/passwords.json)
            var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO Users (Name, Email, PasswordHash, ApiKey, CreatedAt, IsActive)
                VALUES ($name, $email, '[SEGREGATED]', $apiKey, $createdAt, 1);
                SELECT last_insert_rowid();";
            insertCmd.Parameters.AddWithValue("$name", name);
            insertCmd.Parameters.AddWithValue("$email", email);
            insertCmd.Parameters.AddWithValue("$apiKey", apiKey);
            insertCmd.Parameters.AddWithValue("$createdAt", createdAt);

            var userId = Convert.ToInt64(await insertCmd.ExecuteScalarAsync(ct));

            var clientRecord = new ClientAccountRecord(
                UserId: userId,
                Name: name,
                Email: email,
                ApiKey: apiKey,
                CreatedAt: createdAt,
                IsActive: true
            );

            await store.SaveClientAndPasswordAsync(clientRecord, passwordHash, ct);

            Log.Information("Nova conta registrada com armazenamento segregado para {Email} (UserId={UserId})", email, userId);

            return Results.Created("/api/v1/auth/me", new
            {
                userId,
                name,
                email,
                apiKey,
                createdAt
            });
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Results.Conflict(new { erro = "Já existe uma conta cadastrada com este e-mail." });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erro ao registrar usuário {Email}", email);
            return Results.Problem("Não foi possível concluir o cadastro no momento. Tente novamente.");
        }
    })
    .WithName("RegistrarUsuario")
    .WithTags("Autenticação");

    app.MapPost("/api/v1/auth/login", async (
        LoginUserRequest request,
        ClientCredentialStore store,
        CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { erro = "E-mail e senha são obrigatórios." });
        }

        var email = request.Email.Trim().ToLowerInvariant();

        // 1. Consulta os dados cadastrais do cliente em Storage/Clients/clients.json
        var client = await store.GetClientByEmailAsync(email, ct);
        if (client is null || !client.IsActive)
        {
            return Results.Unauthorized();
        }

        // 2. Cruza o UserId para buscar o hash PBKDF2 em Storage/Credentials/passwords.json
        var passwordHash = await store.GetPasswordHashByUserIdAsync(client.UserId, ct);
        if (string.IsNullOrWhiteSpace(passwordHash) ||
            !ProgramAuthHelpers.VerifyPassword(request.Password, passwordHash))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new
        {
            userId = client.UserId,
            name = client.Name,
            email = client.Email,
            apiKey = client.ApiKey,
            createdAt = client.CreatedAt
        });
    })
    .WithName("LoginUsuario")
    .WithTags("Autenticação");

    app.MapGet("/api/v1/auth/me", async (HttpContext context, ClientCredentialStore store) =>
    {
        var user = await ProgramAuthHelpers.ResolveUserFromRequestAsync(context, sqliteConnectionString, store);
        if (user.UserId is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new
        {
            userId = user.UserId,
            name = user.Name,
            email = user.Email,
            apiKey = user.ApiKey
        });
    })
    .WithName("MeuPerfil")
    .WithTags("Autenticação");

    // ==========================================
    // CONSULTA E CADASTRO DE ALVOS MONITORADOS
    // ==========================================
    app.MapGet("/api/v1/targets", async (HttpContext context, ClientCredentialStore store) =>
    {
        try
        {
            var user = await ProgramAuthHelpers.ResolveUserFromRequestAsync(context, sqliteConnectionString, store);
            var hasApiKeyHeader = !string.IsNullOrWhiteSpace(context.Request.Headers["X-Api-Key"].FirstOrDefault());
            if (hasApiKeyHeader && user.UserId is null)
            {
                return Results.Unauthorized();
            }

            using var connection = new SqliteConnection(sqliteConnectionString);
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            if (user.UserId is not null)
            {
                cmd.CommandText = @"
                    SELECT Id, Url, TargetBrand, HttpStatus, LastStatus, IsActive, LastChecked, UserId
                    FROM MonitoredTargets
                    WHERE UserId = $userId
                    ORDER BY Id DESC;";
                cmd.Parameters.AddWithValue("$userId", user.UserId.Value);
            }
            else
            {
                cmd.CommandText = @"
                    SELECT Id, Url, TargetBrand, HttpStatus, LastStatus, IsActive, LastChecked, UserId
                    FROM MonitoredTargets
                    ORDER BY Id DESC;";
            }

            var targets = new List<object>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                targets.Add(new
                {
                    Id = reader.GetInt32(0),
                    Url = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    TargetBrand = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    HttpStatus = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    LastStatus = reader.IsDBNull(4) ? null : reader.GetString(4),
                    IsActive = !reader.IsDBNull(5) && reader.GetInt32(5) == 1,
                    LastChecked = reader.IsDBNull(6) ? null : reader.GetString(6),
                    UserId = reader.IsDBNull(7) ? (long?)null : reader.GetInt64(7)
                });
            }

            return Results.Ok(targets);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erro ao listar alvos monitorados.");
            return Results.Problem("Erro ao listar alvos monitorados.");
        }
    })
    .WithName("ListarAlvos")
    .WithTags("Monitoramento Autônomo");

    app.MapPost("/api/v1/targets", async (HttpContext context, CreateTargetRequest request, ClientCredentialStore store) =>
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return Results.BadRequest(new { erro = "A URL é obrigatória." });
        }

        var user = await ProgramAuthHelpers.ResolveUserFromRequestAsync(context, sqliteConnectionString, store);
        var hasApiKeyHeader = !string.IsNullOrWhiteSpace(context.Request.Headers["X-Api-Key"].FirstOrDefault());
        if (hasApiKeyHeader && user.UserId is null)
        {
            return Results.Unauthorized();
        }

        var normalizedUrl = request.Url.Trim();
        if (!normalizedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalizedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalizedUrl = "https://" + normalizedUrl;
        }

        var brand = string.IsNullOrWhiteSpace(request.TargetBrand)
            ? (user.Name ?? "Alvo Manual")
            : request.TargetBrand.Trim();

        try
        {
            using var connection = new SqliteConnection(sqliteConnectionString);
            await connection.OpenAsync();

            var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO MonitoredTargets (Url, TargetBrand, HttpStatus, LastStatus, IsActive, LastChecked, UserId)
                VALUES ($url, $brand, 0, 'Aguardando leitura', 1, NULL, $userId);
                SELECT last_insert_rowid();";
            insertCmd.Parameters.AddWithValue("$url", normalizedUrl);
            insertCmd.Parameters.AddWithValue("$brand", brand);
            insertCmd.Parameters.AddWithValue("$userId", (object?)user.UserId ?? DBNull.Value);

            var id = Convert.ToInt64(await insertCmd.ExecuteScalarAsync());

            return Results.Created($"/api/v1/targets/{id}", new
            {
                Id = id,
                Url = normalizedUrl,
                TargetBrand = brand,
                HttpStatus = 0,
                LastStatus = "Aguardando leitura",
                IsActive = true,
                UserId = user.UserId
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erro ao cadastrar alvo {Url}", normalizedUrl);
            return Results.Problem("Erro ao cadastrar alvo monitorado.");
        }
    })
    .WithName("CriarAlvo")
    .WithTags("Monitoramento Autônomo");

    // ==========================================
    // TELEMETRIA DO AGENTE (GuardiA.Agent)
    // ==========================================
    app.MapPost("/api/v1/agent/heartbeat", async (
        AgentHeartbeatRequest request,
        HttpContext httpContext,
        AgentTelemetryRepository repo) =>
    {
        var expectedAgentToken = app.Configuration["AgentConfig:AgentToken"];
        if (!string.IsNullOrWhiteSpace(expectedAgentToken))
        {
            var sentToken = httpContext.Request.Headers["X-Guardia-Agent-Token"].ToString();
            if (!string.Equals(sentToken, expectedAgentToken, StringComparison.Ordinal))
            {
                return Results.Unauthorized();
            }
        }

        var saved = await repo.UpsertAsync(request, httpContext.RequestAborted);
        return Results.Ok(new
        {
            status = "ok",
            deviceKey = saved.DeviceKey,
            clientId = saved.ClientId,
            machineName = saved.MachineName,
            receivedAtUtc = DateTimeOffset.UtcNow
        });
    })
    .WithName("RegistrarHeartbeatAgente")
    .WithTags("Agentes e Monitoramento");

    app.MapGet("/api/v1/agent/devices", async (AgentTelemetryRepository repo, CancellationToken ct) =>
    {
        var devices = await repo.GetAllAsync(ct);
        return Results.Ok(devices);
    })
    .WithName("ListarDispositivosAgente")
    .WithTags("Agentes e Monitoramento");

    // ==========================================
    // MÓDULOS DA API (Minimal APIs)
    // ==========================================
    app.MapPost("/api/v1/scanner/url", async (UrlScanRequest request) =>
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return Results.BadRequest(new { erro = "A URL é obrigatória." });

        try
        {
            var targetUrl = request.Url;
            if (!targetUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !targetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                targetUrl = "https://" + targetUrl;
            }

            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var redirectClient = new HttpClient(handler);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Head, targetUrl);
            using var response = await redirectClient.SendAsync(requestMessage);

            bool isRedirect = (int)response.StatusCode >= 300 && (int)response.StatusCode <= 399;
            string redirectLocation = response.Headers.Location?.ToString() ?? "Nenhum";

            return Results.Ok(new
            {
                UrlOriginal = request.Url,
                CodigoStatus = (int)response.StatusCode,
                PossuiRedirecionamento = isRedirect,
                DestinoRedirecionamento = redirectLocation,
                AvaliacaoRisco = isRedirect ? "ALERTA: Link com redirecionamento detectado." : "OK: Sem redirecionamentos imediatos."
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erro ao analisar URL {Url}", request.Url);
            return Results.Problem("Erro ao processar a URL informada.");
        }
    })
    .WithName("AnalisarUrl")
    .WithTags("Varredura de Links");

    app.MapPost("/api/v1/targets/{id}/scan", async (int id, HttpClient httpClient) =>
    {
        using var connection = new SqliteConnection(sqliteConnectionString);
        await connection.OpenAsync();

        var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT Url FROM MonitoredTargets WHERE Id = $id;";
        selectCmd.Parameters.AddWithValue("$id", id);
        var url = selectCmd.ExecuteScalar()?.ToString();

        if (string.IsNullOrEmpty(url))
            return Results.NotFound(new { erro = "Alvo não encontrado." });

        int statusCode = 0;
        string statusText = "Verificado";
        try
        {
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            var response = await httpClient.GetAsync(url);
            statusCode = (int)response.StatusCode;
            statusText = $"HTTP {statusCode}";
        }
        catch
        {
            statusCode = 503;
            statusText = "Indisponível / Timeout";
        }

        var updateCmd = connection.CreateCommand();
        updateCmd.CommandText = @"
            UPDATE MonitoredTargets
            SET HttpStatus = $status, LastStatus = $lastStatus, LastChecked = datetime('now')
            WHERE Id = $id;";
        updateCmd.Parameters.AddWithValue("$status", statusCode);
        updateCmd.Parameters.AddWithValue("$lastStatus", statusText);
        updateCmd.Parameters.AddWithValue("$id", id);
        await updateCmd.ExecuteNonQueryAsync();

        return Results.Ok(new { mensagem = "Varredura executada e status atualizado!", httpStatus = statusCode, lastStatus = statusText });
    })
    .WithName("EscanearAlvoPorId")
    .WithTags("Monitoramento Autônomo");

    app.MapPost("/api/v1/scanner/typosquatting", async (
        HttpContext context,
        TyposquattingRequest request,
        ClientCredentialStore store) =>
    {
        if (string.IsNullOrWhiteSpace(request.Domain))
            return Results.BadRequest(new { erro = "O domínio base é obrigatório." });

        var user = await ProgramAuthHelpers.ResolveUserFromRequestAsync(context, sqliteConnectionString, store);
        var hasApiKeyHeader = !string.IsNullOrWhiteSpace(context.Request.Headers["X-Api-Key"].FirstOrDefault());
        if (hasApiKeyHeader && user.UserId is null)
            return Results.Unauthorized();

        var variacoes = ProgramHelpers.GerarVariacoesTyposquatting(request.Domain);
        string marcaAlvo = request.TargetBrand ?? "Typosquatting Alvo";
        int salvos = 0;

        try
        {
            using var connection = new SqliteConnection(sqliteConnectionString);
            await connection.OpenAsync();

            foreach (var dominio in variacoes)
            {
                var checkCmd = connection.CreateCommand();
                checkCmd.CommandText = "SELECT COUNT(*) FROM MonitoredTargets WHERE Url = $url;";
                checkCmd.Parameters.AddWithValue("$url", $"https://{dominio}");
                long exists = (long)(await checkCmd.ExecuteScalarAsync() ?? 0L);

                if (exists == 0)
                {
                    var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = @"
                        INSERT INTO MonitoredTargets (Url, TargetBrand, HttpStatus, LastStatus, IsActive, UserId)
                        VALUES ($url, $brand, 0, $status, 1, $userId);";
                    insertCmd.Parameters.AddWithValue("$url", $"https://{dominio}");
                    insertCmd.Parameters.AddWithValue("$brand", $"Typosquatting de {marcaAlvo}");
                    insertCmd.Parameters.AddWithValue("$status", "Pendente de Checagem");
                    insertCmd.Parameters.AddWithValue("$userId", (object?)user.UserId ?? DBNull.Value);
                    await insertCmd.ExecuteNonQueryAsync();
                    salvos++;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erro ao salvar variações de typosquatting.");
        }

        return Results.Ok(new
        {
            dominioOriginal = request.Domain,
            totalGerado = variacoes.Count,
            totalCadastradosNoRobo = salvos,
            possiveisAlvosMaliciosos = variacoes
        });
    })
    .WithName("AnalisarTyposquatting")
    .WithTags("Varredura de Links");

    app.MapPost("/api/v1/seguranca/verificar-senha", async (PasswordCheckRequest request, IHttpClientFactory httpClientFactory) =>
    {
        if (string.IsNullOrEmpty(request.Password))
            return Results.BadRequest(new { erro = "A senha não pode ser vazia." });

        try
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(request.Password);
            byte[] hashBytes = SHA1.HashData(inputBytes);
            string hash = Convert.ToHexString(hashBytes).ToUpper();

            string prefix = hash[..5];
            string suffix = hash[5..];

            var client = httpClientFactory.CreateClient();
            string response = await client.GetStringAsync($"https://api.pwnedpasswords.com/range/{prefix}");
            string[] matches = response.Split("\r\n");

            int count = 0;
            foreach (var line in matches)
            {
                var parts = line.Split(':');
                if (parts.Length == 2 && parts[0].Equals(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    count = int.Parse(parts[1]);
                    break;
                }
            }

            return Results.Ok(new
            {
                ExpostaEmVazamento = count > 0,
                TotalVazamentos = count,
                Recomendacao = count > 0
                    ? "CRÍTICO: Esta senha já apareceu em vazamentos públicos. Altere imediatamente!"
                    : "SEGURO: Nenhuma ocorrência pública encontrada para esta senha."
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erro ao consultar API HIBP");
            return Results.Problem("Erro ao consultar a base de vazamentos.");
        }
    })
    .WithName("VerificarVazamentoSenha")
    .WithTags("Gestão de Credenciais");

    app.MapPost("/api/v1/seguranca/auditar-forca-senha", (PasswordCheckRequest request) =>
    {
        if (string.IsNullOrEmpty(request.Password))
            return Results.BadRequest(new { erro = "A senha não pode ser vazia." });

        var pass = request.Password;
        bool temTamanho = pass.Length >= 8;
        bool temMaiuscula = Regex.IsMatch(pass, @"[A-Z]");
        bool temMinuscula = Regex.IsMatch(pass, @"[a-z]");
        bool temNumero = Regex.IsMatch(pass, @"[0-9]");
        bool temEspecial = Regex.IsMatch(pass, @"[\W_]");

        int pontuacao = 0;
        if (temTamanho) pontuacao++;
        if (pass.Length >= 12) pontuacao++;
        if (temMaiuscula) pontuacao++;
        if (temMinuscula) pontuacao++;
        if (temNumero) pontuacao++;
        if (temEspecial) pontuacao++;

        string nivelForca = pontuacao switch
        {
            <= 2 => "Fraca",
            3 or 4 => "Moderada",
            5 => "Forte",
            _ => "Muito Forte (Excelente)"
        };

        return Results.Ok(new
        {
            TamanhoMinimo8 = temTamanho,
            PossuiLetraMaiuscula = temMaiuscula,
            PossuiLetraMinuscula = temMinuscula,
            PossuiNumero = temNumero,
            PossuiCaractereEspecial = temEspecial,
            PontuacaoGeral = $"{pontuacao}/6",
            NivelDeSeguranca = nivelForca,
            AtendePadraoCorporativo = pontuacao >= 4
        });
    })
    .WithName("AuditarForcaSenha")
    .WithTags("Gestão de Credenciais");

    app.MapPost("/api/v1/seguranca/analisar-cabecalho-de-e-mail", (EmailHeaderRequest request) =>
    {
        if (string.IsNullOrWhiteSpace(request.HeaderContent))
            return Results.BadRequest(new { erro = "O conteúdo do cabeçalho é obrigatório." });

        var header = request.HeaderContent;
        string from = ProgramHelpers.ExtractHeaderValue(header, @"From:\s*(.*)");
        string returnPath = ProgramHelpers.ExtractHeaderValue(header, @"Return-Path:\s*(.*)");
        string spf = ProgramHelpers.ExtractHeaderValue(header, @"received-spf:\s*(\w+)");
        string dkim = ProgramHelpers.ExtractHeaderValue(header, @"dkim=(\w+)");
        string dmarc = ProgramHelpers.ExtractHeaderValue(header, @"dmarc=(\w+)");

        bool spoofingRisk = !string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(returnPath) &&
                            !from.Equals(returnPath, StringComparison.OrdinalIgnoreCase);

        return Results.Ok(new
        {
            RemetenteVisivel = from ?? "Não identificado",
            CaminhoRetorno = returnPath ?? "Não identificado",
            StatusSPF = spf ?? "Não configurado",
            StatusDKIM = dkim ?? "Não configurado",
            StatusDMARC = dmarc ?? "Não configurado",
            RiscoSpoofing = spoofingRisk,
            DiagnosticoFinal = spoofingRisk ? "ALTA AMEAÇA: Possível Email Spoofing detectado." : "BAIXO RISCO: Cabeçalhos consistentes."
        });
    })
    .WithName("AnalisarCabecalhoEmail")
    .WithTags("Proteção de E-mail");

    app.MapRelatoriosEndpoints(dbPath, ExpectedApiKey);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "O aplicativo parou inesperadamente.");
}
finally
{
    Log.CloseAndFlush();
}

// ==========================================
// MODELOS, AUTH E REPOSITÓRIO DE TELEMETRIA
// ==========================================
public sealed record RegisterUserRequest(string Name, string Email, string Password);
public sealed record LoginUserRequest(string Email, string Password);
public sealed record CreateTargetRequest(string Url, string? TargetBrand);

public static class ProgramAuthHelpers
{
    public static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new MailAddress(email);
            return string.Equals(addr.Address, email, StringComparison.OrdinalIgnoreCase) && email.Contains('.');
        }
        catch
        {
            return false;
        }
    }

    public static async Task EnsureColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = await pragmaCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var existingColumn = reader.GetString(1);
            if (string.Equals(existingColumn, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var alterCmd = connection.CreateCommand();
        alterCmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await alterCmd.ExecuteNonQueryAsync();
    }

    public static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);

        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split(':', 2);
        if (parts.Length != 2) return false;

        byte[] salt = Convert.FromBase64String(parts[0]);
        byte[] expectedHash = Convert.FromBase64String(parts[1]);
        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    public static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return "grd_live_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static async Task<(long? UserId, string? Email, string? Name, string? ApiKey)> ResolveUserFromRequestAsync(
        HttpContext context,
        string sqliteConnectionString,
        ClientCredentialStore store)
    {
        var apiKeyHeader = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKeyHeader))
            return (null, null, null, null);

        var trimmedKey = apiKeyHeader.Trim();

        // 1. Consulta primeiro o arquivo segregado de clientes
        var clientFromStore = await store.GetClientByApiKeyAsync(trimmedKey, context.RequestAborted);
        if (clientFromStore is not null)
        {
            return (
                clientFromStore.UserId,
                clientFromStore.Email,
                clientFromStore.Name,
                clientFromStore.ApiKey
            );
        }

        // 2. Fallback para registros legados no SQLite
        using var conn = new SqliteConnection(sqliteConnectionString);
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Email, Name, ApiKey
            FROM Users
            WHERE ApiKey = $apiKey AND IsActive = 1
            LIMIT 1;";
        cmd.Parameters.AddWithValue("$apiKey", trimmedKey);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return (null, null, null, null);

        return (
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3)
        );
    }
}

public sealed record AgentHeartbeatRequest(
    string ClientId,
    string MachineName,
    string? OsDescription,
    string? AgentVersion,
    DateTimeOffset TimestampUtc,
    double CpuLoadPercent,
    double MemoryUsedPercent,
    int ActiveTcpConnectionsCount,
    bool? WindowsFirewallDomainEnabled,
    bool? WindowsFirewallPrivateEnabled,
    bool? WindowsFirewallPublicEnabled,
    string? DefenderRealTimeStatus
);

public sealed record AgentDeviceDto(
    string DeviceKey,
    string ClientId,
    string MachineName,
    string OsDescription,
    string AgentVersion,
    DateTimeOffset LastSeenUtc,
    double CpuLoadPercent,
    double MemoryUsedPercent,
    int ActiveTcpConnectionsCount,
    bool? WindowsFirewallDomainEnabled,
    bool? WindowsFirewallPrivateEnabled,
    bool? WindowsFirewallPublicEnabled,
    string? DefenderRealTimeStatus
);

public sealed class AgentTelemetryRepository(string dbPath)
{
    private readonly string _connectionString = $"Data Source={dbPath}";

    public async Task<AgentDeviceDto> UpsertAsync(AgentHeartbeatRequest req, CancellationToken ct = default)
    {
        var clientId = string.IsNullOrWhiteSpace(req.ClientId) ? "cliente-padrao" : req.ClientId.Trim();
        var machineName = string.IsNullOrWhiteSpace(req.MachineName) ? "unknown-host" : req.MachineName.Trim();
        var deviceKey = $"{clientId}::{machineName}";
        var lastSeen = req.TimestampUtc == default ? DateTimeOffset.UtcNow : req.TimestampUtc;
        var osDesc = string.IsNullOrWhiteSpace(req.OsDescription) ? "Desconhecido" : req.OsDescription.Trim();
        var version = string.IsNullOrWhiteSpace(req.AgentVersion) ? "0.1.0-alpha" : req.AgentVersion.Trim();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO MonitoredAgents (
                DeviceKey, ClientId, MachineName, OsDescription, AgentVersion, LastSeenUtc,
                CpuLoadPercent, MemoryUsedPercent, ActiveTcpConnectionsCount,
                WindowsFirewallDomainEnabled, WindowsFirewallPrivateEnabled, WindowsFirewallPublicEnabled,
                DefenderRealTimeStatus
            ) VALUES (
                $key, $clientId, $machineName, $os, $ver, $seen,
                $cpu, $mem, $tcp,
                $fwDomain, $fwPrivate, $fwPublic,
                $defender
            )
            ON CONFLICT(DeviceKey) DO UPDATE SET
                ClientId = excluded.ClientId,
                MachineName = excluded.MachineName,
                OsDescription = excluded.OsDescription,
                AgentVersion = excluded.AgentVersion,
                LastSeenUtc = excluded.LastSeenUtc,
                CpuLoadPercent = excluded.CpuLoadPercent,
                MemoryUsedPercent = excluded.MemoryUsedPercent,
                ActiveTcpConnectionsCount = excluded.ActiveTcpConnectionsCount,
                WindowsFirewallDomainEnabled = excluded.WindowsFirewallDomainEnabled,
                WindowsFirewallPrivateEnabled = excluded.WindowsFirewallPrivateEnabled,
                WindowsFirewallPublicEnabled = excluded.WindowsFirewallPublicEnabled,
                DefenderRealTimeStatus = excluded.DefenderRealTimeStatus;";

        cmd.Parameters.AddWithValue("$key", deviceKey);
        cmd.Parameters.AddWithValue("$clientId", clientId);
        cmd.Parameters.AddWithValue("$machineName", machineName);
        cmd.Parameters.AddWithValue("$os", osDesc);
        cmd.Parameters.AddWithValue("$ver", version);
        cmd.Parameters.AddWithValue("$seen", lastSeen.ToString("O"));
        cmd.Parameters.AddWithValue("$cpu", req.CpuLoadPercent);
        cmd.Parameters.AddWithValue("$mem", req.MemoryUsedPercent);
        cmd.Parameters.AddWithValue("$tcp", req.ActiveTcpConnectionsCount);
        cmd.Parameters.AddWithValue("$fwDomain", req.WindowsFirewallDomainEnabled.HasValue ? (req.WindowsFirewallDomainEnabled.Value ? 1 : 0) : DBNull.Value);
        cmd.Parameters.AddWithValue("$fwPrivate", req.WindowsFirewallPrivateEnabled.HasValue ? (req.WindowsFirewallPrivateEnabled.Value ? 1 : 0) : DBNull.Value);
        cmd.Parameters.AddWithValue("$fwPublic", req.WindowsFirewallPublicEnabled.HasValue ? (req.WindowsFirewallPublicEnabled.Value ? 1 : 0) : DBNull.Value);
        cmd.Parameters.AddWithValue("$defender", (object?)req.DefenderRealTimeStatus ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);

        return new AgentDeviceDto(
            deviceKey, clientId, machineName, osDesc, version, lastSeen,
            req.CpuLoadPercent, req.MemoryUsedPercent, req.ActiveTcpConnectionsCount,
            req.WindowsFirewallDomainEnabled, req.WindowsFirewallPrivateEnabled, req.WindowsFirewallPublicEnabled,
            req.DefenderRealTimeStatus
        );
    }

    public async Task<IReadOnlyList<AgentDeviceDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = new List<AgentDeviceDto>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT DeviceKey, ClientId, MachineName, OsDescription, AgentVersion, LastSeenUtc,
                   CpuLoadPercent, MemoryUsedPercent, ActiveTcpConnectionsCount,
                   WindowsFirewallDomainEnabled, WindowsFirewallPrivateEnabled, WindowsFirewallPublicEnabled,
                   DefenderRealTimeStatus
            FROM MonitoredAgents
            ORDER BY LastSeenUtc DESC;";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var seenStr = reader.IsDBNull(5) ? DateTimeOffset.UtcNow.ToString("O") : reader.GetString(5);
            DateTimeOffset.TryParse(seenStr, out var seenDt);

            list.Add(new AgentDeviceDto(
                DeviceKey: reader.GetString(0),
                ClientId: reader.GetString(1),
                MachineName: reader.GetString(2),
                OsDescription: reader.IsDBNull(3) ? "Desconhecido" : reader.GetString(3),
                AgentVersion: reader.IsDBNull(4) ? "0.1.0-alpha" : reader.GetString(4),
                LastSeenUtc: seenDt == default ? DateTimeOffset.UtcNow : seenDt,
                CpuLoadPercent: reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
                MemoryUsedPercent: reader.IsDBNull(7) ? 0 : reader.GetDouble(7),
                ActiveTcpConnectionsCount: reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                WindowsFirewallDomainEnabled: reader.IsDBNull(9) ? null : reader.GetInt32(9) == 1,
                WindowsFirewallPrivateEnabled: reader.IsDBNull(10) ? null : reader.GetInt32(10) == 1,
                WindowsFirewallPublicEnabled: reader.IsDBNull(11) ? null : reader.GetInt32(11) == 1,
                DefenderRealTimeStatus: reader.IsDBNull(12) ? null : reader.GetString(12)
            ));
        }

        return list;
    }
}