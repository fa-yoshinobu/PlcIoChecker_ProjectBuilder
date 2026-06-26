using PlcIoCheckerQr.Core;

namespace PlcIoCheckerQr.Core.Tests;

internal static class ProjectInputBuilder
{
    public static ProjectInput MakeInput(
        string Name = "Unit Project",
        string Vendor = "Melsec",
        string ConnectionMode = "Real",
        string Host = "192.168.250.100",
        int? Port = null,
        int MonitorIntervalMs = 500,
        int TimeoutMs = 2000,
        string? MachineLabel = null,
        string KeyenceDeviceMode = "Normal",
        string TransportMode = "Tcp",
        int Network = 0,
        int Station = 255,
        int ModuleIo = 1023,
        int Multidrop = 0,
        string DevicesText = "D100",
        string WatchText = "",
        string TrapsText = "",
        string CommentsText = "") => new(
        Name: Name,
        Vendor: Vendor,
        ConnectionMode: ConnectionMode,
        Host: Host,
        Port: Port ?? (Vendor == "Keyence" ? 8501 : 1025),
        MonitorIntervalMs: MonitorIntervalMs,
        TimeoutMs: TimeoutMs,
        MachineLabel: MachineLabel ?? (Vendor == "Keyence" ? "KV-8000" : "iQ-R"),
        KeyenceDeviceMode: KeyenceDeviceMode,
        TransportMode: TransportMode,
        Network: Network,
        Station: Station,
        ModuleIo: ModuleIo,
        Multidrop: Multidrop,
        DevicesText: DevicesText,
        WatchText: WatchText,
        TrapsText: TrapsText,
        CommentsText: CommentsText);
}
