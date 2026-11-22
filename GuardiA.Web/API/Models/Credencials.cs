namespace GuardiAProjeto.Models
{
    public class TeamCredential
    {
        public int Id { get; set; }
        public string ServiceName { get; set; } = string.Empty; // Ex: GitHub, AWS, Servidor
        public string Username { get; set; } = string.Empty;
        public string EncryptedPassword { get; set; } = string.Empty; // Senha guardada criptografada
        public string OwnerTeam { get; set; } = string.Empty;       // Equipe responsável
        public string CreatedBy { get; set; } = string.Empty;       // Quem cadastrou
    }

    public class CredentialDto
    {
        public string ServiceName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string PlainPassword { get; set; } = string.Empty; // Senha normal enviada ao cadastrar
        public string OwnerTeam { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
    }
}