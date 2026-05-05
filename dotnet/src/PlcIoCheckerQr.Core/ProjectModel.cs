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
    int ModuleIo,
    int Multidrop);

public sealed record DeviceDefinition(string Address, string DataType);

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
    int ModuleIo,
    int Multidrop,
    string DevicesText,
    string WatchText,
    string TrapsText);

public static partial class ProjectFactory
{
    public const int MaxTimeChartTargets = 20;

    public static readonly string[] Vendors = ["Melsec", "Keyence"];
    public static readonly string[] ConnectionModes = ["Real", "DemoMock"];
    public static readonly string[] KeyenceDeviceModes = ["Normal", "Xym"];
    public static readonly string[] TransportModes = ["Tcp", "Udp"];
    public static readonly string[] DeviceDataTypes = ["Bit", "Int16", "UInt16", "Int32", "UInt32", "Float32"];
    public static readonly string[] MelsecCpuModels = ["iQ-R", "iQ-F", "iQ-L", "MX-R", "MX-F", "QnUDV", "QnU", "QCPU", "LCPU"];
    public static readonly string[] KeyenceCpuModels = ["KV-X500", "KV-8000", "KV-7000", "KV-5000"];

    public static readonly string[] BitTrapConditions = ["Rise", "Fall", "Change"];
    public static readonly string[] WordTrapConditions = ["Change", "GreaterOrEqual", "LessOrEqual", "Equal", "NotEqual"];
    public static readonly string[] TrapConditions = BitTrapConditions.Concat(WordTrapConditions).Distinct(StringComparer.Ordinal).ToArray();

    private static readonly HashSet<string> ValidTrapConditions = new(TrapConditions, StringComparer.Ordinal);
    private static readonly DeviceFamilyRule[] MelsecDeviceFamilies =
    [
        Bit("X", DeviceAddressNumberFormat.Hex),
        Bit("Y", DeviceAddressNumberFormat.Hex),
        Bit("M"),
        Bit("B", DeviceAddressNumberFormat.Hex),
        Bit("SB", DeviceAddressNumberFormat.Hex),
        Bit("F"),
        Bit("V"),
        Bit("L"),
        Bit("SM"),
        Word("D"),
        Word("W", DeviceAddressNumberFormat.Hex),
        Word("SW", DeviceAddressNumberFormat.Hex),
        Word("R"),
        Bit("TS"),
        Bit("TC"),
        Word("TN"),
        Bit("STS"),
        Bit("STC"),
        Word("STN"),
        Bit("CS"),
        Bit("CC"),
        Word("CN"),
        Bit("LTS"),
        Bit("LTC"),
        Word("LTN"),
        Bit("LSTS"),
        Bit("LSTC"),
        Word("LSTN"),
        Bit("LCS"),
        Bit("LCC"),
        Word("LCN"),
        Word("Z"),
        Word("LZ"),
        Word("ZR"),
        Word("RD"),
        Word("SD"),
    ];

    private static readonly DeviceFamilyRule[] KeyenceNormalDeviceFamilies =
    [
        Bit("R", DeviceAddressNumberFormat.KeyenceBitBank),
        Bit("B", DeviceAddressNumberFormat.Hex),
        Bit("MR", DeviceAddressNumberFormat.KeyenceBitBank),
        Bit("LR", DeviceAddressNumberFormat.KeyenceBitBank),
        Bit("CR", DeviceAddressNumberFormat.KeyenceBitBank),
        Word("DM"),
        Word("EM"),
        Word("FM"),
        Word("ZF"),
        Word("W", DeviceAddressNumberFormat.Hex),
        Word("TM"),
        Word("CM"),
    ];

    private static readonly DeviceFamilyRule[] KeyenceXymDeviceFamilies =
    [
        Bit("B", DeviceAddressNumberFormat.Hex),
        Bit("CR", DeviceAddressNumberFormat.KeyenceBitBank),
        Word("ZF"),
        Word("W", DeviceAddressNumberFormat.Hex),
        Word("TM"),
        Word("CM"),
        Bit("X", DeviceAddressNumberFormat.KeyenceXymBit),
        Bit("Y", DeviceAddressNumberFormat.KeyenceXymBit),
        Bit("M"),
        Bit("L"),
        Word("D"),
        Word("E"),
        Word("F"),
    ];

