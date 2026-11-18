namespace GuardiAProjeto.Models
{
    public class HealthLog
    {
        public int Id { get; set; }
        public int TargetId { get; set; }
        public string TargetName { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    }
}