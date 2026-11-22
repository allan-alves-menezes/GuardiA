using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GuardiAProjeto.Models;

// ==========================================
// DTOs / REQUESTS DA API
// ==========================================
public class UrlScanRequest
{
    public string Url { get; set; } = string.Empty;
}

public class TyposquattingRequest
{
    public string Domain { get; set; } = string.Empty;
    public string? TargetBrand { get; set; }
}

public class PasswordCheckRequest
{
    public string Password { get; set; } = string.Empty;
}

public class EmailHeaderRequest
{
    public string HeaderContent { get; set; } = string.Empty;
}

public class RegisterAdminDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Cpf { get; set; } = string.Empty;
    public int BirthYear { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? AssociatedIp { get; set; }
}

public class VaultStoreRequest
{
    public string Service { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string Password { get; set; } = string.Empty;
    public string MasterKey { get; set; } = string.Empty;
    public string? OwnerTeam { get; set; }
    public string? CreatedBy { get; set; }
}

public class VaultRetrieveRequest
{
    public string MasterKey { get; set; } = string.Empty;
    public string? TeamName { get; set; }
}

public class ClientIpRequest
{
    public string ClientName { get; set; } = string.Empty;
    public string AllowedIp { get; set; } = string.Empty;
}

public class TargetRequest
{
    public string TargetUrl { get; set; } = string.Empty;
    public string? TargetBrand { get; set; }
}

public class AnaliseRequest
{
    public string Url { get; set; } = string.Empty;
}

public class ValidateAccessRequest
{
    public string CredentialNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
}

public class RegisterAccessRequest
{
    public string FullName { get; set; } = string.Empty;
    public string CredentialNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string AuthorizedMachineId { get; set; } = string.Empty;
}

// ==========================================
// ENTIDADES DO BANCO DE DADOS (EF CORE)
// ==========================================
[Table("UserMachineAccesses")]
public class UserMachineAccess
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string FullName { get; set; } = string.Empty;

    [Required]
    public string CredentialNumber { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    public string AuthorizedMachineId { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}