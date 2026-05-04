using System.Text.RegularExpressions;

namespace PlcIoCheckerQr.Core;

public sealed record PlcProject(
    string Id,
    string Name,
    PlcConnection Connection,
    IReadOnlyList<DeviceDefinition> Devices,
    IReadOnlyList<string> WatchItems,
    IReadOnlyList<TrapDefinition> Traps,
    AppSettings Settings,
    long UpdatedAtEpochMs);

public sealed record PlcConnection(
    string Vendor,
    string ConnectionMode,
    string Host,
    int Port,
    int MonitorIntervalMs,
    int TimeoutMs,
    string MachineLabel,
    string KeyenceDeviceMode,
    string TransportMode,
    int Network,
    int Station,
    int ModuleIo,
    int Multidrop);

public sealed record DeviceDefinition(string Address, string DataType);

public sealed record TrapDefinition(
    string Id,
    string Address,
    bool Enabled,
    string Condition,
    double? Threshold,
    int TriggerCount,
    long? LastTriggeredAtEpochMs,
    string? LastObservedValue);

public sealed record AppSettings(string BlockDisplayDensity);

public sealed record ProjectInput(
    string Name,
    string Vendor,
    string ConnectionMode,
    string Host,
    int Port,
    int MonitorIntervalMs,
    int TimeoutMs,
    string MachineLabel,
    string KeyenceDeviceMode,
    string TransportMode,
    int Network,
    int Station,
    int ModuleIo,
    int Multidrop,
    string DevicesText,
    string WatchText,
    string TrapsText,
    string BlockDisplayDensity = "Compact");

public static partial class ProjectFactory
{
    public const int MaxTimeChartTargets = 20;

    public static readonly string[] Vendors = ["Melsec", "Keyence"];
    public static readonly string[] ConnectionModes = ["Real", "DemoMock"];
    public static readonly string[] KeyenceDeviceModes = ["Normal", "Xym"];
    public static readonly string[] TransportModes = ["Tcp", "Udp"];
    public static readonly string[] BlockDisplayDensities = ["Compact", "Detailed"];
    public static readonly string[] DeviceDataTypes = ["Bit", "Int16", "UInt16", "Int32", "UInt32", "Float32"];
    public static readonly string[] MelsecCpuModels = ["iQ-R", "iQ-F", "iQ-L", "MX-R", "MX-F", "QnUDV", "QnU", "QCPU", "LCPU"];
    public static readonly string[] KeyenceCpuModels = ["KV-X500", "KV-8000", "KV-7000", "KV-5000"];

    public static readonly string[] BitTrapConditions = ["Rise", "Fall", "Change"];
    public static readonly string[] WordTrapConditions = ["Change", "GreaterOrEqual", "LessOrEqual", "Equal", "NotEqual"];
    public static readonly string[] TrapConditions = BitTrapConditions.Concat(WordTrapConditions).Distinct(StringComparer.Ordinal).ToArray();

    private static readonly HashSet<string> ValidTrapConditions = new(TrapConditions, StringComparer.Ordinal);
    private static readonly HashSet<string> MelsecBitDeviceKinds = new(["SB", "X", "Y", "M", "L", "F", "B", "SM", "STC", "TC", "TS", "CC", "CS"], StringComparer.Ordinal);
    private static readonly HashSet<string> KeyenceBitDeviceKinds = new(["R", "B", "MR", "LR", "CR", "VB", "X", "Y", "M", "L"], StringComparer.Ordinal);

    private static readonly string[] DeviceKindPrefixes =
    [
        "STC",
        "SB", "SW", "ZR", "SM", "SD", "MR", "LR", "CR", "VB", "DM", "EM", "FM", "ZF",
        "TM", "TC", "TS", "CC", "CS", "CM", "AT", "VM",
        "X", "Y", "M", "D", "L", "B", "F", "W", "R", "E", "Z", "T", "C",
    ];

    public static PlcProject MakeProject(ProjectInput input, long? nowEpochMs = null)
    {
        ValidateChoice(input.Vendor, Vendors, "vendor");
        ValidateChoice(input.ConnectionMode, ConnectionModes, "connection mode");
        ValidateChoice(input.KeyenceDeviceMode, KeyenceDeviceModes, "Keyence device mode");
        ValidateChoice(input.TransportMode, TransportModes, "transport mode");
        ValidateChoice(input.BlockDisplayDensity, BlockDisplayDensities, "block display density");

        var now = nowEpochMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var name = string.IsNullOrWhiteSpace(input.Name) ? "PLC QR Project" : input.Name.Trim();
        var devices = new List<DeviceDefinition>();

        foreach (var line in ParseLines(input.DevicesText))
        {
            var parts = line.Split(',').Select(part => part.Trim()).ToArray();
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            var address = parts[0].ToUpperInvariant();
            var dataType = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
                ? parts[1]
                : GuessDataType(address, input.Vendor);
            ValidateChoice(dataType, DeviceDataTypes, "device data type");
            devices.Add(new DeviceDefinition(address, dataType));
        }

        var watchItems = ParseLines(input.WatchText)
            .Select(line => line.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (watchItems.Count > MaxTimeChartTargets)
        {
            throw new ArgumentException($"タイムチャートに追加できるのは最大 {MaxTimeChartTargets} チャンネルです。");
        }

        var traps = new List<TrapDefinition>();
        var offset = 1;
        foreach (var line in ParseLines(input.TrapsText))
        {
            var parts = line.Split(',').Select(part => part.Trim()).ToArray();
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                continue;
            }

            var address = parts[0].ToUpperInvariant();
            var condition = ValidateTrapConditionForAddress(address, parts[1], input.Vendor);
            double? threshold = null;
            if (TrapConditionRequiresThreshold(condition))
            {
                if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[2]))
                {
                    throw new ArgumentException($"Trap threshold is required for {condition}: {address}");
                }

                threshold = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            }

