namespace GuardiA.Admin.Models;

public class UsuarioMonitorado
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    
    // Controle de autonomia e notificações
    public bool NotificacoesAtivas { get; set; } = true;
    public string? CanalNotificacao { get; set; } // Ex: "Email", "Webhook", "Desktop"
    
    // Controle de download do agente desktop
    public bool BaixouAgenteDesktop { get; set; } = false;
    public string ChaveLicencaAgente { get; set; } = Guid.NewGuid().ToString("N").ToUpper();
    public DateTime DataRegistro { get; set; } = DateTime.UtcNow;
}