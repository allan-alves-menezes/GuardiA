namespace GuardiA.Admin.Models
{
    public class CreateTargetRequest
    {
        public string TargetUrl { get; set; } = string.Empty;
        public string? TargetBrand { get; set; }
    }
}