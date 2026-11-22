namespace GuardiA.Admin.Models;

public sealed record AgentDeviceDto(
    string DeviceKey,
    string ClientId,
    string MachineName,
    string OsDescription,
    string AgentVersion,
    DateTimeOffset LastSeenUtc,
    double CpuLoadPercent,
    double MemoryUsedPercent,
    int ActiveTcpConnectionsCount,
    bool? WindowsFirewallDomainEnabled,
    bool? WindowsFirewallPrivateEnabled,
    bool? WindowsFirewallPublicEnabled,
    string? DefenderRealTimeStatus
)
{
    public bool IsOnline(TimeSpan? offlineThreshold = null)
    {
        var limit = offlineThreshold ?? TimeSpan.FromSeconds(75);
        return (DateTimeOffset.UtcNow - LastSeenUtc) <= limit;
    }

    public string StatusConexao => IsOnline() ? "Online" : "Offline";

    public string ResumoFirewall
    {
        get
        {
            string FormatPerfil(string nome, bool? ativo) =>
                ativo switch
                {
                    true => $"{nome}: ON",
                    false => $"{nome}: OFF",
                    null => $"{nome}: ?"
                };

            return $"{FormatPerfil("Dom", WindowsFirewallDomainEnabled)} | " +
                   $"{FormatPerfil("Priv", WindowsFirewallPrivateEnabled)} | " +
                   $"{FormatPerfil("Púb", WindowsFirewallPublicEnabled)}";
        }
    }
}