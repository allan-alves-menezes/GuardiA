namespace GuardIA.Models
{
    public class SecurityAlert
    {
        public int Id { get; set; }
        public string Target { get; set; } = string.Empty; // Ex: Domínio monitorado ou E-mail
        public string AlertType { get; set; } = string.Empty; // Ex: "TyposquattingDetected", "CredentialLeak"
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsResolved { get; set; } = false;
    }
}