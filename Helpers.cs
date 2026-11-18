using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace GuardiAProjeto;

public static class ProgramHelpers
{
    public static void InitDatabase(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Vault (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Service TEXT NOT NULL,
                Username TEXT,
                EncryptedPassword TEXT NOT NULL,
                OwnerTeam TEXT,
                CreatedBy TEXT
            );

            CREATE TABLE IF NOT EXISTS AllowedCorporateIps (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ClientName TEXT NOT NULL,
                AllowedIp TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS MonitoredTargets (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT,
                Url TEXT NOT NULL,
                TargetBrand TEXT,
                LastStatus TEXT,
                LastCheckedAt TEXT,
                StatusCode INTEGER,
                SslValidDays INTEGER,
                IsActive INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS AdminUsers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT UNIQUE NOT NULL,
                PasswordHash TEXT NOT NULL,
                Role TEXT NOT NULL,
                FullName TEXT NOT NULL,
                Cpf TEXT UNIQUE NOT NULL,
                BirthYear INTEGER NOT NULL,
                City TEXT NOT NULL,
                State TEXT NOT NULL,
                AssociatedIp TEXT,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS UserMachineAccesses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FullName TEXT NOT NULL,
                CredentialNumber TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                AuthorizedMachineId TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
        ";
        command.ExecuteNonQuery();
    }

    public static string EncryptString(string plainText, string masterKey)
    {
        byte[] iv = new byte[16];
        byte[] array;
        using (Aes aes = Aes.Create())
        {
            byte[] salt = new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x42, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 };
            aes.Key = Rfc2898DeriveBytes.Pbkdf2(masterKey, salt, 10000, HashAlgorithmName.SHA256, 32);
            aes.IV = iv;
            using ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using MemoryStream memoryStream = new();
            using CryptoStream cryptoStream = new(memoryStream, encryptor, CryptoStreamMode.Write);
            using (StreamWriter streamWriter = new(cryptoStream)) { streamWriter.Write(plainText); }
            array = memoryStream.ToArray();
        }
        return Convert.ToBase64String(array);
    }

    public static string DecryptString(string cipherText, string masterKey)
    {
        byte[] iv = new byte[16];
        byte[] buffer = Convert.FromBase64String(cipherText);
        using (Aes aes = Aes.Create())
        {
            byte[] salt = new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x42, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 };
            aes.Key = Rfc2898DeriveBytes.Pbkdf2(masterKey, salt, 10000, HashAlgorithmName.SHA256, 32);
            aes.IV = iv;
            using ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using MemoryStream memoryStream = new(buffer);
            using CryptoStream cryptoStream = new(memoryStream, decryptor, CryptoStreamMode.Read);
            using StreamReader streamReader = new(cryptoStream);
            return streamReader.ReadToEnd();
        }
    }

    public static List<string> GerarVariacoesTyposquatting(string dominio)
    {
        var lista = new List<string>();
        var partes = dominio.Split('.');
        if (partes.Length == 0) return lista;
        string nome = partes[0];
        string extensao = partes.Length > 1 ? "." + string.Join(".", partes[1..]) : ".com";
        if (nome.Length > 1)
        {
            lista.Add(nome[1..] + extensao);
            lista.Add(nome + nome[^1] + extensao);
        }
        lista.Add(nome + "s" + extensao);
        return lista;
    }

    public static string ExtractHeaderValue(string headerContent, string pattern)
    {
        var match = Regex.Match(headerContent, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }
}

public record ValidateAccessRequest(string CredentialNumber, string Password, string MachineId);
public record RegisterAccessRequest(string FullName, string CredentialNumber, string Password, string AuthorizedMachineId);