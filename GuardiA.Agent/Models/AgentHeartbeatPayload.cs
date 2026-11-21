namespace GuardiA.Agent.Models;

public sealed class AgentHeartbeatPayload
{
    public required string ClientId { get; init; }
    public required string MachineName { get; init; }
    public required string OsDescription { get; init; }
    public required string AgentVersion { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public double CpuLoadPercent { get; init; }
    public double MemoryUsedPercent { get; init; }
    public int ActiveTcpConnectionsCount { get; init; }
    public bool? WindowsFirewallDomainEnabled { get; init; }
    public bool? WindowsFirewallPrivateEnabled { get; init; }
    public bool? WindowsFirewallPublicEnabled { get; init; }
    public string? DefenderRealTimeStatus { get; init; }
}