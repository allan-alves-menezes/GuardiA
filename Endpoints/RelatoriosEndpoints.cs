using Microsoft.Data.Sqlite;

namespace GuardiAProjeto.Endpoints
{
    public static class RelatoriosEndpoints
    {
        public static void MapRelatoriosEndpoints(this IEndpointRouteBuilder app, string dbPath, string expectedApiKey)
        {
            // Endpoint de Relatório Geral
            app.MapGet("/api/v1/relatorios/geral", (HttpContext context) =>
            {
                if (!context.Request.Headers.TryGetValue("X-Api-Key", out var key) || key != expectedApiKey)
                    return Results.Unauthorized();

                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                int targetCount = 0;
                using (var cmd = connection.CreateCommand()) { cmd.CommandText = "SELECT COUNT(*) FROM MonitoredTargets;"; targetCount = Convert.ToInt32(cmd.ExecuteScalar()); }

                int vaultCount = 0;
                using (var cmd = connection.CreateCommand()) { cmd.CommandText = "SELECT COUNT(*) FROM Vault;"; vaultCount = Convert.ToInt32(cmd.ExecuteScalar()); }

                return Results.Ok(new
                {
                    GeradoEm = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    StatusSistema = "Operacional, Seguro e Ativo",
                    TotalAlvos = targetCount,
                    TotalCredenciaisCofre = vaultCount
                });
            })
            .WithName("GerarRelatorioGeral")
            .WithTags("Relatórios e Auditoria");

            // Novo Dashboard KPI
            app.MapGet("/api/v1/relatorios/dashboard", (HttpContext context) =>
            {
                if (!context.Request.Headers.TryGetValue("X-Api-Key", out var key) || key != expectedApiKey)
                    return Results.Unauthorized();

                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                int totalAlvos = 0;
                int alvosAtivos = 0;
                using (var cmd = connection.CreateCommand()) 
                { 
                    cmd.CommandText = "SELECT COUNT(*), SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) FROM MonitoredTargets;";
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        totalAlvos = reader.GetInt32(0);
                        alvosAtivos = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    }
                }

                int totalCredenciais = 0;
                using (var cmd = connection.CreateCommand()) 
                { 
                    cmd.CommandText = "SELECT COUNT(*) FROM Vault;"; 
                    totalCredenciais = Convert.ToInt32(cmd.ExecuteScalar()); 
                }

                int totalColaboradores = 0;
                using (var cmd = connection.CreateCommand()) 
                { 
                    cmd.CommandText = "SELECT COUNT(*) FROM AdminUsers;"; 
                    totalColaboradores = Convert.ToInt32(cmd.ExecuteScalar()); 
                }

                return Results.Ok(new
                {
                    AtualizadoEm = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    Indicadores = new
                    {
                        SegurancaPerimetro = new { TotalAlvosMonitorados = totalAlvos, AlvosAtivosNoRadar = alvosAtivos },
                        CofreCorporativo = new { TotalSegredosArmazenados = totalCredenciais },
                        ControleDeAcessos = new { TotalColaboradoresCadastrados = totalColaboradores }
                    },
                    MensagemDashboard = "Indicadores consolidados com sucesso."
                });
            })
            .WithName("ObterDashboardKpi")
            .WithTags("Relatórios e Auditoria");
        }
    }
}