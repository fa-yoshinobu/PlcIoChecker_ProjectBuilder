using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using ZstdSharp;

namespace PlcIoCheckerQr.Core;

public sealed record QrChunk(
    string Session,
    int Index,
    int Total,
    string Checksum,
    string Payload)
{
    public const string Prefix = "PLCIOC1";
    public const string ZstdAlgorithm = "ZSTD";

    public string Text => $"{Prefix}|{ZstdAlgorithm}|{Session}|{Index}|{Total}|{Checksum}|{Payload}";
}

public static class ProjectQrPayload
{
    private const int ZstdCompressionLevel = 19;
    private const int MaxQrEncodedCharacters = ((ProjectFactory.MaxQrCompressedBytes + 2) / 3) * 4;

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
        RequireProjectJsonSize(JsonSerializer.SerializeToUtf8Bytes(ToJsonShape(project), PrettyJsonOptions));

    public static byte[] ProjectQrJsonBytes(PlcProject project) =>
        RequireProjectJsonSize(JsonSerializer.SerializeToUtf8Bytes(ToJsonShape(project), MinifiedJsonOptions));

    public static byte[] ProjectQrBytes(PlcProject project)
    {
        var data = ZstdCompress(ProjectQrJsonBytes(project));
        RequireQrCompressedSize(data);
        return data;
    }

