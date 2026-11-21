using System.Net.Http.Json;
using GuardiA.Admin.Models;

namespace GuardiA.Admin.Services;

public class TargetService
{
    private readonly HttpClient _http;

    public TargetService(HttpClient http)
    {
        _http = http;
    }

    public Task<List<TargetDto>?> GetTargetsAsync(CancellationToken ct)
        => GetTargetsAsync(apiKey: null, ct: ct);

    public async Task<List<TargetDto>?> GetTargetsAsync(string? apiKey = null, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/targets");
            ApplyApiKeyHeader(request, apiKey);

            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<TargetDto>>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter alvos: {ex.Message}");
            return null;
        }
    }

    public Task<bool> AddTargetAsync(CreateTargetRequest request, CancellationToken ct)
        => AddTargetAsync(request, apiKey: null, ct: ct);

    public async Task<bool> AddTargetAsync(CreateTargetRequest request, string? apiKey = null, CancellationToken ct = default)
    {
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/v1/targets")
            {
                Content = JsonContent.Create(request)
            };
            ApplyApiKeyHeader(httpRequest, apiKey);

            using var response = await _http.SendAsync(httpRequest, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao guardar alvo: {ex.Message}");
            return false;
        }
    }

    public Task<bool> ScanTargetNowAsync(int targetId, CancellationToken ct)
        => ScanTargetNowAsync(targetId, apiKey: null, ct: ct);

    public async Task<bool> ScanTargetNowAsync(int targetId, string? apiKey = null, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"api/v1/targets/{targetId}/scan");
            ApplyApiKeyHeader(request, apiKey);

            using var response = await _http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao iniciar varredura no alvo {targetId}: {ex.Message}");
            return false;
        }
    }

    private static void ApplyApiKeyHeader(HttpRequestMessage request, string? apiKey)
    {
        request.Headers.Remove("X-Api-Key");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Add("X-Api-Key", apiKey.Trim());
        }
    }
}