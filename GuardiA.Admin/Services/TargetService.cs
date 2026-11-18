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

    public async Task<List<TargetDto>?> GetTargetsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<TargetDto>>("api/v1/targets");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao buscar alvos: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> AddTargetAsync(CreateTargetRequest request)
    {
        try
        {
            // Corrigido para a mesma base de rota "/api/v1/targets"
            var response = await _http.PostAsJsonAsync("api/v1/targets", request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao salvar alvo: {ex.Message}");
            return false;
        }
    }
}