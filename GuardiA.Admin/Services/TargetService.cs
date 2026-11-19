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

    public async Task<List<TargetDto>?> GetTargetsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<TargetDto>>("api/v1/targets", ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter alvos: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> AddTargetAsync(CreateTargetRequest request, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("api/v1/targets", request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao guardar alvo: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ScanTargetNowAsync(int targetId, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsync($"api/v1/targets/{targetId}/scan", content: null, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao iniciar varredura no alvo {targetId}: {ex.Message}");
            return false;
        }
    }
}