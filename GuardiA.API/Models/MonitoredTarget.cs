namespace GuardiAProjeto.Models;

public class MonitoredTarget
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    
    // Campos para registrar o resultado do monitoramento feito pelo Worker
    public string? LastStatus { get; set; } // Ex: "Online", "Offline", "Timeout"
    public int? StatusCode { get; set; }    // Ex: 200, 404, 500
    public DateTime? LastCheckedAt { get; set; }
}