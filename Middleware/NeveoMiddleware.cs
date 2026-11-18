using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace GuardiAProjeto.Middleware
{
    public class NeveoMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<NeveoMiddleware> _logger;

        public NeveoMiddleware(RequestDelegate next, ILogger<NeveoMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            string path = context.Request.Path.Value?.ToLower() ?? "";
            string queryString = context.Request.QueryString.Value?.ToLower() ?? "";

            // Padrões simulados de tentativa de invasão ou varredura maliciosa (A "Névoa" aciona aqui)
            bool isSuspicious = path.Contains("wp-login") || 
                                path.Contains(".env") || 
                                path.Contains("admin/config") || 
                                queryString.Contains("union select") || 
                                queryString.Contains("<script>");

            if (isSuspicious)
            {
                // Registra o incidente silenciosamente para o suporte técnico de fraudes
                _logger.LogWarning("[PROJETO NEVEO] Ameaça detectada! Origem IP: {Ip} tentou acessar a rota suspeita: {Path}", 
                    context.Connection.RemoteIpAddress?.ToString() ?? "Desconhecido", path);

                // Dispara a Cortina de Fumaça (Simulando uma falha real de sistema para enganar o invasor)
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";

                var fakeErrorResponse = new
                {
                    error = "Microsoft.Data.Sqlite.SqliteException",
                    message = "SQLite Error 11: 'database disk image is malformed' during internal command execution.",
                    path = path,
                    timestamp = DateTime.UtcNow
                };

                await context.Response.WriteAsync(JsonSerializer.Serialize(fakeErrorResponse));
                return; // Interrompe o fluxo aqui, o invasor caiu na fumaça e não chega na API real!
            }

            // Se o tráfego for limpo, o fluxo segue normalmente para os endpoints reais ou para o espelho
            await _next(context);
        }
    }
}