    public static PlcProject MakeProject(ProjectInput input, long? nowEpochMs = null)
    {
        ValidateChoice(input.Vendor, Vendors, "vendor");
        ValidateChoice(input.ConnectionMode, ConnectionModes, "connection mode");
        ValidateChoice(input.KeyenceDeviceMode, KeyenceDeviceModes, "Keyence device mode");
        ValidateChoice(input.TransportMode, TransportModes, "transport mode");

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
            ValidateDeviceAddress(address, input.Vendor, input.KeyenceDeviceMode);
            var dataType = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
                ? parts[1]
                : GuessDataType(address, input.Vendor, input.KeyenceDeviceMode);
            ValidateChoice(dataType, DeviceDataTypes, "device data type");
            dataType = ValidateDataTypeForAddress(address, dataType, input.Vendor, input.KeyenceDeviceMode, "device data type");
            devices.Add(new DeviceDefinition(address, dataType));
        }

        var deviceTypesByAddress = devices
            .GroupBy(device => device.Address, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().DataType, StringComparer.OrdinalIgnoreCase);

        var timeChart = ParseLines(input.WatchText)
            .Select(line => ParseDeviceLine(line, input.Vendor, input.KeyenceDeviceMode, deviceTypesByAddress))
            .DistinctBy(target => target.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (timeChart.Count > MaxTimeChartTargets)
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
            ValidateDeviceAddress(address, input.Vendor, input.KeyenceDeviceMode);
            var hasDataType = parts.Length >= 3 && DeviceDataTypes.Contains(parts[1], StringComparer.Ordinal);
            var dataType = hasDataType
                ? parts[1]
                : deviceTypesByAddress.GetValueOrDefault(address, GuessDataType(address, input.Vendor, input.KeyenceDeviceMode));
            ValidateChoice(dataType, DeviceDataTypes, "trap data type");
            dataType = ValidateDataTypeForAddress(address, dataType, input.Vendor, input.KeyenceDeviceMode, "trap data type");
            var condition = ValidateTrapConditionForAddress(address, hasDataType ? parts[2] : parts[1], input.Vendor, input.KeyenceDeviceMode);
            var thresholdIndex = hasDataType ? 3 : 2;
            var enabledIndex = hasDataType ? 4 : 3;
            double? threshold = null;
            if (TrapConditionRequiresThreshold(condition))
            {
                if (parts.Length <= thresholdIndex || string.IsNullOrWhiteSpace(parts[thresholdIndex]))
                {
                    throw new ArgumentException($"Trap threshold is required for {condition}: {address}");
                }

                threshold = double.Parse(parts[thresholdIndex], System.Globalization.CultureInfo.InvariantCulture);
            }

            var enabled = true;
            if (parts.Length > enabledIndex && !string.IsNullOrWhiteSpace(parts[enabledIndex]))
            {
                enabled = parts[enabledIndex].ToLowerInvariant() is not ("0" or "false" or "off" or "no");
            }

            traps.Add(new TrapDefinition(
                Id: $"trap-{offset}",
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
            TimeChart: timeChart,
            Traps: traps,
            UpdatedAtEpochMs: now);
    }

    private static MonitorTargetDefinition ParseDeviceLine(
        string line,
        string vendor,
        string keyenceDeviceMode,
        IReadOnlyDictionary<string, string> registeredDeviceTypes)
    {
        var parts = line.Split(',').Select(part => part.Trim()).ToArray();
        var address = parts[0].ToUpperInvariant();
        ValidateDeviceAddress(address, vendor, keyenceDeviceMode);
        var dataType = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
            ? parts[1]
            : registeredDeviceTypes.GetValueOrDefault(address, GuessDataType(address, vendor, keyenceDeviceMode));
        ValidateChoice(dataType, DeviceDataTypes, "time chart data type");
        dataType = ValidateDataTypeForAddress(address, dataType, vendor, keyenceDeviceMode, "time chart data type");
        return new MonitorTargetDefinition(address, dataType);
    }

    private static string ValidateDataTypeForAddress(
        string address,
        string dataType,
        string vendor,
        string keyenceDeviceMode,
        string name)
    {
        var allowed = DeviceDataTypesForAddress(address, vendor, keyenceDeviceMode);
        if (allowed.Contains(dataType, StringComparer.Ordinal))
        {
            return dataType;
        }

        throw new ArgumentException($"Invalid {name} for {address}: {dataType}. Use one of: {string.Join(", ", allowed)}");
    }

    public static string Slugify(string text, string fallback = "plc-project")
    {
        var slug = SlugRegex().Replace(text.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? fallback : slug;
    }

    public static string GuessDataType(string address) => GuessDataType(address, "Melsec", "Normal");

    public static string GuessDataType(string address, string vendor) => GuessDataType(address, vendor, "Normal");

    public static string GuessDataType(string address, string vendor, string keyenceDeviceMode) =>
        IsBitAddress(address, vendor, keyenceDeviceMode) ? "Bit" : "Int16";

    public static IReadOnlyList<string> DeviceDataTypesForAddress(string address, string vendor, string keyenceDeviceMode = "Normal")
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return DeviceDataTypes;
        }

        return TryResolveDeviceFamily(address, vendor, keyenceDeviceMode, out var family)
            ? family.IsBit
                ? ["Bit"]
                : DeviceDataTypes.Where(dataType => dataType != "Bit").ToArray()
            : DeviceDataTypes;
    }