    public static IReadOnlyList<QrChunk> EncodeProjectChunks(PlcProject project, int chunkSize)
    {
        var safeChunkSize = Math.Max(200, chunkSize);
        var data = ProjectQrJsonBytes(project);
        var compressedData = ZstdCompress(data);
        RequireQrCompressedSize(compressedData);
        var checksum = Sha256Hex(data);
        var encoded = Base64UrlEncode(compressedData);
        var session = Guid.NewGuid().ToString("N")[..12];
        var payloads = ChunkString(encoded, safeChunkSize).DefaultIfEmpty(string.Empty).ToList();
        if (payloads.Count > ProjectFactory.MaxQrChunks)
        {
            throw new ArgumentException($"Project QR can contain up to {ProjectFactory.MaxQrChunks} chunks.");
        }

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
        if (first.Total is < 1 or > ProjectFactory.MaxQrChunks ||
            chunkList.Count != first.Total ||
            chunkList.Select(chunk => chunk.Index).Distinct().Count() != first.Total ||
            !chunkList.Select(chunk => chunk.Index).SequenceEqual(Enumerable.Range(1, first.Total)))
        {
            throw new ArgumentException("Missing QR chunks");
        }
        if (chunkList.Sum(chunk => (long)chunk.Payload.Length) > MaxQrEncodedCharacters ||
            chunkList.Any(chunk =>
                string.IsNullOrWhiteSpace(chunk.Session) ||
                chunk.Session.Length > 128 ||
                chunk.Checksum.Length != 64 ||
                chunk.Checksum.Any(character => !Uri.IsHexDigit(character)) ||
                string.IsNullOrEmpty(chunk.Payload) ||
                chunk.Payload.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))))
        {
            throw new ArgumentException("Invalid project QR chunk");
        }

        if (chunkList.Any(chunk =>
                chunk.Session != first.Session ||
                chunk.Total != first.Total ||
                chunk.Checksum != first.Checksum))
        {
            throw new ArgumentException("QR chunks do not belong to the same project");
        }

        var encoded = string.Concat(chunkList.Select(chunk => chunk.Payload));
        var compressedData = Base64UrlDecode(encoded);
        RequireQrCompressedSize(compressedData);
        var data = ZstdDecompress(compressedData);
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
        if (index < 1 || index > total || total <= 0 || total > ProjectFactory.MaxQrChunks)
        {
            throw new ArgumentException("Invalid project QR index");
        }
        if (string.IsNullOrWhiteSpace(session) || session.Length > 128)
        {
            throw new ArgumentException("Invalid project QR session");
        }
        if (checksum.Length != 64 || checksum.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Invalid project QR checksum");
        }
        if (string.IsNullOrEmpty(payload) ||
            payload.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException("Invalid project QR payload");
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
        using var input = new MemoryStream(data, writable: false);
        using var decompressor = new DecompressionStream(input);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = decompressor.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > ProjectFactory.MaxProjectJsonBytes)
            {
                throw new ArgumentException($"Project JSON exceeds {ProjectFactory.MaxProjectJsonBytes} bytes.");
            }
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static byte[] RequireProjectJsonSize(byte[] data)
    {
        if (data.Length > ProjectFactory.MaxProjectJsonBytes)
        {
            throw new ArgumentException($"Project JSON exceeds {ProjectFactory.MaxProjectJsonBytes} bytes.");
        }
        return data;
    }

    private static void RequireQrCompressedSize(byte[] data)
    {
        if (data.Length > ProjectFactory.MaxQrCompressedBytes)
        {
            throw new ArgumentException($"Compressed project QR exceeds {ProjectFactory.MaxQrCompressedBytes} bytes.");
        }
    }

    private static object ToJsonShape(PlcProject project)
    {
        var deviceMeta = ProjectDeviceMeta(project);
        return new
    {
        schema = "plc-io-checker-project",
        schemaVersion = 2,
        exportInfo = new
        {
            source = "PROJECT_BUILDER",
            version = ExporterVersion(),
        },
        projectId = project.Id,
        projectName = project.Name,
        plc = new
        {
            vendor = FormatVendor(project.Connection.Vendor),
            cpuModel = ProjectFactory.ToCanonicalMachineLabel(project.Connection.Vendor, project.Connection.MachineLabel),
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
                    moduleIo = project.Connection.ModuleIo,
                }
                : null,
            keyence = (object?)null,
        },
        deviceList = project.Devices.Select(device => new
        {
            address = device.Address,
        }),
        timeChart = project.TimeChart.Select(target => new
        {
            address = target.Address,
        }),
        deviceMeta = deviceMeta.Select(meta => new
        {
            address = meta.Address,
            dataType = FormatDataType(meta.DataType),
            comment = string.IsNullOrWhiteSpace(meta.Comment) ? null : meta.Comment,
        }),
        traps = project.Traps.Select(trap => new
        {
            id = trap.Id,
            enabled = trap.Enabled,
            address = trap.Address,
            condition = FormatTrapCondition(trap.Condition),
            comparisonValue = trap.Threshold,
        }),
        updatedAtEpochMs = project.UpdatedAtEpochMs,
    };
    }

    private sealed record DeviceMeta(string Address, string DataType, string Comment);

    private static IReadOnlyList<DeviceMeta> ProjectDeviceMeta(PlcProject project)
    {
        var indexByAddress = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DeviceMeta>();
        var commentsByAddress = project.Comments.ToDictionary(
            comment => comment.Address,
            comment => comment,
            StringComparer.OrdinalIgnoreCase);

        void AddOrFillComment(string address, string dataType, string comment = "")
        {
            var resolvedComment = commentsByAddress.TryGetValue(address, out var configuredComment)
                ? configuredComment.Comment
                : comment;
            if (indexByAddress.TryGetValue(address, out var index))
            {
                var existing = result[index];
                if (string.IsNullOrWhiteSpace(existing.Comment) && !string.IsNullOrWhiteSpace(resolvedComment))
                {
                    result[index] = existing with { Comment = resolvedComment };
                }
                return;
            }

            indexByAddress[address] = result.Count;
            result.Add(new DeviceMeta(address, dataType, resolvedComment));
        }

        foreach (var device in project.Devices)
        {
            AddOrFillComment(device.Address, device.DataType, device.Comment);
        }

        foreach (var target in project.TimeChart)
        {
            AddOrFillComment(target.Address, target.DataType);
        }

        foreach (var trap in project.Traps)
        {
            AddOrFillComment(trap.Address, trap.DataType);
        }

        foreach (var comment in project.Comments)
        {
            AddOrFillComment(comment.Address, comment.DataType, comment.Comment);
        }

        return result;
    }

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

    private static string ExporterVersion()
    {
        var assembly = typeof(ProjectQrPayload).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

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
