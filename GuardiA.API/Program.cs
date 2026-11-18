using Scalar.AspNetCore;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;
using GuardiA.API;
using GuardiA.API.Endpoints;
using GuardiA.API.Models;
using GuardiA.API.Services;

Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

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

    string dbPath = Path.Combine(AppContext.BaseDirectory, "guardia_vault.db");
    ProgramHelpers.InitDatabase(dbPath);

    // Garantir que a tabela e colunas de targets existam no banco SQLite de forma segura
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        var createTableCmd = connection.CreateCommand();
        createTableCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS MonitoredTargets (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Url TEXT NOT NULL,
                TargetBrand TEXT NOT NULL,
                HttpStatus INTEGER DEFAULT 0,
                LastChecked TEXT
            );
        ";
        createTableCmd.ExecuteNonQuery();
    }

    builder.Services.AddDbContext<GuardiA.API.Data.GuardiaDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath}"));

    builder.Services.AddHostedService<AutonomousMonitoringService>();

    builder.Host.UseWindowsService();
    builder.Host.UseSerilog();

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(5005);
    });

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
    // MIDDLEWARE DE SEGURANÇA: Restrição de IP por Funcionário
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
                using var connection = new SqliteConnection($"Data Source={dbPath}");
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
                await context.Response.WriteAsJsonAsync(new { erro = $"Acesso negado. O IP da sua máquina ({remoteIp}) não está vinculado à ficha de nenhum colaborador autorizado." });
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
            return Results.Problem($"Erro ao processar URL: {ex.Message}");
        }
    })
    .WithName("AnalisarUrl")
    .WithTags("Varredura de Links");

    app.MapPost("/api/v1/targets/{id}/scan", async (int id, HttpClient httpClient) =>
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        // 1. Buscar a URL do alvo pelo ID
        var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT Url FROM MonitoredTargets WHERE Id = $id;";
        selectCmd.Parameters.AddWithValue("$id", id);
        var url = selectCmd.ExecuteScalar()?.ToString();

        if (string.IsNullOrEmpty(url))
            return Results.NotFound(new { erro = "Alvo não encontrado." });

        int statusCode = 0;
        try
        {
            // 2. Executar o scanner HTTP com timeout controlado de 5 segundos
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            var response = await httpClient.GetAsync(url);
            statusCode = (int)response.StatusCode;
        }
        catch
        {
            statusCode = 503; // Erro de conexão / Indisponível / Timeout
        }

        // 3. Atualizar o status e data da última verificação no banco
        var updateCmd = connection.CreateCommand();
        updateCmd.CommandText = @"
            UPDATE MonitoredTargets 
            SET HttpStatus = $status, LastChecked = datetime('now') 
            WHERE Id = $id;";
        updateCmd.Parameters.AddWithValue("$status", statusCode);
        updateCmd.Parameters.AddWithValue("$id", id);
        await updateCmd.ExecuteNonQueryAsync();

        return Results.Ok(new { mensagem = "Varredura executada e status atualizado!", httpStatus = statusCode });
    })
    .WithName("EscanearAlvoPorId")
    .WithTags("Monitoramento Autônomo");

    app.MapPost("/api/v1/scanner/typosquatting", (TyposquattingRequest request) =>
    {
        if (string.IsNullOrWhiteSpace(request.Domain))
            return Results.BadRequest(new { erro = "O domínio base é obrigatório." });

        var variacoes = ProgramHelpers.GerarVariacoesTyposquatting(request.Domain);
        string marcaAlvo = request.TargetBrand ?? "Typosquatting Alvo";
        int salvos = 0;

        try
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            foreach (var dominio in variacoes)
            {
                var checkCmd = connection.CreateCommand();
                checkCmd.CommandText = "SELECT COUNT(*) FROM MonitoredTargets WHERE Url = $url;";
                checkCmd.Parameters.AddWithValue("$url", $"https://{dominio}");
                long exists = (long)checkCmd.ExecuteScalar()!;

                if (exists == 0)
                {
                    var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = "INSERT INTO MonitoredTargets (Url, TargetBrand, LastStatus, IsActive) VALUES ($url, $brand, $status, 1);";
                    insertCmd.Parameters.AddWithValue("$url", $"https://{dominio}");
                    insertCmd.Parameters.AddWithValue("$brand", $"Typosquatting de {marcaAlvo}");
                    insertCmd.Parameters.AddWithValue("$status", "Pendente de Checagem");
                    insertCmd.ExecuteNonQuery();
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
            byte[] hashBytes = System.Security.Cryptography.SHA1.HashData(inputBytes);
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
            return Results.Problem($"Erro ao consultar base de vazamentos: {ex.Message}");
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