using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZstdSharp;

namespace PlcIoCheckerQr.Core;

public sealed record QrChunk(
    string Session,
    int Index,
    int Total,
    string Checksum,
    string Payload)
{
    public const string Prefix = "PLCIOC3";
    public const string ZstdAlgorithm = "ZSTD";

    public string Text => $"{Prefix}|{ZstdAlgorithm}|{Session}|{Index}|{Total}|{Checksum}|{Payload}";
}

public static class ProjectQrPayload
{
    private const int ZstdCompressionLevel = 19;

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions MinifiedJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static byte[] ProjectJsonBytes(PlcProject project) =>
        JsonSerializer.SerializeToUtf8Bytes(ToJsonShape(project), PrettyJsonOptions);

    public static byte[] ProjectQrJsonBytes(PlcProject project) =>
        JsonSerializer.SerializeToUtf8Bytes(ToJsonShape(project), MinifiedJsonOptions);

    public static byte[] ProjectQrBytes(PlcProject project) =>
        ZstdCompress(ProjectQrJsonBytes(project));

    public static IReadOnlyList<QrChunk> EncodeProjectChunks(PlcProject project, int chunkSize)
    {
        var safeChunkSize = Math.Max(200, chunkSize);
        var data = ProjectQrJsonBytes(project);
        var compressedData = ZstdCompress(data);
        var checksum = Sha256Hex(data);
        var encoded = Base64UrlEncode(compressedData);
        var session = Guid.NewGuid().ToString("N")[..12];
        var payloads = ChunkString(encoded, safeChunkSize).DefaultIfEmpty(string.Empty).ToList();

        return payloads
            .Select((payload, index) => new QrChunk(session, index + 1, payloads.Count, checksum, payload))
            .ToList();
    }

    public static byte[] DecodeChunks(IEnumerable<QrChunk> chunks)
    {
        var chunkList = chunks.OrderBy(chunk => chunk.Index).ToList();
        if (chunkList.Count == 0)
        {
            throw new ArgumentException("No QR chunks");
        }

        var first = chunkList[0];
        if (chunkList.Count != first.Total)
        {
            throw new ArgumentException("Missing QR chunks");
        }

        if (chunkList.Any(chunk =>
                chunk.Session != first.Session ||
                chunk.Total != first.Total ||
                chunk.Checksum != first.Checksum))
        {
            throw new ArgumentException("QR chunks do not belong to the same project");
        }

        var encoded = string.Concat(chunkList.Select(chunk => chunk.Payload));
        var data = ZstdDecompress(Base64UrlDecode(encoded));
        if (!string.Equals(Sha256Hex(data), first.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("QR checksum mismatch");
        }

        return data;
    }

    public static QrChunk ParseChunkText(string text)
    {
        var parts = text.Trim().Split('|');
        if (parts.Length == 7 &&
            parts[0] == QrChunk.Prefix &&
            parts[1].Equals(QrChunk.ZstdAlgorithm, StringComparison.OrdinalIgnoreCase))
        {
            return ParseChunkParts(parts[2], parts[3], parts[4], parts[5], parts[6]);
        }

        throw new ArgumentException("Invalid project QR text");
    }

    private static QrChunk ParseChunkParts(
        string session,
        string indexText,
        string totalText,
        string checksum,
        string payload)
    {
        var index = int.Parse(indexText);
        var total = int.Parse(totalText);
        if (index < 1 || index > total || total <= 0)
        {
            throw new ArgumentException("Invalid project QR index");
        }

        return new QrChunk(session, index, total, checksum.ToLowerInvariant(), payload);
    }

    private static byte[] ZstdCompress(byte[] data)
    {
        using var compressor = new Compressor(ZstdCompressionLevel);
        return compressor.Wrap(data).ToArray();
    }

    private static byte[] ZstdDecompress(byte[] data)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(data).ToArray();
    }

