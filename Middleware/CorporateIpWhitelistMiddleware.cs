using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Serilog;

namespace GuardiAProjeto.Core.Middleware
{
    public class CorporateIpWhitelistMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _dbPath;

        public CorporateIpWhitelistMiddleware(RequestDelegate next)
        {
            _next = next;
            _dbPath = Path.Combine(AppContext.BaseDirectory, "guardia_vault.db");
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Aplica a restrição de IP apenas para rotas sensíveis do cofre ou perímetro
            var path = context.Request.Path.Value ?? string.Empty;
            
            if (path.Contains("/api/v1/vault", StringComparison.OrdinalIgnoreCase))
            {
                // Obtém o IP remoto do cliente
                var remoteIp = context.Connection.RemoteIpAddress?.ToString();

                // Se houver proxy reverso, pode ser necessário ler o cabeçalho X-Forwarded-For
                if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedHeader))
                {
                    remoteIp = forwardedHeader.FirstOrDefault()?.Split(',')[0].Trim();
                }

                if (string.IsNullOrEmpty(remoteIp))
                {
                    Log.Warning("Tentativa de acesso negada: IP não identificado para a rota {Path}", path);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { erro = "Acesso negado. IP de origem não identificado." });
                    return;
                }

                // Verifica no banco de dados se o IP está autorizado
                bool isAllowed = false;
                try
                {
                    using var connection = new SqliteConnection($"Data Source={_dbPath}");
                    await connection.OpenAsync();

                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM AllowedCorporateIps WHERE AllowedIp = $ip;";
                    command.Parameters.AddWithValue("$ip", remoteIp);

                    long count = (long)(await command.ExecuteScalarAsync() ?? 0L);
                    isAllowed = count > 0;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Erro ao validar IP corporativo no banco de dados.");
                }

                if (!isAllowed)
                {
                    Log.Warning("Acesso bloqueado para o IP não autorizado: {Ip} na rota {Path}", remoteIp, path);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { erro = $"O IP {remoteIp} não está autorizado a acessar este perímetro corporativo." });
                    return;
                }
            }

            // Segue o fluxo normal se o IP for válido ou se a rota não for restrita por IP
            await _next(context);
        }
    }
}