namespace GuardiA.API.Models;

public class UserMachineAccess
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string CredentialNumber { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string AuthorizedMachineId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}