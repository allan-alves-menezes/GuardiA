using Microsoft.EntityFrameworkCore;
using GuardiAProjeto.Models;

namespace GuardiAProjeto
{
    public static class GuardiaEndpoints
    {
        public static void MapGuardiaEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/v1").WithTags("GuardiA - API");

            // 1. Endpoint Nativo de Saúde da API (/healthz)
            group.MapGet("/healthz", async (GuardiaDbContext db) =>
            {
                try
                {
                    var canConnect = await db.Database.CanConnectAsync();
                    return canConnect 
                        ? Results.Ok(new { Status = "Healthy", Database = "Connected", Timestamp = DateTime.UtcNow }) 
                        : Results.StatusCode(503);
                }
                catch (Exception ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: 500);
                }
            });

            // 2. Gerenciamento de Alvos (Targets)
            var targets = group.MapGroup("/targets");

            targets.MapPost("/", async (MonitoredTarget target, GuardiaDbContext db) =>
            {
                target.IsActive = true;
                db.MonitoredTargets.Add(target);
                await db.SaveChangesAsync();
                return Results.Created($"/api/v1/targets/{target.Id}", target);
            });

            targets.MapGet("/", async (GuardiaDbContext db) =>
            {
                var list = await db.MonitoredTargets.ToListAsync();
                return Results.Ok(list);
            });

            // 3. Histórico de Saúde (Health Logs)
            group.MapGet("/health/history", async (GuardiaDbContext db) =>
            {
                var logs = await db.HealthLogs
                    .OrderByDescending(h => h.CheckedAt)
                    .Take(50) // Traz os últimos 50 registros de auditoria
                    .ToListAsync();
                return Results.Ok(logs);
            });
        }
    }
}