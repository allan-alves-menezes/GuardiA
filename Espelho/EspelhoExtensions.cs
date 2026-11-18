using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace GuardiAProjeto
{
    public static class EspelhoExtensions
    {
        public static void ConfigurarEspelho(this WebApplication app)
        {
            app.MapGet("/api/v1/espelho/status", () => Results.Ok(new { status = "Espelho ativo e operando em modo armadilha." }))
               .WithName("StatusEspelho")
               .WithTags("Projeto Espelho");
        }
    }
}