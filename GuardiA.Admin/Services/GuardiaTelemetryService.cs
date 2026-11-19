namespace GuardiA.Admin.Services;

public class GuardiaTelemetryService
{
    private readonly HttpClient _httpClient;

    public GuardiaTelemetryService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(3);
    }

    public bool IsApiOnline { get; private set; }
    public string ApiStatusLabel { get; private set; } = "Verificando...";
    public int SecurityScore { get; private set; } = 100;
    public DateTimeOffset? LastScanAt { get; private set; }
    public DateTimeOffset? NextScanAt { get; private set; }

    public event Action? OnStateChanged;

    public async Task CheckApiHealthAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/health", ct);
            IsApiOnline = response.IsSuccessStatusCode;
            ApiStatusLabel = IsApiOnline ? "Online" : $"HTTP {(int)response.StatusCode}";
        }
        catch
        {
            IsApiOnline = false;
            ApiStatusLabel = "Offline / Acordando";
        }

        NotifyStateChanged();
    }

    public void UpdateScore(int totalTargets, int highOrCriticalAlerts)
    {
        if (totalTargets <= 0 && !IsApiOnline)
        {
            SecurityScore = 0;
        }
        else
        {
            var penalty = Math.Min(80, highOrCriticalAlerts * 15);
            SecurityScore = Math.Clamp(100 - penalty, 0, 100);
        }

        NotifyStateChanged();
    }

    public void RecordScanCycle(DateTimeOffset executedAt, TimeSpan interval)
    {
        LastScanAt = executedAt;
        NextScanAt = executedAt.Add(interval);
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnStateChanged?.Invoke();
}