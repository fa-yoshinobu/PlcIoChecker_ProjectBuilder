using System.Globalization;
using System.Text.RegularExpressions;

namespace PlcIoCheckerQr.Core;

public sealed record PlcProject(
    string Id,
    string Name,
    PlcConnection Connection,
    IReadOnlyList<DeviceDefinition> Devices,
    IReadOnlyList<MonitorTargetDefinition> TimeChart,
    IReadOnlyList<TrapDefinition> Traps,
    IReadOnlyList<DeviceCommentDefinition> Comments,
    long UpdatedAtEpochMs)
{
    public IReadOnlyList<string> TimeChartAddresses { get; } = TimeChart.Select(target => target.Address).ToArray();
}

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
    string ModuleIo);

public sealed record DeviceDefinition(string Address, string DataType, string Comment = "");

public sealed record DeviceCommentDefinition(string Address, string DataType, string Comment);

public sealed record MonitorTargetDefinition(string Address, string DataType);

public sealed record TrapDefinition(
    string Id,
    string Address,
    string DataType,
    bool Enabled,
    string Condition,
    double? Threshold,
    int TriggerCount,
    long? LastTriggeredAtEpochMs,
    string? LastObservedValue);

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
    string ModuleIo,
    string DevicesText,
    string WatchText,
    string TrapsText,
    string CommentsText = "",
    string ProjectId = "",
    IReadOnlyList<string>? TrapIds = null);

public static partial class ProjectFactory
{
    public const int MaxDevices = 1_000;
    public const int MaxDeviceMeta = 1_040;
    public const int MaxTimeChartTargets = 20;
    public const int MaxTrapDefinitions = 20;
    public const int MinPollingIntervalMs = 100;
    public const int MaxPollingIntervalMs = 10_000;
    public const int MinTimeoutMs = 250;
    public const int MaxTimeoutMs = 10_000;
    public const int MaxProjectJsonBytes = 5 * 1024 * 1024;
    public const int MaxQrCompressedBytes = 1 * 1024 * 1024;
    public const int MaxQrChunks = 4_096;

    public static readonly string[] Vendors = ["Melsec", "Keyence"];
    public static readonly string[] ConnectionModes = ["Real", "DemoMock"];
    public static readonly string[] TransportModes = ["Tcp", "Udp"];

    public static readonly string[] ModuleIoTargets =
    [
        "OwnStation",
        "ControlSystemCpu",
        "StandbySystemCpu",
        "SystemACpu",
        "SystemBCpu",
        "MultipleCpu1",
        "MultipleCpu2",
        "MultipleCpu3",
        "MultipleCpu4",
        "RemoteHead1",
        "RemoteHead2",
        "ControlSystemRemoteHead",
        "StandbySystemRemoteHead",
    ];
    public static readonly string[] DeviceDataTypes = ["Bit", "Int16", "UInt16", "Int32", "UInt32", "Float32"];

    public static readonly string[] BitTrapConditions = ["Rise", "Fall", "Change"];
    public static readonly string[] WordTrapConditions = ["Change", "GreaterOrEqual", "LessOrEqual", "Equal", "NotEqual"];
    public static readonly string[] TrapConditions = BitTrapConditions.Concat(WordTrapConditions).Distinct(StringComparer.Ordinal).ToArray();

    private static readonly HashSet<string> ValidTrapConditions = new(TrapConditions, StringComparer.Ordinal);

    private sealed record CpuModelOption(string DisplayLabel, string CanonicalLabel);

    // Keep these labels in sync with the communication libraries' PLC profile display names.
    // KEYENCE XYM is represented by the "-xym" PLC profile, not a separate UI setting.
    private static readonly CpuModelOption[] MelsecCpuModelOptions =
    [
        new("MELSEC iQ-R (built-in)", "melsec:iq-r"),
        new("MELSEC iQ-R (RJ71EN71)", "melsec:iq-r:rj71en71"),
        new("MELSEC iQ-F (built-in)", "melsec:iq-f"),
        new("MELSEC iQ-L (built-in)", "melsec:iq-l"),
        new("MELSEC MX-R (built-in)", "melsec:mx-r"),
        new("MELSEC MX-R (RJ71EN71)", "melsec:mx-r:rj71en71"),
        new("MELSEC MX-F (built-in)", "melsec:mx-f"),
        new("MELSEC QnUDV (built-in)", "melsec:qnudv"),
        new("MELSEC QnUDV (QJ71E71-100)", "melsec:qnudv:qj71e71-100"),
        new("MELSEC QnU (built-in)", "melsec:qnu"),
        new("MELSEC QnU (QJ71E71-100)", "melsec:qnu:qj71e71-100"),
        new("MELSEC-Q (QJ71E71-100)", "melsec:qcpu:qj71e71-100"),
        new("MELSEC-L (built-in)", "melsec:lcpu"),
        new("MELSEC-L (LJ71E71-100)", "melsec:lcpu:lj71e71-100"),
    ];

