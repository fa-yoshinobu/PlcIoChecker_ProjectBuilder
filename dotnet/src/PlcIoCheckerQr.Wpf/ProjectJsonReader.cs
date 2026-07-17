using System.Globalization;
using System.Text.Json;

namespace PlcIoCheckerQr.Wpf;

internal static class ProjectJsonReader
{
    internal static void RequireProjectJsonV2(JsonElement root)
    {
        var schema = ReadRequiredString(root, "schema");
        if (schema != "plc-io-checker-project")
        {
            throw new ProjectJsonException("error.jsonSchemaInvalid", schema);
        }

        var version = ReadRequiredInt(root, "schemaVersion");
        if (version != 2)
        {
            throw new ProjectJsonException("error.jsonVersionInvalid", version.ToString(CultureInfo.InvariantCulture));
        }
    }

    internal static void RequireOnlyProperties(JsonElement element, string path, params string[] allowedNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Project JSON value '{path}' must be an object.");
        }

        var allowed = new HashSet<string>(allowedNames, StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new InvalidOperationException($"Unknown project JSON value '{path}.{property.Name}'.");
            }
        }
    }

    internal static void RequireObjectArrayProperties(JsonElement array, string path, params string[] allowedNames)
    {
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            RequireOnlyProperties(item, $"{path}[{index}]", allowedNames);
            index++;
        }
    }

    internal static JsonElement ReadRequiredObject(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Project JSON value '{name}' must be an object.");
        }

        return value;
    }

    internal static JsonElement ReadRequiredArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Project JSON value '{name}' must be an array.");
        }

        return value;
    }

    internal static string ReadOptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return "";
        }

        if (value.ValueKind == JsonValueKind.String &&
            value.GetString() is { } result)
        {
            return result;
        }

        throw new InvalidOperationException($"Project JSON value '{name}' must be a string.");
    }

    internal static string ReadRequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            value.GetString() is not { } result)
        {
            throw new InvalidOperationException($"Project JSON value '{name}' must be a string.");
        }

        return result;
    }

    internal static int ReadRequiredInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result))
        {
            throw new InvalidOperationException($"Project JSON value '{name}' must be an integer.");
        }

        return result;
    }

    internal static int ReadRequiredHexInt(JsonElement element, string name, int min, int max)
    {
        var text = ReadRequiredString(element, name).Trim();
        if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result) ||
            result < min ||
            result > max)
        {
            throw new InvalidOperationException($"Project JSON value '{name}' must be a 0x-prefixed hexadecimal string.");
        }

        return result;
    }

    internal static long ReadRequiredInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var result))
        {
            throw new InvalidOperationException($"Project JSON value '{name}' must be an integer.");
        }

        return result;
    }

    internal static bool ReadRequiredBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidOperationException($"Project JSON value '{name}' must be a boolean.");
        }

        return value.GetBoolean();
    }
}

internal sealed class ProjectJsonException(string localizationKey, string detail)
    : InvalidOperationException(detail)
{
    internal string LocalizationKey { get; } = localizationKey;
}
