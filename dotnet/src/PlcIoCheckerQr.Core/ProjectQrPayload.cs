using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlcIoCheckerQr.Core;

public sealed record QrChunk(string Session, int Index, int Total, string Checksum, string Payload)
{
    public const string Prefix = "PLCIOC2D";

    public string Text => $"{Prefix}|{Session}|{Index}|{Total}|{Checksum}|{Payload}";
}

public static class ProjectQrPayload
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly JsonSerializerOptions MinifiedJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static byte[] ProjectJsonBytes(PlcProject project) =>
        JsonSerializer.SerializeToUtf8Bytes(ToJsonShape(project), PrettyJsonOptions);

    public static byte[] ProjectQrJsonBytes(PlcProject project) =>
        JsonSerializer.SerializeToUtf8Bytes(ToJsonShape(project), MinifiedJsonOptions);

    public static byte[] ProjectQrBytes(PlcProject project) => RawDeflate(ProjectQrJsonBytes(project));

    public static IReadOnlyList<QrChunk> EncodeProjectChunks(PlcProject project, int chunkSize)
    {
        var safeChunkSize = Math.Max(200, chunkSize);
        var data = ProjectQrJsonBytes(project);
        var compressedData = RawDeflate(data);
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

        if (chunkList.Any(chunk => chunk.Session != first.Session || chunk.Total != first.Total || chunk.Checksum != first.Checksum))
        {
            throw new ArgumentException("QR chunks do not belong to the same project");
        }

        var encoded = string.Concat(chunkList.Select(chunk => chunk.Payload));
        var data = RawInflate(Base64UrlDecode(encoded));
        if (!string.Equals(Sha256Hex(data), first.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("QR checksum mismatch");
        }

        return data;
    }

    public static QrChunk ParseChunkText(string text)
    {
        var parts = text.Trim().Split('|');
        if (parts.Length != 6 || parts[0] != QrChunk.Prefix)
        {
            throw new ArgumentException("Invalid project QR text");
        }

        var index = int.Parse(parts[2]);
        var total = int.Parse(parts[3]);
        if (index < 1 || index > total || total <= 0)
        {
            throw new ArgumentException("Invalid project QR index");
        }

        return new QrChunk(parts[1], index, total, parts[4].ToLowerInvariant(), parts[5]);
    }

    private static object ToJsonShape(PlcProject project) => new
    {
        id = project.Id,
        name = project.Name,
        connection = new
        {
            vendor = project.Connection.Vendor,
            connectionMode = project.Connection.ConnectionMode,
            host = project.Connection.Host,
            port = project.Connection.Port,
            monitorIntervalMs = project.Connection.MonitorIntervalMs,
            timeoutMs = project.Connection.TimeoutMs,
            machineLabel = project.Connection.MachineLabel,
            keyenceDeviceMode = project.Connection.KeyenceDeviceMode,
            transportMode = project.Connection.TransportMode,
            network = project.Connection.Network,
            station = project.Connection.Station,
            moduleIo = project.Connection.ModuleIo,
            multidrop = project.Connection.Multidrop,
        },
        devices = project.Devices.Select(device => new
        {
            address = device.Address,
            dataType = device.DataType,
        }),
        watchItems = project.WatchItems,
        traps = project.Traps.Select(trap => new
        {
            id = trap.Id,
            address = trap.Address,
            enabled = trap.Enabled,
            condition = trap.Condition,
            threshold = trap.Threshold,
            triggerCount = trap.TriggerCount,
            lastTriggeredAtEpochMs = trap.LastTriggeredAtEpochMs,
            lastObservedValue = trap.LastObservedValue,
        }),
        settings = new
        {
            blockDisplayDensity = project.Settings.BlockDisplayDensity,
        },
        updatedAtEpochMs = project.UpdatedAtEpochMs,
    };

    private static byte[] RawDeflate(byte[] data)
    {
        using var output = new MemoryStream();
        // DeflateStream emits raw deflate bytes on modern .NET. Android reads this with Inflater(true).
        using (var stream = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            stream.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    private static byte[] RawInflate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var stream = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

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
