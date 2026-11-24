using System.Text.Json;

namespace GuardiA.API.Services;

public sealed record ClientAccountRecord(
    long UserId,
    string Name,
    string Email,
    string ApiKey,
    string CreatedAt,
    bool IsActive
);

public sealed record PasswordCredentialRecord(
    long UserId,
    string Email,
    string PasswordHash,
    string UpdatedAt
);

public sealed class ClientCredentialStore
{
    private readonly string _clientsFilePath;
    private readonly string _credentialsFilePath;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public ClientCredentialStore(string baseStoragePath)
    {
        var clientsDir = Path.Combine(baseStoragePath, "Clients");
        var credentialsDir = Path.Combine(baseStoragePath, "Credentials");

        Directory.CreateDirectory(clientsDir);
        Directory.CreateDirectory(credentialsDir);

        _clientsFilePath = Path.Combine(clientsDir, "clients.json");
        _credentialsFilePath = Path.Combine(credentialsDir, "passwords.json");

        if (!File.Exists(_clientsFilePath))
        {
            File.WriteAllText(_clientsFilePath, "[]");
        }

        if (!File.Exists(_credentialsFilePath))
        {
            File.WriteAllText(_credentialsFilePath, "[]");
        }
    }

    public async Task<ClientAccountRecord?> GetClientByEmailAsync(string normalizedEmail, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var clients = await ReadJsonAsync<List<ClientAccountRecord>>(_clientsFilePath, ct) ?? [];
            return clients.FirstOrDefault(c =>
                c.IsActive && string.Equals(c.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<ClientAccountRecord?> GetClientByApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var clients = await ReadJsonAsync<List<ClientAccountRecord>>(_clientsFilePath, ct) ?? [];
            return clients.FirstOrDefault(c =>
                c.IsActive && string.Equals(c.ApiKey, apiKey, StringComparison.Ordinal));
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<string?> GetPasswordHashByUserIdAsync(long userId, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var creds = await ReadJsonAsync<List<PasswordCredentialRecord>>(_credentialsFilePath, ct) ?? [];
            return creds.FirstOrDefault(p => p.UserId == userId)?.PasswordHash;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SaveClientAndPasswordAsync(
        ClientAccountRecord client,
        string passwordHash,
        CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var clients = await ReadJsonAsync<List<ClientAccountRecord>>(_clientsFilePath, ct) ?? [];
            var creds = await ReadJsonAsync<List<PasswordCredentialRecord>>(_credentialsFilePath, ct) ?? [];

            clients.RemoveAll(c => c.UserId == client.UserId ||
                string.Equals(c.Email, client.Email, StringComparison.OrdinalIgnoreCase));
            clients.Add(client);

            creds.RemoveAll(p => p.UserId == client.UserId ||
                string.Equals(p.Email, client.Email, StringComparison.OrdinalIgnoreCase));
            creds.Add(new PasswordCredentialRecord(
                UserId: client.UserId,
                Email: client.Email,
                PasswordHash: passwordHash,
                UpdatedAt: DateTime.UtcNow.ToString("o")
            ));

            await WriteJsonAsync(_clientsFilePath, clients, ct);
            await WriteJsonAsync(_credentialsFilePath, creds, ct);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, ct);
    }

    private static async Task WriteJsonAsync<T>(string path, T data, CancellationToken ct)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, data, _jsonOptions, ct);
    }
}