    private static readonly CpuModelOption[] KeyenceCpuModelOptions =
    [
        new("KEYENCE KV-NANO", "keyence:kv-nano"),
        new("KEYENCE KV-NANO (XYM)", "keyence:kv-nano-xym"),
        new("KEYENCE KV-3000", "keyence:kv-3000"),
        new("KEYENCE KV-3000 (XYM)", "keyence:kv-3000-xym"),
        new("KEYENCE KV-5000", "keyence:kv-5000"),
        new("KEYENCE KV-5000 (XYM)", "keyence:kv-5000-xym"),
        new("KEYENCE KV-7000", "keyence:kv-7000"),
        new("KEYENCE KV-7000 (XYM)", "keyence:kv-7000-xym"),
        new("KEYENCE KV-8000", "keyence:kv-8000"),
        new("KEYENCE KV-8000 (XYM)", "keyence:kv-8000-xym"),
        new("KEYENCE KV-X500", "keyence:kv-x500"),
        new("KEYENCE KV-X500 (XYM)", "keyence:kv-x500-xym"),
    ];

    public static readonly string[] MelsecCpuModels = MelsecCpuModelOptions.Select(option => option.DisplayLabel).ToArray();
    public static readonly string[] KeyenceCpuModels = KeyenceCpuModelOptions.Select(option => option.DisplayLabel).ToArray();

    public static string ToCanonicalMachineLabel(string vendor, string machineLabel)
    {
        var normalized = machineLabel.Trim();
        if (string.Equals(vendor, "Melsec", StringComparison.OrdinalIgnoreCase))
        {
            return ToCanonicalCpuModelLabel(MelsecCpuModelOptions, normalized, "MELSEC", machineLabel);
        }

        if (string.Equals(vendor, "Keyence", StringComparison.OrdinalIgnoreCase))
        {
            return ToCanonicalCpuModelLabel(KeyenceCpuModelOptions, normalized, "KEYENCE", machineLabel);
        }

        throw new ArgumentException($"Unsupported PLC vendor: {vendor}");
    }

    public static string ToDisplayMachineLabel(string vendor, string machineLabel)
    {
        var normalized = machineLabel.Trim();
        if (string.Equals(vendor, "Melsec", StringComparison.OrdinalIgnoreCase))
        {
            return ToDisplayCpuModelLabel(MelsecCpuModelOptions, normalized, "MELSEC", machineLabel);
        }

        if (string.Equals(vendor, "Keyence", StringComparison.OrdinalIgnoreCase))
        {
            return ToDisplayCpuModelLabel(KeyenceCpuModelOptions, normalized, "KEYENCE", machineLabel);
        }

        throw new ArgumentException($"Unsupported PLC vendor: {vendor}");
    }

    private static string ToCanonicalCpuModelLabel(
        IReadOnlyList<CpuModelOption> options,
        string normalized,
        string vendorName,
        string original)
    {
        var option = options.FirstOrDefault(candidate => candidate.DisplayLabel == normalized);
        return option?.CanonicalLabel
            ?? throw new ArgumentException($"Unsupported {vendorName} CPU model: {original}");
    }

    private static string ToDisplayCpuModelLabel(
        IReadOnlyList<CpuModelOption> options,
        string normalized,
        string vendorName,
        string original)
    {
        var option = options.FirstOrDefault(candidate => candidate.CanonicalLabel == normalized);
        return option?.DisplayLabel
            ?? throw new ArgumentException($"Unsupported {vendorName} CPU model: {original}");
    }

    public static string KeyenceDeviceModeForMachineLabel(string vendor, string machineLabel)
    {
        if (!string.Equals(vendor, "Keyence", StringComparison.OrdinalIgnoreCase))
        {
            return "Normal";
        }

        return ToCanonicalMachineLabel(vendor, machineLabel).EndsWith("-xym", StringComparison.Ordinal)
            ? "Xym"
            : "Normal";
    }

    private static readonly DeviceFamilyRule[] MelsecDeviceFamilies =
    [
        Bit("X", DeviceAddressNumberFormat.Hex),
        Bit("Y", DeviceAddressNumberFormat.Hex),
        Bit("M"),
        Word("D"),
        Bit("L"),
        Bit("F"),
        Bit("B", DeviceAddressNumberFormat.Hex),
        Bit("SB", DeviceAddressNumberFormat.Hex),
        Bit("SM"),
        Bit("STC"),
        Bit("TC"),
        Bit("CC"),
        Word("W", DeviceAddressNumberFormat.Hex),
        Word("SW", DeviceAddressNumberFormat.Hex),
        Word("R"),
        Word("ZR"),
        Word("SD"),
    ];

    private static readonly DeviceFamilyRule[] MelsecIqFDeviceFamilies =
    [
        Bit("X", DeviceAddressNumberFormat.Octal),
        Bit("Y", DeviceAddressNumberFormat.Octal),
        Bit("M"),
        Word("D"),
        Bit("L"),
        Bit("F"),
        Bit("B", DeviceAddressNumberFormat.Hex),
        Bit("SB", DeviceAddressNumberFormat.Hex),
        Bit("SM"),
        Bit("STC"),
        Bit("TC"),
        Bit("CC"),
        Word("W", DeviceAddressNumberFormat.Hex),
        Word("SW", DeviceAddressNumberFormat.Hex),
        Word("R"),
        Word("ZR"),
        Word("SD"),
    ];

    private static readonly DeviceFamilyRule[] KeyenceNormalDeviceFamilies =
    [
        Bit("R", DeviceAddressNumberFormat.KeyenceBitBank, maxNumber: 199915),
        Bit("B", DeviceAddressNumberFormat.Hex, maxNumber: 0x7FFF),
        Bit("MR", DeviceAddressNumberFormat.KeyenceBitBank, maxNumber: 399915),
        Bit("LR", DeviceAddressNumberFormat.KeyenceBitBank, maxNumber: 99915),
        Bit("CR", DeviceAddressNumberFormat.KeyenceBitBank, maxNumber: 7915),
        Word("DM", maxNumber: 65534),
        Word("EM", maxNumber: 65534),
        Word("FM", maxNumber: 32767),
        Word("ZF", maxNumber: 524287),
        Word("W", DeviceAddressNumberFormat.Hex, maxNumber: 0x7FFF),
        Word("TM", maxNumber: 511),
        Word("CM", maxNumber: 7599),
    ];

