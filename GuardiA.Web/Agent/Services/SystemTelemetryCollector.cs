using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using GuardiA.Agent.Models;

namespace GuardiA.Agent.Services;

public sealed class SystemTelemetryCollector(ILogger<SystemTelemetryCollector> logger)
{
    public async Task<AgentHeartbeatPayload> CollectAsync(string clientId, CancellationToken ct)
    {
        var tcpCount = 0;
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            tcpCount = props.GetActiveTcpConnections().Length;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Falha ao coletar conexões TCP ativas.");
        }

        var memUsedPercent = GetMemoryUsedPercent();
        var cpuLoadPercent = await GetCpuLoadEstimateAsync(ct);

        bool? domainFw = null;
        bool? privateFw = null;
        bool? publicFw = null;
        string? defenderStatus = null;

        if (OperatingSystem.IsWindows())
        {
            var fwStatus = await TryGetWindowsFirewallStatusAsync(ct);
            domainFw = fwStatus.Domain;
            privateFw = fwStatus.Private;
            publicFw = fwStatus.Public;

            defenderStatus = await TryGetDefenderRealTimeStatusAsync(ct);
        }

        return new AgentHeartbeatPayload
        {
            ClientId = clientId,
            MachineName = Environment.MachineName,
            OsDescription = RuntimeInformation.OSDescription,
            AgentVersion = "0.1.0-alpha",
            TimestampUtc = DateTimeOffset.UtcNow,
            CpuLoadPercent = Math.Round(cpuLoadPercent, 1),
            MemoryUsedPercent = Math.Round(memUsedPercent, 1),
            ActiveTcpConnectionsCount = tcpCount,
            WindowsFirewallDomainEnabled = domainFw,
            WindowsFirewallPrivateEnabled = privateFw,
            WindowsFirewallPublicEnabled = publicFw,
            DefenderRealTimeStatus = defenderStatus
        };
    }

    private static double GetMemoryUsedPercent()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        var totalAvailable = gcInfo.TotalAvailableMemoryBytes;
        if (totalAvailable <= 0) return 0;

        var usedBytes = Process.GetCurrentProcess().WorkingSet64;
        return Math.Clamp((double)usedBytes / totalAvailable * 100.0, 0, 100);
    }

    private static async Task<double> GetCpuLoadEstimateAsync(CancellationToken ct)
    {
        var proc = Process.GetCurrentProcess();
        var startCpu = proc.TotalProcessorTime;
        var startWall = DateTime.UtcNow;

        await Task.Delay(250, ct);

        proc.Refresh();
        var endCpu = proc.TotalProcessorTime;
        var endWall = DateTime.UtcNow;

        var cpuMs = (endCpu - startCpu).TotalMilliseconds;
        var elapsedMs = (endWall - startWall).TotalMilliseconds * Environment.ProcessorCount;
        if (elapsedMs <= 0) return 0;

        return Math.Clamp(cpuMs / elapsedMs * 100.0, 0, 100);
    }

    private async Task<(bool? Domain, bool? Private, bool? Public)> TryGetWindowsFirewallStatusAsync(CancellationToken ct)
    {
        try
        {
            const string script = "Get-NetFirewallProfile | Select-Object Name,Enabled | ConvertTo-Json -Compress";
            var output = await RunPowerShellAsync(script, ct);
            if (string.IsNullOrWhiteSpace(output)) return (null, null, null);

            using var doc = JsonDocument.Parse(output);
            bool? domain = null, priv = null, pub = null;

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var name = item.GetProperty("Name").GetString();
                    var enabled = item.TryGetProperty("Enabled", out var en) && en.ValueKind == JsonValueKind.True;

                    if (string.Equals(name, "Domain", StringComparison.OrdinalIgnoreCase)) domain = enabled;
                    else if (string.Equals(name, "Private", StringComparison.OrdinalIgnoreCase)) priv = enabled;
                    else if (string.Equals(name, "Public", StringComparison.OrdinalIgnoreCase)) pub = enabled;
                }
            }

            return (domain, priv, pub);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível consultar perfis do Windows Firewall.");
            return (null, null, null);
        }
    }

    private async Task<string?> TryGetDefenderRealTimeStatusAsync(CancellationToken ct)
    {
        try
        {
            const string script = "(Get-MpComputerStatus).RealTimeProtectionEnabled";
            var output = await RunPowerShellAsync(script, ct);
            return string.IsNullOrWhiteSpace(output) ? "Desconhecido" : output.Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível consultar status do Windows Defender.");
            return "Indisponível";
        }
    }

    private static async Task<string> RunPowerShellAsync(string command, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null) return string.Empty;

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return await outputTask;
    }
}