    public static bool IsBitAddress(string address, string vendor) => IsBitAddress(address, vendor, "Normal");

    public static bool IsBitAddress(string address, string vendor, string keyenceDeviceMode)
    {
        return TryResolveDeviceFamily(address, vendor, keyenceDeviceMode, out var family)
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

    public static string DefaultTrapConditionForAddress(string address, string vendor) =>
        DefaultTrapConditionForAddress(address, vendor, "Normal");

    public static string DefaultTrapConditionForAddress(string address, string vendor, string keyenceDeviceMode)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return "Change";
        }

        return IsBitAddress(address, vendor, keyenceDeviceMode) ? "Rise" : "GreaterOrEqual";
    }

    public static string CoerceTrapConditionForAddress(string address, string condition, string vendor) =>
        CoerceTrapConditionForAddress(address, condition, vendor, "Normal");

    public static string CoerceTrapConditionForAddress(string address, string condition, string vendor, string keyenceDeviceMode)
    {
        var value = string.IsNullOrWhiteSpace(condition) ? DefaultTrapConditionForAddress(address, vendor, keyenceDeviceMode) : ValidateTrapCondition(condition);
        return TrapConditionsForAddress(address, vendor, keyenceDeviceMode).Contains(value, StringComparer.Ordinal)
            ? value
            : DefaultTrapConditionForAddress(address, vendor, keyenceDeviceMode);
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

    public static void ValidateDeviceAddress(string address, string vendor, string keyenceDeviceMode = "Normal")
    {
        if (TryResolveDeviceFamily(address, vendor, keyenceDeviceMode, out _))
        {
            return;
        }

        var context = vendor == "Keyence" ? $"Keyence {keyenceDeviceMode}" : vendor;
        throw new ArgumentException($"Unsupported device for {context}: {address}. Use one of: {string.Join(", ", AllowedDeviceFamilyCodes(vendor, keyenceDeviceMode))}");
    }

    public static IReadOnlyList<string> BuildDeviceBlock(string startAddress, int count, string vendor, string keyenceDeviceMode = "Normal")
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Point count must be greater than zero.");
        }

        if (!TryResolveDeviceFamily(startAddress, vendor, keyenceDeviceMode, out var family, out var parsedAddress))
        {
            ValidateDeviceAddress(startAddress, vendor, keyenceDeviceMode);
        }

        var startLogicalNumber = ToLogicalNumber(parsedAddress.Number, family.NumberFormat);
        var addresses = new List<string>(count);
        for (var offset = 0; offset < count; offset++)
        {
            var nextLogicalNumber = checked(startLogicalNumber + (uint)offset);
            var nextNumber = FromLogicalNumber(nextLogicalNumber, family.NumberFormat);
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
        TryResolveDeviceFamily(address, vendor, keyenceDeviceMode, out family, out _);

    private static bool TryResolveDeviceFamily(
        string address,
        string vendor,
        string keyenceDeviceMode,
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
        foreach (var candidate in DeviceFamiliesFor(vendor, keyenceDeviceMode).OrderByDescending(item => item.Code.Length))
        {
            if (!normalized.StartsWith(candidate.Code, StringComparison.OrdinalIgnoreCase) ||
                normalized.Length <= candidate.Code.Length)
            {
                continue;
            }

            var numberText = normalized[candidate.Code.Length..];
            if (TryParseAddressNumber(numberText, candidate.NumberFormat, out var number))
            {
                family = candidate;
                parsedAddress = new DeviceAddressParse(number, numberText.Length);
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<DeviceFamilyRule> DeviceFamiliesFor(string vendor, string keyenceDeviceMode)
    {
        if (!vendor.Equals("Keyence", StringComparison.OrdinalIgnoreCase))
        {
            return MelsecDeviceFamilies;
        }

        return keyenceDeviceMode.Equals("Xym", StringComparison.OrdinalIgnoreCase)
            ? KeyenceXymDeviceFamilies
            : KeyenceNormalDeviceFamilies;
    }

    private static IEnumerable<string> AllowedDeviceFamilyCodes(string vendor, string keyenceDeviceMode) =>
        DeviceFamiliesFor(vendor, keyenceDeviceMode).Select(family => family.Code);

    private static bool TryParseAddressNumber(
        string numberText,
        DeviceAddressNumberFormat numberFormat,
        out uint number)
    {
        number = 0;
        return numberFormat switch
        {
            DeviceAddressNumberFormat.Hex => TryParseNumber(numberText, usesHexAddressing: true, out number),
            DeviceAddressNumberFormat.KeyenceBitBank => TryParseKeyenceBitBankNumber(numberText, out number),
            DeviceAddressNumberFormat.KeyenceXymBit => TryParseKeyenceXymBitNumber(numberText, out number),
            _ => TryParseNumber(numberText, usesHexAddressing: false, out number),
        };
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

        number = checked(bank * 16 + bit);
        return true;
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
            DeviceAddressNumberFormat.KeyenceBitBank => $"{family.Code}{FormatKeyenceBitBankNumber(number)}",
            DeviceAddressNumberFormat.KeyenceXymBit => $"{family.Code}{FormatKeyenceXymBitNumber(number)}",
            _ => $"{family.Code}{number.ToString($"D{width}", CultureInfo.InvariantCulture)}",
        };

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
        return bank.ToString(CultureInfo.InvariantCulture) + bit.ToString("X", CultureInfo.InvariantCulture);
    }

    private static DeviceFamilyRule Bit(string code, DeviceAddressNumberFormat numberFormat = DeviceAddressNumberFormat.Decimal) =>
        new(code, true, numberFormat);

    private static DeviceFamilyRule Word(string code, DeviceAddressNumberFormat numberFormat = DeviceAddressNumberFormat.Decimal) =>
        new(code, false, numberFormat);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugRegex();

    private enum DeviceAddressNumberFormat
    {
        Decimal,
        Hex,
        KeyenceBitBank,
        KeyenceXymBit,
    }

    private readonly record struct DeviceFamilyRule(string Code, bool IsBit, DeviceAddressNumberFormat NumberFormat);

    private readonly record struct DeviceAddressParse(uint Number, int Width);
}