    private static readonly DeviceFamilyRule[] KeyenceXymDeviceFamilies =
    [
        Bit("B", DeviceAddressNumberFormat.Hex, maxNumber: 0x7FFF),
        Bit("CR", DeviceAddressNumberFormat.KeyenceBitBank, maxNumber: 7915),
        Word("ZF", maxNumber: 524287),
        Word("W", DeviceAddressNumberFormat.Hex, maxNumber: 0x7FFF),
        Word("TM", maxNumber: 511),
        Word("CM", maxNumber: 7599),
        Bit("X", DeviceAddressNumberFormat.KeyenceXymBit, maxNumber: 1999 * 16 + 15),
        Bit("Y", DeviceAddressNumberFormat.KeyenceXymBit, maxNumber: 1999 * 16 + 15),
        Bit("M", maxNumber: 63999),
        Bit("L", maxNumber: 15999),
        Word("D", maxNumber: 65534),
        Word("E", maxNumber: 65534),
        Word("F", maxNumber: 32767),
    ];

    public static PlcProject MakeProject(ProjectInput input, long? nowEpochMs = null)
    {
        ValidateChoice(input.Vendor, Vendors, "vendor");
        ValidateChoice(input.ConnectionMode, ConnectionModes, "connection mode");
        ValidateChoice(
            input.MachineLabel,
            input.Vendor == "Keyence" ? KeyenceCpuModels : MelsecCpuModels,
            "CPU model");
        ValidateChoice(input.TransportMode, TransportModes, "transport mode");
        ValidateChoice(input.ModuleIo, ModuleIoTargets, "module IO");
        if (string.IsNullOrWhiteSpace(input.Host))
        {
            throw new ArgumentException("Host is required.");
        }
        if (input.Port is < 1 or > 65_535)
        {
            throw new ArgumentException("Port must be between 1 and 65535.");
        }
        if (input.MonitorIntervalMs is < MinPollingIntervalMs or > MaxPollingIntervalMs)
        {
            throw new ArgumentException($"Polling interval must be between {MinPollingIntervalMs} and {MaxPollingIntervalMs} ms.");
        }
        if (input.TimeoutMs is < MinTimeoutMs or > MaxTimeoutMs)
        {
            throw new ArgumentException($"Timeout must be between {MinTimeoutMs} and {MaxTimeoutMs} ms.");
        }
        if (input.Network is < 0 or > 255 || input.Station is < 0 or > 255)
        {
            throw new ArgumentException("Network and station numbers must be between 0 and 255.");
        }

        var now = nowEpochMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var name = string.IsNullOrWhiteSpace(input.Name) ? "PLC QR Project" : input.Name.Trim();
        var keyenceDeviceMode = KeyenceDeviceModeForMachineLabel(input.Vendor, input.MachineLabel);

        var explicitDataTypesByAddress = BuildExplicitDataTypeIndex(
            input,
            input.Vendor,
            keyenceDeviceMode,
            input.MachineLabel);
        var devices = ParseDevices(input.DevicesText, input.Vendor, keyenceDeviceMode, input.MachineLabel, explicitDataTypesByAddress);
        var comments = MergeDeviceComments(
            devices,
            ParseComments(input.CommentsText, input.Vendor, keyenceDeviceMode, input.MachineLabel, explicitDataTypesByAddress));
        devices = ApplyDeviceComments(devices, comments);
        var deviceTypesByAddress = devices
            .GroupBy(device => device.Address, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().DataType, StringComparer.OrdinalIgnoreCase);

        var timeChart = ParseTimeChart(input.WatchText, input.Vendor, keyenceDeviceMode, input.MachineLabel, explicitDataTypesByAddress);
        var traps = ParseTraps(
            input.TrapsText,
            input.Vendor,
            keyenceDeviceMode,
            input.MachineLabel,
            explicitDataTypesByAddress,
            input.TrapIds);
        var (projectDevices, projectTimeChart, projectTraps) = CommonizeDeviceDataTypes(devices, timeChart, traps);
        var deviceMetaCount = projectDevices.Select(item => item.Address)
            .Concat(comments.Select(item => item.Address))
            .Concat(projectTimeChart.Select(item => item.Address))
            .Concat(projectTraps.Select(item => item.Address))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (deviceMetaCount > MaxDeviceMeta)
        {
            throw new ArgumentException($"Device metadata can contain up to {MaxDeviceMeta} addresses.");
        }

        return new PlcProject(
            Id: string.IsNullOrWhiteSpace(input.ProjectId) ? $"{Slugify(name)}-{now}" : input.ProjectId.Trim(),
            Name: name,
            Connection: BuildConnection(input, keyenceDeviceMode),
            Devices: projectDevices,
            TimeChart: projectTimeChart,
            Traps: projectTraps,
            Comments: comments,
            UpdatedAtEpochMs: now);
    }

