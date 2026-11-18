using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class GuardiaAutonomoWorker : BackgroundService
{
    private readonly ILogger<GuardiaAutonomoWorker> _logger;
    private readonly string _dbPath;
    private readonly IHttpClientFactory _httpClientFactory;

    public GuardiaAutonomoWorker(ILogger<GuardiaAutonomoWorker> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _dbPath = Path.Combine(Directory.GetCurrentDirectory(), "guardia_vault.db");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("==================================================");
        _logger.LogInformation("[GUARDIA AUTÔNOMO] Robô avançado ativado e monitorando!");
        _logger.LogInformation("==================================================");

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("[Autônomo] Iniciando varredura profunda de alvos e integridade SSL...");
                await ExecutarVarreduraAvancadaAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Autônomo] Erro crítico no ciclo de varredura profunda.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task ExecutarVarreduraAvancadaAsync()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        // 1. Garante que a tabela existe
        var createTableCmd = connection.CreateCommand();
        createTableCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS MonitoredTargets (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TargetUrl TEXT NOT NULL,
                TargetBrand TEXT,
                LastStatus TEXT,
                LastCheckedAt TEXT,
                SslValidDays INTEGER DEFAULT 0,
                ContentHash TEXT
            );";
        createTableCmd.ExecuteNonQuery();

        // 2. AUTO-CORREÇÃO DE ESQUEMA: Adiciona as colunas caso o banco antigo já exista sem elas
        AdicionarColunaSeNaoExistir(connection, "MonitoredTargets", "SslValidDays", "INTEGER DEFAULT 0");
        AdicionarColunaSeNaoExistir(connection, "MonitoredTargets", "ContentHash", "TEXT");

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, TargetUrl, TargetBrand, ContentHash FROM MonitoredTargets;";

        var targets = new List<(int Id, string Url, string Brand, string OldHash)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                targets.Add((
                    reader.GetInt32(0), 
                    reader.GetString(1), 
                    reader.IsDBNull(2) ? "" : reader.GetString(2),
                    reader.IsDBNull(3) ? "" : reader.GetString(3)
                ));
            }
        }

        if (targets.Count == 0)
        {
            _logger.LogInformation("[Autônomo] Nenhum alvo cadastrado no momento.");
            return;
        }

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
        };

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

        foreach (var target in targets)
        {
            try
            {
                string checkUrl = target.Url;
                if (!checkUrl.StartsWith("http://") && !checkUrl.StartsWith("https://"))
                    checkUrl = "https://" + checkUrl;

                var response = await client.GetAsync(checkUrl);
                string htmlContent = await response.Content.ReadAsStringAsync();

                byte[] encodedBytes = Encoding.UTF8.GetBytes(htmlContent);
                byte[] hashBytes = SHA256.HashData(encodedBytes);
                string newHash = Convert.ToHexString(hashBytes);

                bool contentChanged = !string.IsNullOrEmpty(target.OldHash) && target.OldHash != newHash;
                if (contentChanged)
                {
                    _logger.LogWarning("⚠️ [ALERTA DE MUDANÇA] O conteúdo da página do alvo {Url} foi alterado!", target.Url);
                }

                int sslDaysRemaining = 0;
                if (checkUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var uri = new Uri(checkUrl);
                        using var tcpClient = new System.Net.Sockets.TcpClient();
                        await tcpClient.ConnectAsync(uri.Host, 443);
                        using var sslStream = new SslStream(tcpClient.GetStream(), false, (sender, certificate, chain, errors) => true);
                        await sslStream.AuthenticateAsClientAsync(uri.Host);

                        if (sslStream.RemoteCertificate is X509Certificate2 cert2)
                        {
                            var expirationDate = DateTime.Parse(cert2.GetExpirationDateString());
                            sslDaysRemaining = (expirationDate - DateTime.Now).Days;

                            if (sslDaysRemaining < 15)
                            {
                                _logger.LogWarning("🚨 [ALERTA SSL] O certificado do site {Url} expira em breve! Restam apenas {Days} dias.", target.Url, sslDaysRemaining);
                            }
                        }
                    }
                    catch
                    {
                        sslDaysRemaining = -1;
                    }
                }

                string statusResult = $"ONLINE - HTTP {(int)response.StatusCode} | SSL: {sslDaysRemaining}d Restantes";
                _logger.LogInformation("[Autônomo] Alvo verificado: {Url} -> {Status}", target.Url, statusResult);

                var updateCmd = connection.CreateCommand();
                updateCmd.CommandText = @"
                    UPDATE MonitoredTargets 
                    SET LastStatus = $status, LastCheckedAt = $time, SslValidDays = $ssl, ContentHash = $hash 
                    WHERE Id = $id;";
                updateCmd.Parameters.AddWithValue("$status", statusResult);
                updateCmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                updateCmd.Parameters.AddWithValue("$ssl", sslDaysRemaining);
                updateCmd.Parameters.AddWithValue("$hash", newHash);
                updateCmd.Parameters.AddWithValue("$id", target.Id);
                updateCmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("❌ [Autônomo] Falha crítica ao processar o alvo {Url}: {Message}", target.Url, ex.Message);
            }
        }
    }

    private void AdicionarColunaSeNaoExistir(SqliteConnection connection, string tabela, string coluna, string tipoDef)
    {
        try
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {tabela} ADD COLUMN {coluna} {tipoDef};";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Se a coluna já existir, o SQLite vai disparar uma exceção que ignoramos com segurança aqui.
        }
    }
}