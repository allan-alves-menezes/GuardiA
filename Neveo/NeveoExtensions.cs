using Microsoft.AspNetCore.Builder;
using GuardiAProjeto.Core.Middleware;

namespace GuardiAProjeto
{
    public static class NeveoExtensions
    {
        public static void ConfigurarNeveo(this WebApplication app)
        {
            // Ativa o middleware de interceptação e dissimulação do Neveo
            app.UseMiddleware<NeveoMiddleware>();
        }
    }
}