using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GuardiA.API.Models;

public static class ProgramHelpers
{
    public static void InitDatabase(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        var tableCmd = connection.CreateCommand();
        tableCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS MonitoredTargets (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Url TEXT NOT NULL,
                TargetBrand TEXT,
                LastStatus TEXT,
                LastCheckedAt TEXT,
                StatusCode INTEGER,
                SslValidDays INTEGER,
                IsActive INTEGER DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS Vault (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Service TEXT NOT NULL,
                Username TEXT,
                EncryptedPassword TEXT NOT NULL,
                OwnerTeam TEXT,
                CreatedBy TEXT
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
                CredentialNumber TEXT UNIQUE NOT NULL,
                PasswordHash TEXT NOT NULL,
                AuthorizedMachineId TEXT NOT NULL,
                IsActive INTEGER DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
        ";
        tableCmd.ExecuteNonQuery();
    }

    public static List<string> GerarVariacoesTyposquatting(string domain)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(domain)) return list;

        string cleanDomain = domain.Replace("https://", "").Replace("http://", "").Trim('/');
        list.Add(cleanDomain.Replace(".", "-") + ".com");
        list.Add("secure-" + cleanDomain);
        list.Add(cleanDomain + "online.com");
        return list;
    }

    public static string ExtractHeaderValue(string headerContent, string pattern)
    {
        var match = Regex.Match(headerContent, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    public static string EncryptString(string plainText, string masterKey)
    {
        byte[] iv = new byte[16];
        byte[] array;

        using (Aes aes = Aes.Create())
        {
            aes.Key = Encoding.UTF8.GetBytes(masterKey.PadRight(32)[..32]);
            aes.IV = iv;
            using MemoryStream memoryStream = new();
            using CryptoStream cryptoStream = new(memoryStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
            using StreamWriter streamWriter = new(cryptoStream);
            streamWriter.Write(plainText);
            array = memoryStream.ToArray();
        }

        return Convert.ToBase64String(array);
    }

    public static string DecryptString(string cipherText, string masterKey)
    {
        byte[] iv = new byte[16];
        byte[] buffer = Convert.FromBase64String(cipherText);

        using Aes aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(masterKey.PadRight(32)[..32]);
        aes.IV = iv;
        using MemoryStream memoryStream = new(buffer);
        using CryptoStream cryptoStream = new(memoryStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using StreamReader streamReader = new(cryptoStream);
        return streamReader.ReadToEnd();
    }
}

// ==========================================
// DTOs da API
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
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MasterKey { get; set; } = string.Empty;
    public string? OwnerTeam { get; set; }
    public string? CreatedBy { get; set; }
}

public class VaultRetrieveRequest
{
    public string MasterKey { get; set; } = string.Empty;
}

public class TargetRequest
{
    public string TargetUrl { get; set; } = string.Empty;
    public string? TargetBrand { get; set; }
}