            var enabled = true;
            if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]))
            {
                enabled = parts[3].ToLowerInvariant() is not ("0" or "false" or "off" or "no");
            }

            traps.Add(new TrapDefinition(
                Id: $"trap-{offset}",
                Address: address,
                Enabled: enabled,
                Condition: condition,
                Threshold: threshold,
                TriggerCount: 0,
                LastTriggeredAtEpochMs: null,
                LastObservedValue: null));
            offset++;
        }

        return new PlcProject(
            Id: $"{Slugify(name)}-{now}",
            Name: name,
            Connection: new PlcConnection(
                input.Vendor,
                input.ConnectionMode,
                input.Host.Trim(),
                input.Port,
                input.MonitorIntervalMs,
                input.TimeoutMs,
                input.MachineLabel,
                input.KeyenceDeviceMode,
                input.TransportMode,
                input.Network,
                input.Station,
                input.ModuleIo,
                input.Multidrop),
            Devices: devices,
            WatchItems: watchItems,
            Traps: traps,
            Settings: new AppSettings(input.BlockDisplayDensity),
            UpdatedAtEpochMs: now);
    }

    public static string Slugify(string text, string fallback = "plc-project")
    {
        var slug = SlugRegex().Replace(text.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? fallback : slug;
    }

    public static string GuessDataType(string address) => GuessDataType(address, "Melsec");

    public static string GuessDataType(string address, string vendor) =>
        IsBitAddress(address, vendor) ? "Bit" : "Int16";

    public static bool IsBitAddress(string address, string vendor)
    {
        var kind = GuessDeviceKind(address);
        return vendor == "Keyence"
            ? KeyenceBitDeviceKinds.Contains(kind)
            : MelsecBitDeviceKinds.Contains(kind);
    }

    public static IReadOnlyList<string> TrapConditionsForAddress(string address, string vendor)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return TrapConditions;
        }

        return IsBitAddress(address, vendor) ? BitTrapConditions : WordTrapConditions;
    }

    public static string DefaultTrapConditionForAddress(string address, string vendor)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return "Change";
        }

        return IsBitAddress(address, vendor) ? "Rise" : "GreaterOrEqual";
    }

    public static string CoerceTrapConditionForAddress(string address, string condition, string vendor)
    {
        var value = string.IsNullOrWhiteSpace(condition) ? DefaultTrapConditionForAddress(address, vendor) : ValidateTrapCondition(condition);
        return TrapConditionsForAddress(address, vendor).Contains(value, StringComparer.Ordinal)
            ? value
            : DefaultTrapConditionForAddress(address, vendor);
    }

    public static string ValidateTrapConditionForAddress(string address, string condition, string vendor)
    {
        var value = ValidateTrapCondition(condition);
        var allowed = TrapConditionsForAddress(address, vendor);
        if (allowed.Contains(value, StringComparer.Ordinal))
        {
            return value;
        }

        throw new ArgumentException($"Invalid trap condition for {address}: {value}. Use one of: {string.Join(", ", allowed)}");
    }

    public static bool TrapConditionRequiresThreshold(string condition) => ValidateTrapCondition(condition) switch
    {
        "GreaterOrEqual" or "LessOrEqual" or "Equal" or "NotEqual" => true,
        _ => false,
    };

    private static string GuessDeviceKind(string address)
    {
        var normalized = address.Trim().ToUpperInvariant();
        return DeviceKindPrefixes.FirstOrDefault(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal)) ?? "";
    }

    public static string ValidateTrapCondition(string text)
    {
        var value = text.Trim();
        if (ValidTrapConditions.Contains(value))
        {
            return value;
        }

        throw new ArgumentException($"Invalid trap condition: {value}. Use one of: {string.Join(", ", TrapConditions)}");
    }

    private static void ValidateChoice(string value, IReadOnlyCollection<string> allowed, string name)
    {
        if (allowed.Contains(value, StringComparer.Ordinal))
        {
            return;
        }

        throw new ArgumentException($"Invalid {name}: {value}. Use one of: {string.Join(", ", allowed)}");
    }

    private static IEnumerable<string> ParseLines(string text) =>
        text.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'));

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugRegex();
}