    private static List<DeviceCommentDefinition> MergeDeviceComments(
        IEnumerable<DeviceDefinition> devices,
        IEnumerable<DeviceCommentDefinition> explicitComments)
    {
        var commentsByAddress = new Dictionary<string, DeviceCommentDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var comment in explicitComments)
        {
            if (!commentsByAddress.TryAdd(comment.Address, comment) &&
                string.IsNullOrWhiteSpace(commentsByAddress[comment.Address].Comment) &&
                !string.IsNullOrWhiteSpace(comment.Comment))
            {
                commentsByAddress[comment.Address] = comment;
            }
        }

        foreach (var device in devices.Where(device => !string.IsNullOrWhiteSpace(device.Comment)))
        {
            commentsByAddress.TryAdd(device.Address, new DeviceCommentDefinition(device.Address, device.DataType, device.Comment));
        }

        return commentsByAddress.Values.ToList();
    }

    private static List<DeviceDefinition> ApplyDeviceComments(
        IEnumerable<DeviceDefinition> devices,
        IReadOnlyCollection<DeviceCommentDefinition> comments)
    {
        var commentsByAddress = comments.ToDictionary(
            comment => comment.Address,
            comment => comment.Comment,
            StringComparer.OrdinalIgnoreCase);

        return devices
            .Select(device => device with { Comment = commentsByAddress.GetValueOrDefault(device.Address, "") })
            .ToList();
    }

    private static (
        List<DeviceDefinition> Devices,
        List<MonitorTargetDefinition> TimeChart,
        List<TrapDefinition> Traps) CommonizeDeviceDataTypes(
            List<DeviceDefinition> devices,
            List<MonitorTargetDefinition> timeChart,
            List<TrapDefinition> traps)
    {
        var dataTypesByAddress = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices)
        {
            dataTypesByAddress.TryAdd(device.Address, device.DataType);
        }

        foreach (var target in timeChart)
        {
            dataTypesByAddress.TryAdd(target.Address, target.DataType);
        }

        foreach (var trap in traps)
        {
            dataTypesByAddress.TryAdd(trap.Address, trap.DataType);
        }

        return (
            devices.Select(device => device with { DataType = dataTypesByAddress[device.Address] }).ToList(),
            timeChart.Select(target => target with { DataType = dataTypesByAddress[target.Address] }).ToList(),
            traps.Select(trap => trap with { DataType = dataTypesByAddress[trap.Address] }).ToList());
    }

    private static Dictionary<string, string> BuildExplicitDataTypeIndex(
        ProjectInput input,
        string vendor,
        string keyenceDeviceMode,
        string machineLabel)
    {
        var dataTypesByAddress = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Remember(string line, bool allowConditionInSecondColumn, string name)
        {
            var parts = line.Split(',').Select(part => part.Trim()).ToArray();
            if (parts.Length <= 1 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                return;
            }

            if (!TryNormalizeDeviceDataType(parts[1], out var dataType))
            {
                if (allowConditionInSecondColumn && ValidTrapConditions.Contains(parts[1]))
                {
                    return;
                }

                return;
            }

            var address = NormalizeDeviceAddress(parts[0], vendor, keyenceDeviceMode, machineLabel);
            dataType = ValidateDataTypeForAddress(address, dataType, vendor, keyenceDeviceMode, machineLabel, name);
            if (dataTypesByAddress.TryGetValue(address, out var existing) &&
                !string.Equals(existing, dataType, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Conflicting data type for {address}: {existing} vs {dataType}.");
            }

            dataTypesByAddress[address] = dataType;
        }

        foreach (var line in ParseLines(input.DevicesText))
        {
            Remember(line, allowConditionInSecondColumn: false, "device data type");
        }

        foreach (var line in ParseLines(input.CommentsText))
        {
            Remember(line, allowConditionInSecondColumn: false, "comment data type");
        }

        foreach (var line in ParseLines(input.WatchText))
        {
            Remember(line, allowConditionInSecondColumn: false, "time chart data type");
        }

        foreach (var line in ParseLines(input.TrapsText))
        {
            Remember(line, allowConditionInSecondColumn: true, "trap data type");
        }

        return dataTypesByAddress;
    }

    private static List<DeviceDefinition> ParseDevices(
        string devicesText,
        string vendor,
        string keyenceDeviceMode,
        string machineLabel,
        IReadOnlyDictionary<string, string> explicitDataTypesByAddress)
    {
        var parsedDevices = new List<DeviceDefinition>();
        foreach (var line in ParseLines(devicesText))
        {
            var parts = line.Split(',').Select(part => part.Trim()).ToArray();
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            var address = NormalizeDeviceAddress(parts[0], vendor, keyenceDeviceMode, machineLabel);
            var (dataType, hasExplicitDataType) = ResolveDeviceDataType(
                parts,
                1,
                address,
                vendor,
                keyenceDeviceMode,
                machineLabel,
                explicitDataTypesByAddress,
                "device data type");
            parsedDevices.Add(new DeviceDefinition(address, dataType, DeviceCommentFromParts(parts, hasExplicitDataType ? 2 : 1)));
            if (parsedDevices.Count > MaxDevices)
            {
                throw new ArgumentException($"Device list can contain up to {MaxDevices} rows.");
            }
        }

        var commentsByAddress = parsedDevices
            .Where(device => !string.IsNullOrWhiteSpace(device.Comment))
            .GroupBy(device => device.Address, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Comment, StringComparer.OrdinalIgnoreCase);
        return parsedDevices
            .Select(device => device with { Comment = commentsByAddress.GetValueOrDefault(device.Address, "") })
            .ToList();
    }

    private static List<DeviceCommentDefinition> ParseComments(
        string commentsText,
        string vendor,
        string keyenceDeviceMode,
        string machineLabel,
        IReadOnlyDictionary<string, string> explicitDataTypesByAddress)
    {
        var comments = new List<DeviceCommentDefinition>();
        foreach (var line in ParseLines(commentsText))
        {
            var parts = line.Split(',').Select(part => part.Trim()).ToArray();
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            var address = NormalizeDeviceAddress(parts[0], vendor, keyenceDeviceMode, machineLabel);
            var (dataType, hasExplicitDataType) = ResolveDeviceDataType(
                parts,
                1,
                address,
                vendor,
                keyenceDeviceMode,
                machineLabel,
                explicitDataTypesByAddress,
                "comment data type");
            var commentIndex = hasExplicitDataType ? 2 : 1;
            comments.Add(new DeviceCommentDefinition(address, dataType, DeviceCommentFromParts(parts, commentIndex)));
        }

        return comments;
    }

    private static List<MonitorTargetDefinition> ParseTimeChart(
        string watchText,
        string vendor,
        string keyenceDeviceMode,
        string machineLabel,
        IReadOnlyDictionary<string, string> explicitDataTypesByAddress)
    {
        var timeChart = ParseLines(watchText)
            .Select(line => ParseDeviceLine(line, vendor, keyenceDeviceMode, machineLabel, explicitDataTypesByAddress))
            .DistinctBy(target => target.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (timeChart.Count > MaxTimeChartTargets)
        {
            throw new ArgumentException($"Time chart can contain up to {MaxTimeChartTargets} channels.");
        }

        return timeChart;
    }

    private static List<TrapDefinition> ParseTraps(
        string trapsText,
        string vendor,
        string keyenceDeviceMode,
        string machineLabel,
        IReadOnlyDictionary<string, string> explicitDataTypesByAddress,
        IReadOnlyList<string>? configuredTrapIds)
    {
        var traps = new List<TrapDefinition>();
        var trapIds = new HashSet<string>(StringComparer.Ordinal);
        var offset = 1;
        foreach (var line in ParseLines(trapsText))
        {
            var parts = line.Split(',').Select(part => part.Trim()).ToArray();
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            var address = NormalizeDeviceAddress(parts[0], vendor, keyenceDeviceMode, machineLabel);
            var (dataType, hasExplicitDataType) = ResolveDeviceDataType(
                parts,
                1,
                address,
                vendor,
                keyenceDeviceMode,
                machineLabel,
                explicitDataTypesByAddress,
                "trap data type");
            var conditionIndex = hasExplicitDataType ? 2 : 1;
            if (parts.Length <= conditionIndex || string.IsNullOrWhiteSpace(parts[conditionIndex]))
            {
                throw new ArgumentException($"Trap condition is required for {address}.");
            }

            var condition = ValidateTrapConditionForAddress(address, parts[conditionIndex], vendor, keyenceDeviceMode);
            var thresholdIndex = conditionIndex + 1;
            var enabledIndex = conditionIndex + 2;
            double? threshold = null;
            if (TrapConditionRequiresThreshold(condition))
            {
                if (parts.Length <= thresholdIndex || string.IsNullOrWhiteSpace(parts[thresholdIndex]))
                {
                    throw new ArgumentException($"Trap threshold is required for {condition}: {address}");
                }

                threshold = double.Parse(parts[thresholdIndex], System.Globalization.CultureInfo.InvariantCulture);
                if (!IsTrapThresholdValid(threshold.Value, dataType))
                {
                    throw new ArgumentException($"Trap threshold is outside the {dataType} range: {address}");
                }
            }

            var enabled = true;
            if (parts.Length > enabledIndex && !string.IsNullOrWhiteSpace(parts[enabledIndex]))
            {
                enabled = parts[enabledIndex].ToLowerInvariant() is not ("0" or "false" or "off" or "no");
            }

            if (traps.Count >= MaxTrapDefinitions)
            {
                throw new ArgumentException($"Traps can contain up to {MaxTrapDefinitions} rows.");
            }

            var configuredTrapId = configuredTrapIds is not null && configuredTrapIds.Count >= offset
                ? configuredTrapIds[offset - 1].Trim()
                : "";
            var trapId = !string.IsNullOrWhiteSpace(configuredTrapId)
                ? configuredTrapId
                : $"trap-{offset}";
            if (!trapIds.Add(trapId))
            {
                throw new ArgumentException($"Duplicate trap ID: {trapId}");
            }

            traps.Add(new TrapDefinition(
                Id: trapId,
                Address: address,
                DataType: dataType,
                Enabled: enabled,
                Condition: condition,
                Threshold: threshold,
                TriggerCount: 0,
                LastTriggeredAtEpochMs: null,
                LastObservedValue: null));
            offset++;
        }

        return traps;
    }

    private static bool IsTrapThresholdValid(double value, string dataType)
    {
        if (!double.IsFinite(value))
        {
            return false;
        }

        return dataType switch
        {
            "Int16" => value is >= short.MinValue and <= short.MaxValue,
            "UInt16" => value is >= ushort.MinValue and <= ushort.MaxValue,
            "Int32" => value is >= int.MinValue and <= int.MaxValue,
            "UInt32" => value is >= uint.MinValue and <= uint.MaxValue,
            "Float32" => value is >= -float.MaxValue and <= float.MaxValue,
            _ => false,
        };
    }

    private static PlcConnection BuildConnection(ProjectInput input, string keyenceDeviceMode) =>
        new(
            input.Vendor,
            input.ConnectionMode,
            input.Host.Trim(),
            input.Port,
            input.MonitorIntervalMs,
            input.TimeoutMs,
            input.MachineLabel,
            keyenceDeviceMode,
            input.TransportMode,
            input.Network,
            input.Station,
            input.ModuleIo);

    private static MonitorTargetDefinition ParseDeviceLine(
        string line,
        string vendor,
        string keyenceDeviceMode,
        string machineLabel,
        IReadOnlyDictionary<string, string> explicitDataTypesByAddress)
    {
        var parts = line.Split(',').Select(part => part.Trim()).ToArray();
        var address = NormalizeDeviceAddress(parts[0], vendor, keyenceDeviceMode, machineLabel);
        var (dataType, _) = ResolveDeviceDataType(
            parts,
            1,
            address,
            vendor,
            keyenceDeviceMode,
            machineLabel,
            explicitDataTypesByAddress,
            "time chart data type");
        return new MonitorTargetDefinition(address, dataType);
    }

    private static (string DataType, bool HasExplicitDataType) ResolveDeviceDataType(
        IReadOnlyList<string> parts,
        int index,
        string address,
        string vendor,
        string keyenceDeviceMode,
        string? machineLabel,
        IReadOnlyDictionary<string, string> explicitDataTypesByAddress,
        string name)
    {
        if (parts.Count > index && TryNormalizeDeviceDataType(parts[index], out var parsedDataType))
        {
            return (
                ValidateDataTypeForAddress(address, parsedDataType, vendor, keyenceDeviceMode, machineLabel, name),
                true);
        }

        if (explicitDataTypesByAddress.TryGetValue(address, out var dataType))
        {
            return (
                ValidateDataTypeForAddress(address, dataType, vendor, keyenceDeviceMode, machineLabel, name),
                false);
        }

        var value = parts.Count > index ? parts[index].Trim() : "";
        if (!string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Invalid {name} for {address}: {value}. Use one of: {string.Join(", ", DeviceDataTypes)}");
        }

        throw new ArgumentException($"{name} is required for {address}.");
    }

    private static string ValidateDataTypeForAddress(
        string address,
        string dataType,
        string vendor,
        string keyenceDeviceMode,
        string? machineLabel,
        string name)
    {
        var allowed = DeviceDataTypesForAddress(address, vendor, keyenceDeviceMode, machineLabel);
        if (allowed.Contains(dataType, StringComparer.Ordinal))
        {
            return dataType;
        }

        throw new ArgumentException($"Invalid {name} for {address}: {dataType}. Use one of: {string.Join(", ", allowed)}");
    }

    private static bool TryNormalizeDeviceDataType(string text, out string dataType)
    {
        var value = text.Trim();
        dataType = DeviceDataTypes.FirstOrDefault(candidate => candidate.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? "";
        return dataType.Length > 0;
    }

    private static string DeviceCommentFromParts(IReadOnlyList<string> parts, int startIndex) =>
        startIndex >= parts.Count
            ? ""
            : NormalizeDeviceComment(string.Join(",", parts.Skip(startIndex)));

    private static string NormalizeDeviceComment(string text) =>
        ProjectCommentRules.Normalize(text);

    public static string Slugify(string text, string fallback = "plc-project")
    {
        var slug = SlugRegex().Replace(text.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? fallback : slug;
    }

    public static IReadOnlyList<string> DeviceDataTypesForAddress(string address, string vendor, string keyenceDeviceMode = "Normal", string? machineLabel = null)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return DeviceDataTypes;
        }

        return TryResolveDeviceFamily(address, vendor, keyenceDeviceMode, machineLabel, out var family)
            ? family.IsBit
                ? ["Bit"]
                : DeviceDataTypes.Where(dataType => dataType != "Bit").ToArray()
            : DeviceDataTypes;
    }

    public static bool IsBitAddress(string address, string vendor) => IsBitAddress(address, vendor, "Normal");

    public static bool IsBitAddress(string address, string vendor, string keyenceDeviceMode) =>
        IsBitAddress(address, vendor, keyenceDeviceMode, machineLabel: null);

    public static bool IsBitAddress(string address, string vendor, string keyenceDeviceMode, string? machineLabel)
    {
        return TryResolveDeviceFamily(address, vendor, keyenceDeviceMode, machineLabel, out var family)
            && family.IsBit;
    }

    public static IReadOnlyList<string> TrapConditionsForAddress(string address, string vendor) =>
        TrapConditionsForAddress(address, vendor, "Normal");

    public static IReadOnlyList<string> TrapConditionsForAddress(string address, string vendor, string keyenceDeviceMode)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return TrapConditions;
        }

        return IsBitAddress(address, vendor, keyenceDeviceMode) ? BitTrapConditions : WordTrapConditions;
    }

    public static string ValidateTrapConditionForAddress(string address, string condition, string vendor) =>
        ValidateTrapConditionForAddress(address, condition, vendor, "Normal");

    public static string ValidateTrapConditionForAddress(string address, string condition, string vendor, string keyenceDeviceMode)
    {
        var value = ValidateTrapCondition(condition);
        var allowed = TrapConditionsForAddress(address, vendor, keyenceDeviceMode);
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

    public static IReadOnlyList<string> SupportedDeviceNames(string vendor, string keyenceDeviceMode = "Normal", string? machineLabel = null) =>
        AllowedDeviceFamilyCodes(vendor, keyenceDeviceMode, machineLabel).ToArray();

    public static void ValidateDeviceAddress(string address, string vendor, string keyenceDeviceMode = "Normal", string? machineLabel = null)
    {
        if (TryResolveDeviceFamily(address, vendor, keyenceDeviceMode, machineLabel, out _))
        {
            return;
        }

        var context = vendor.Equals("Keyence", StringComparison.OrdinalIgnoreCase)
            ? $"KEYENCE {keyenceDeviceMode.ToUpperInvariant()}"
            : "MELSEC";
        throw new ArgumentException($"Unsupported device for {context}: {address}. Use one of: {string.Join(", ", AllowedDeviceFamilyCodes(vendor, keyenceDeviceMode, machineLabel))}");
    }

    public static string NormalizeDeviceAddress(string address, string vendor, string keyenceDeviceMode = "Normal", string? machineLabel = null)
    {
        if (TryResolveDeviceFamily(address, vendor, keyenceDeviceMode, machineLabel, out var family, out var parsedAddress))
        {
            return FormatDeviceAddress(family, parsedAddress.Number, width: 0);
        }

        ValidateDeviceAddress(address, vendor, keyenceDeviceMode, machineLabel);
        return address.Trim().ToUpperInvariant();
    }

    public static IReadOnlyList<string> BuildDeviceBlock(string startAddress, int count, string vendor, string keyenceDeviceMode = "Normal", string? machineLabel = null)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Point count must be greater than zero.");
        }

        if (!TryResolveDeviceFamily(startAddress, vendor, keyenceDeviceMode, machineLabel, out var family, out var parsedAddress))
        {
            ValidateDeviceAddress(startAddress, vendor, keyenceDeviceMode, machineLabel);
        }

        var startLogicalNumber = ToLogicalNumber(parsedAddress.Number, family.NumberFormat);
        var addresses = new List<string>(count);
        for (var offset = 0; offset < count; offset++)
        {
            var nextLogicalNumber = checked(startLogicalNumber + (uint)offset);
            var nextNumber = FromLogicalNumber(nextLogicalNumber, family.NumberFormat);
            if (nextNumber < family.MinNumber ||
                (family.MaxNumber is not null && nextNumber > family.MaxNumber.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, $"Device block exceeds supported range for {family.Code}.");
            }

            addresses.Add(FormatDeviceAddress(family, nextNumber, parsedAddress.Width));
        }

        return addresses;
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

    private static bool TryResolveDeviceFamily(string address, string vendor, string keyenceDeviceMode, out DeviceFamilyRule family) =>
        TryResolveDeviceFamily(address, vendor, keyenceDeviceMode, machineLabel: null, out family, out _);

    private static bool TryResolveDeviceFamily(string address, string vendor, string keyenceDeviceMode, string? machineLabel, out DeviceFamilyRule family) =>
        TryResolveDeviceFamily(address, vendor, keyenceDeviceMode, machineLabel, out family, out _);

    private static bool TryResolveDeviceFamily(
        string address,
        string vendor,
        string keyenceDeviceMode,
        string? machineLabel,
        out DeviceFamilyRule family,
        out DeviceAddressParse parsedAddress)
    {
        family = default;
        parsedAddress = default;
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var normalized = address.Trim().ToUpperInvariant();
        foreach (var candidate in DeviceFamiliesFor(vendor, keyenceDeviceMode, machineLabel).OrderByDescending(item => item.Code.Length))
        {
            if (!normalized.StartsWith(candidate.Code, StringComparison.OrdinalIgnoreCase) ||
                normalized.Length <= candidate.Code.Length)
            {
                continue;
            }

            var numberText = normalized[candidate.Code.Length..];
            if (TryParseAddressNumber(numberText, candidate.NumberFormat, out var number) &&
                number >= candidate.MinNumber &&
                (candidate.MaxNumber is null || number <= candidate.MaxNumber.Value))
            {
                family = candidate;
                parsedAddress = new DeviceAddressParse(number, numberText.Length);
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<DeviceFamilyRule> DeviceFamiliesFor(string vendor, string keyenceDeviceMode, string? machineLabel = null)
    {
        if (!vendor.Equals("Keyence", StringComparison.OrdinalIgnoreCase))
        {
            return ModelUsesOctalDirectIo(machineLabel) ? MelsecIqFDeviceFamilies : MelsecDeviceFamilies;
        }

        return keyenceDeviceMode.Equals("Xym", StringComparison.OrdinalIgnoreCase)
            ? KeyenceXymDeviceFamilies
            : KeyenceNormalDeviceFamilies;
    }

    private static IEnumerable<string> AllowedDeviceFamilyCodes(string vendor, string keyenceDeviceMode, string? machineLabel) =>
        DeviceFamiliesFor(vendor, keyenceDeviceMode, machineLabel).Select(family => family.Code);

    private static bool ModelUsesOctalDirectIo(string? machineLabel)
    {
        if (string.IsNullOrWhiteSpace(machineLabel))
        {
            return false;
        }

        return ToCanonicalMachineLabel("Melsec", machineLabel) == "melsec:iq-f";
    }

    private static bool TryParseAddressNumber(
        string numberText,
        DeviceAddressNumberFormat numberFormat,
        out uint number)
    {
        number = 0;
        return numberFormat switch
        {
            DeviceAddressNumberFormat.Hex => TryParseNumber(numberText, usesHexAddressing: true, out number),
            DeviceAddressNumberFormat.Octal => TryParseOctalNumber(numberText, out number),
            DeviceAddressNumberFormat.KeyenceBitBank => TryParseKeyenceBitBankNumber(numberText, out number),
            DeviceAddressNumberFormat.KeyenceXymBit => TryParseKeyenceXymBitNumber(numberText, out number),
            _ => TryParseNumber(numberText, usesHexAddressing: false, out number),
        };
    }

    private static bool TryParseOctalNumber(string numberText, out uint number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(numberText) ||
            !numberText.All(character => character is >= '0' and <= '7'))
        {
            return false;
        }

        try
        {
            number = Convert.ToUInt32(numberText, 8);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryParseKeyenceBitBankNumber(string numberText, out uint number) =>
        TryParseNumber(numberText, usesHexAddressing: false, out number) && number % 100 <= 15;

    private static bool TryParseKeyenceXymBitNumber(string numberText, out uint number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(numberText))
        {
            return false;
        }

        var bankText = numberText.Length == 1 ? "0" : numberText[..^1];
        var bitText = numberText[^1..];
        if (!bankText.All(character => character is >= '0' and <= '9') ||
            !uint.TryParse(bankText, NumberStyles.None, CultureInfo.InvariantCulture, out var bank) ||
            !uint.TryParse(bitText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bit) ||
            bit > 15)
        {
            return false;
        }

        try
        {
            number = checked(bank * 16 + bit);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryParseNumber(string numberText, bool usesHexAddressing, out uint number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(numberText))
        {
            return false;
        }

        var style = usesHexAddressing ? NumberStyles.HexNumber : NumberStyles.None;
        return numberText.All(character => IsNumberCharacter(character, usesHexAddressing)) &&
               uint.TryParse(numberText, style, CultureInfo.InvariantCulture, out number);
    }

    private static bool IsNumberCharacter(char character, bool usesHexAddressing) =>
        usesHexAddressing
            ? character is >= '0' and <= '9' or >= 'A' and <= 'F'
            : character is >= '0' and <= '9';

    private static uint ToLogicalNumber(uint physicalNumber, DeviceAddressNumberFormat numberFormat) =>
        numberFormat == DeviceAddressNumberFormat.KeyenceBitBank
            ? checked((physicalNumber / 100 * 16) + (physicalNumber % 100))
            : physicalNumber;

    private static uint FromLogicalNumber(uint logicalNumber, DeviceAddressNumberFormat numberFormat) =>
        numberFormat == DeviceAddressNumberFormat.KeyenceBitBank
            ? checked((logicalNumber / 16 * 100) + (logicalNumber % 16))
            : logicalNumber;

    private static string FormatDeviceAddress(DeviceFamilyRule family, uint number, int width) =>
        family.NumberFormat switch
        {
            DeviceAddressNumberFormat.Hex => $"{family.Code}{number.ToString($"X{width}", CultureInfo.InvariantCulture)}",
            DeviceAddressNumberFormat.Octal => $"{family.Code}{FormatOctalNumber(number, width)}",
            DeviceAddressNumberFormat.KeyenceBitBank => $"{family.Code}{FormatKeyenceBitBankNumber(number)}",
            DeviceAddressNumberFormat.KeyenceXymBit => $"{family.Code}{FormatKeyenceXymBitNumber(number)}",
            _ => $"{family.Code}{number.ToString($"D{width}", CultureInfo.InvariantCulture)}",
        };

    private static string FormatOctalNumber(uint number, int width) =>
        Convert.ToString((long)number, 8).ToUpperInvariant().PadLeft(width, '0');

    private static string FormatKeyenceBitBankNumber(uint physicalNumber)
    {
        var bank = physicalNumber / 100;
        var bit = physicalNumber % 100;
        return bank.ToString(CultureInfo.InvariantCulture) + bit.ToString("D2", CultureInfo.InvariantCulture);
    }

    private static string FormatKeyenceXymBitNumber(uint logicalNumber)
    {
        var bank = logicalNumber / 16;
        var bit = logicalNumber % 16;
        return bank == 0
            ? bit.ToString("X", CultureInfo.InvariantCulture)
            : bank.ToString(CultureInfo.InvariantCulture) + bit.ToString("X", CultureInfo.InvariantCulture);
    }

    private static DeviceFamilyRule Bit(
        string code,
        DeviceAddressNumberFormat numberFormat = DeviceAddressNumberFormat.Decimal,
        uint minNumber = 0,
        uint? maxNumber = null) =>
        new(code, true, numberFormat, minNumber, maxNumber);

    private static DeviceFamilyRule Word(
        string code,
        DeviceAddressNumberFormat numberFormat = DeviceAddressNumberFormat.Decimal,
        uint minNumber = 0,
        uint? maxNumber = null) =>
        new(code, false, numberFormat, minNumber, maxNumber);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugRegex();

    private enum DeviceAddressNumberFormat
    {
        Decimal,
        Octal,
        Hex,
        KeyenceBitBank,
        KeyenceXymBit,
    }

    private readonly record struct DeviceFamilyRule(
        string Code,
        bool IsBit,
        DeviceAddressNumberFormat NumberFormat,
        uint MinNumber,
        uint? MaxNumber);

    private readonly record struct DeviceAddressParse(uint Number, int Width);
}
