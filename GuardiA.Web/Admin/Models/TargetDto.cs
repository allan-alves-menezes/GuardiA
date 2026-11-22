namespace GuardiA.Admin.Models
{
    public class TargetDto
    {
        public int Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public string? Brand { get; set; }
        public string LastStatus { get; set; } = string.Empty;
        public string LastCheckedAt { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public int SslDays { get; set; }
        public bool Active { get; set; }
    }
}