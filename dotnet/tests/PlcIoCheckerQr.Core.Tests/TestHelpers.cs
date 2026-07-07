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
        string ModuleIo = "OwnStation",
        string DevicesText = "D100,Int16",
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
        MachineLabel: MachineLabel ?? (Vendor == "Keyence" ? "KEYENCE KV-8000" : "MELSEC iQ-R (built-in)"),
        KeyenceDeviceMode: KeyenceDeviceMode,
        TransportMode: TransportMode,
        Network: Network,
        Station: Station,
        ModuleIo: ModuleIo,
        DevicesText: DevicesText,
        WatchText: WatchText,
        TrapsText: TrapsText,
        CommentsText: CommentsText);
}
