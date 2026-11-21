using System.Net.Http.Json;
using GuardiA.Admin.Models;

namespace GuardiA.Admin.Services;

public sealed class AgentMonitorService(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<IReadOnlyList<AgentDeviceDto>> GetDevicesAsync(CancellationToken ct = default)
    {
        var devices = await _httpClient.GetFromJsonAsync<List<AgentDeviceDto>>(
            "/api/v1/agent/devices",
            cancellationToken: ct
        );

        return devices ?? [];
    }
}