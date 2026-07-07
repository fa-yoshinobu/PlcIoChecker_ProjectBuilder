using System.Globalization;
using PlcIoCheckerQr.Core;
using PlcIoCheckerQr.Wpf.Localization;

namespace PlcIoCheckerQr.Wpf;

internal static class UiValueMapping
{
    internal static string TrapConditionDisplayText(string condition, LanguageCatalog language) => condition switch
    {
        "Rise" => language.Text("trap.rise"),
        "Fall" => language.Text("trap.fall"),
        "Change" => language.Text("trap.change"),
        "GreaterOrEqual" => ">=",
        "LessOrEqual" => "<=",
        "Equal" => "==",
        "NotEqual" => "!=",
        _ => condition,
    };

    internal static string ModuleIoDisplayText(string moduleIo, LanguageCatalog language) =>
        language.Text(moduleIo switch
        {
            "OwnStation" => "moduleIo.ownStation",
            "ControlSystemCpu" => "moduleIo.controlSystemCpu",
            "StandbySystemCpu" => "moduleIo.standbySystemCpu",
            "SystemACpu" => "moduleIo.systemACpu",
            "SystemBCpu" => "moduleIo.systemBCpu",
            "MultipleCpu1" => "moduleIo.multipleCpu1",
            "MultipleCpu2" => "moduleIo.multipleCpu2",
            "MultipleCpu3" => "moduleIo.multipleCpu3",
            "MultipleCpu4" => "moduleIo.multipleCpu4",
            "RemoteHead1" => "moduleIo.remoteHead1",
            "RemoteHead2" => "moduleIo.remoteHead2",
            "ControlSystemRemoteHead" => "moduleIo.controlSystemRemoteHead",
            "StandbySystemRemoteHead" => "moduleIo.standbySystemRemoteHead",
            _ => moduleIo,
        });

    internal static string ToUiVendor(string value) => value.Trim().ToUpperInvariant() switch
    {
        "MELSEC" => "Melsec",
        "KEYENCE" => "Keyence",
        _ => throw new InvalidOperationException($"Unsupported PLC vendor: {value}"),
    };

    internal static string ToUiConnectionMode(string value) => value.Trim().ToUpperInvariant() switch
    {
        "REAL" => "Real",
        "DEMO_MOCK" => "DemoMock",
        _ => throw new InvalidOperationException($"Unsupported connection mode: {value}"),
    };

    internal static string ToUiTransport(string value) => value.Trim().ToUpperInvariant() switch
    {
        "TCP" => "Tcp",
        "UDP" => "Udp",
        _ => throw new InvalidOperationException($"Unsupported transport: {value}"),
    };

    internal static string ToUiMachineLabel(string vendor, string value)
    {
        try
        {
            return ProjectFactory.ToDisplayMachineLabel(vendor, value);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    internal static string ToUiDataType(string value) => value.Trim().ToUpperInvariant() switch
    {
        "BIT" => "Bit",
        "INT16" => "Int16",
        "UINT16" => "UInt16",
        "INT32" => "Int32",
        "UINT32" => "UInt32",
        "FLOAT32" => "Float32",
        _ => throw new InvalidOperationException($"Unsupported device data type: {value}"),
    };

    internal static string ToUiTrapCondition(string value) => value.Trim().ToUpperInvariant() switch
    {
        "RISING_EDGE" => "Rise",
        "FALLING_EDGE" => "Fall",
        "CHANGE" => "Change",
        "GREATER_OR_EQUAL" => "GreaterOrEqual",
        "LESS_OR_EQUAL" => "LessOrEqual",
        "EQUAL" => "Equal",
        "NOT_EQUAL" => "NotEqual",
        _ => throw new InvalidOperationException($"Unsupported trap condition: {value}"),
    };

    internal static string DisplayVendor(string vendor) => vendor.Trim().ToUpperInvariant() switch
    {
        "MELSEC" => "MELSEC",
        "KEYENCE" => "KEYENCE",
        _ => vendor,
    };

    internal static string FormatPrefixedHex(int value, int width) =>
        $"0x{value.ToString($"X{width}", CultureInfo.InvariantCulture)}";
}
