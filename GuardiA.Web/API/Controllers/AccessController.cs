using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GuardiA.API.Models;

namespace GuardiA.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AccessController : ControllerBase
{
    private readonly Data.GuardiaDbContext _context;
    private readonly ILogger<AccessController> _logger;

    public AccessController(Data.GuardiaDbContext context, ILogger<AccessController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // DTO para receber os dados enviados pelo programa cliente (Validação)
    public class ValidateAccessRequest
    {
        public string CredentialNumber { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string MachineId { get; set; } = string.Empty;
    }

    // DTO para receber os dados enviados ao cadastrar o acesso
    public class RegisterAccessRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string CredentialNumber { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string AuthorizedMachineId { get; set; } = string.Empty;
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateAccess([FromBody] ValidateAccessRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CredentialNumber) || 
            string.IsNullOrWhiteSpace(request.Password) || 
            string.IsNullOrWhiteSpace(request.MachineId))
        {
            return BadRequest(new { authorized = false, message = "Credencial, senha e ID da máquina são obrigatórios." });
        }

        // Busca a credencial no banco de dados
        var accessRecord = await _context.UserMachineAccesses
            .FirstOrDefaultAsync(u => u.CredentialNumber == request.CredentialNumber);

        if (accessRecord == null || !accessRecord.IsActive)
        {
            _logger.LogWarning("Tentativa de acesso negada: Credencial inválida ou inativa ({Credential})", request.CredentialNumber);
            return Unauthorized(new { authorized = false, message = "Credencial inválida ou acesso desativado." });
        }

        // Validação da senha
        if (accessRecord.PasswordHash != request.Password)
        {
            _logger.LogWarning("Tentativa de acesso negada: Senha incorreta para a credencial ({Credential})", request.CredentialNumber);
            return Unauthorized(new { authorized = false, message = "Senha incorreta." });
        }

        // Validação estrita do ID da Máquina (Garante que só roda no PC autorizado)
        if (accessRecord.AuthorizedMachineId != request.MachineId)
        {
            _logger.LogWarning("Tentativa de invasão/bloqueio: A máquina ({MachineId}) tentou usar a credencial de {AuthorizedMachine}", 
                request.MachineId, accessRecord.AuthorizedMachineId);
            
            return StatusCode(StatusCodes.Status403Forbidden, new { 
                authorized = false, 
                message = "Acesso negado: Esta credencial não está autorizada para ser usada neste computador." 
            });
        }

        _logger.LogInformation("Acesso liberado com sucesso para a credencial {Credential} na máquina {MachineId}", 
            request.CredentialNumber, request.MachineId);

        return Ok(new { 
            authorized = true, 
            message = "Acesso autorizado com sucesso.",
            userName = accessRecord.FullName 
        });
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterAccess([FromBody] RegisterAccessRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CredentialNumber) || 
            string.IsNullOrWhiteSpace(request.Password) || 
            string.IsNullOrWhiteSpace(request.AuthorizedMachineId) ||
            string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { success = false, message = "Todos os campos são obrigatórios para o cadastro." });
        }

        // Verifica se a credencial já existe no banco
        var exists = await _context.UserMachineAccesses
            .AnyAsync(u => u.CredentialNumber == request.CredentialNumber);

        if (exists)
        {
            return BadRequest(new { success = false, message = "Esta credencial já está cadastrada no sistema." });
        }

        var newAccess = new UserMachineAccess
        {
            FullName = request.FullName,
            CredentialNumber = request.CredentialNumber,
            PasswordHash = request.Password,
            AuthorizedMachineId = request.AuthorizedMachineId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.UserMachineAccesses.Add(newAccess);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Novo acesso cadastrado com sucesso para {Name} na máquina {MachineId}", 
            request.FullName, request.AuthorizedMachineId);

        return Ok(new { success = true, message = "Acesso cadastrado com sucesso! O usuário já pode utilizar o sistema." });
    }
}