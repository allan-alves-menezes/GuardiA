using System.Collections.Concurrent;
using GuardiA.Agent.Models;

namespace GuardiA.Agent.Services;

public sealed class DownloadRiskWatcher(ILogger<DownloadRiskWatcher> logger) : IDisposable
{
    private static readonly HashSet<string> HighRiskExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".scr", ".jar", ".pif", ".hta"
    };

    private readonly ConcurrentQueue<AgentSecurityEventPayload> _pendingEvents = new();
    private FileSystemWatcher? _watcher;

    public void Start()
    {
        try
        {
            var downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");

            if (!Directory.Exists(downloadsPath)) return;

            _watcher = new FileSystemWatcher(downloadsPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileCreated;
            _watcher.Renamed += OnFileRenamed;
            logger.LogInformation("Monitoramento de pasta Downloads ativo em: {Path}", downloadsPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao iniciar monitoramento da pasta Downloads.");
        }
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e) => HandlePath(e.Name);
    private void OnFileRenamed(object sender, RenamedEventArgs e) => HandlePath(e.Name);

    private void HandlePath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return;
        if (fileName.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            return;

        var ext = Path.GetExtension(fileName);
        var isHighRisk = HighRiskExtensions.Contains(ext);
        var summary = isHighRisk
            ? $"Arquivo baixado com extensao de alto risco ({ext}): {fileName}"
            : $"Novo arquivo baixado no PC: {fileName}";

        _pendingEvents.Enqueue(new AgentSecurityEventPayload
        {
            DeviceId = $"allan-desktop-{Environment.MachineName.ToLowerInvariant()}",
            Hostname = Environment.MachineName,
            EventType = "download",
            TargetOrFile = fileName,
            RiskSummary = summary,
            HasRisk = isHighRisk,
            DetectedAtUtc = DateTimeOffset.UtcNow
        });
    }

    public List<AgentSecurityEventPayload> DrainEvents()
    {
        var list = new List<AgentSecurityEventPayload>();
        while (_pendingEvents.TryDequeue(out var ev)) list.Add(ev);
        return list;
    }

    public void Dispose() => _watcher?.Dispose();
}
