using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace GuardiAProjeto.Core.Middleware
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

            bool isSuspicious = path.Contains("wp-login") || 
                                path.Contains(".env") || 
                                path.Contains("admin/config") || 
                                queryString.Contains("union select") || 
                                queryString.Contains("<script>");

            if (isSuspicious)
            {
                _logger.LogWarning("[PROJETO NEVEO] Tentativa de invasão bloqueada! IP: {Ip} acessou a rota suspeita: {Path}", 
                    context.Connection.RemoteIpAddress?.ToString() ?? "Desconhecido", path);

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
                return; 
            }

            await _next(context);
        }
    }
}