    private static object ToJsonShape(PlcProject project) => new
    {
        schema = "plc-io-checker-project",
        schemaVersion = 2,
        projectId = project.Id,
        projectName = project.Name,
        plc = new
        {
            vendor = FormatVendor(project.Connection.Vendor),
            cpuModel = project.Connection.MachineLabel,
            connection = new
            {
                mode = FormatConnectionMode(project.Connection.ConnectionMode),
                host = project.Connection.Host,
                port = project.Connection.Port,
                transport = FormatTransport(project.Connection.TransportMode),
                pollingIntervalMs = project.Connection.MonitorIntervalMs,
                timeoutMs = project.Connection.TimeoutMs,
            },
            melsec = IsVendor(project.Connection.Vendor, "Melsec")
                ? new
                {
                    networkNo = project.Connection.Network,
                    stationNo = project.Connection.Station,
                    moduleIoNo = project.Connection.ModuleIo,
                    multidropNo = project.Connection.Multidrop,
                }
                : null,
            keyence = IsVendor(project.Connection.Vendor, "Keyence")
                ? new
                {
                    deviceMode = FormatKeyenceDeviceMode(project.Connection.KeyenceDeviceMode),
                }
                : null,
        },
        deviceList = project.Devices.Select(device => new
        {
            address = device.Address,
            dataType = FormatDataType(device.DataType),
            comment = string.IsNullOrWhiteSpace(device.Comment) ? null : device.Comment,
        }),
        timeChart = project.TimeChart.Select(target => new
        {
            address = target.Address,
            dataType = FormatDataType(target.DataType),
        }),
        traps = project.Traps.Select(trap => new
        {
            id = trap.Id,
            enabled = trap.Enabled,
            address = trap.Address,
            dataType = FormatDataType(trap.DataType),
            condition = FormatTrapCondition(trap.Condition),
            comparisonValue = trap.Threshold,
        }),
        updatedAtEpochMs = project.UpdatedAtEpochMs,
    };

    private static bool IsVendor(string value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static string FormatVendor(string value) => value switch
    {
        "Melsec" => "MELSEC",
        "Keyence" => "KEYENCE",
        _ => throw new ArgumentException($"Unsupported PLC vendor: {value}"),
    };

    private static string FormatConnectionMode(string value) => value switch
    {
        "Real" => "REAL",
        "DemoMock" => "DEMO_MOCK",
        _ => throw new ArgumentException($"Unsupported connection mode: {value}"),
    };

    private static string FormatKeyenceDeviceMode(string value) => value switch
    {
        "Normal" => "NORMAL",
        "Xym" => "XYM",
        _ => throw new ArgumentException($"Unsupported KEYENCE device mode: {value}"),
    };

    private static string FormatTransport(string value) => value switch
    {
        "Tcp" => "TCP",
        "Udp" => "UDP",
        _ => throw new ArgumentException($"Unsupported transport: {value}"),
    };

    private static string FormatDataType(string value) => value switch
    {
        "Bit" => "BIT",
        "Int16" => "INT16",
        "UInt16" => "UINT16",
        "Int32" => "INT32",
        "UInt32" => "UINT32",
        "Float32" => "FLOAT32",
        _ => throw new ArgumentException($"Unsupported device data type: {value}"),
    };

    private static string FormatTrapCondition(string value) => value switch
    {
        "Rise" => "RISING_EDGE",
        "Fall" => "FALLING_EDGE",
        "Change" => "CHANGE",
        "GreaterOrEqual" => "GREATER_OR_EQUAL",
        "LessOrEqual" => "LESS_OR_EQUAL",
        "Equal" => "EQUAL",
        "NotEqual" => "NOT_EQUAL",
        _ => throw new ArgumentException($"Unsupported trap condition: {value}"),
    };

    private static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private static IEnumerable<string> ChunkString(string text, int chunkSize)
    {
        for (var offset = 0; offset < text.Length; offset += chunkSize)
        {
            yield return text.Substring(offset, Math.Min(chunkSize, text.Length - offset));
        }
    }
}
