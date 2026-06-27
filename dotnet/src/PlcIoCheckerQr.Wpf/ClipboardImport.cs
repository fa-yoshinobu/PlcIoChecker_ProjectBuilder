using System.Text;
using PlcIoCheckerQr.Core;
using PlcIoCheckerQr.Wpf.Localization;

namespace PlcIoCheckerQr.Wpf;

internal static class ClipboardImport
{
    private static readonly string[] ImportAliasKeys =
    [
        "import.alias.boolean.true",
        "import.alias.header.address",
        "import.alias.header.comment",
        "import.alias.header.condition",
        "import.alias.header.dataType",
        "import.alias.header.watch",
        "import.alias.trap.change",
        "import.alias.trap.equal",
        "import.alias.trap.fall",
        "import.alias.trap.greaterOrEqual",
        "import.alias.trap.lessOrEqual",
        "import.alias.trap.notEqual",
        "import.alias.trap.rise",
    ];

    private static readonly (string Key, string Condition)[] TrapConditionAliasKeys =
    [
        ("import.alias.trap.rise", "Rise"),
        ("import.alias.trap.fall", "Fall"),
        ("import.alias.trap.change", "Change"),
        ("import.alias.trap.greaterOrEqual", "GreaterOrEqual"),
        ("import.alias.trap.lessOrEqual", "LessOrEqual"),
        ("import.alias.trap.equal", "Equal"),
        ("import.alias.trap.notEqual", "NotEqual"),
    ];

    private static readonly Lazy<IReadOnlyDictionary<string, string[]>> ImportAliases = new(LoadImportAliases);

    internal static IReadOnlyDictionary<string, string[]> LoadImportAliases()
    {
        var aliases = ImportAliasKeys.ToDictionary(
            key => key,
            _ => new List<string>(),
            StringComparer.Ordinal);

        foreach (var code in LanguageCatalog.Codes())
        {
            var catalog = LanguageCatalog.Load(code);
            foreach (var key in ImportAliasKeys)
            {
                var text = catalog.Text(key);
                if (text.Equals(key, StringComparison.Ordinal))
                {
                    continue;
                }

                aliases[key].AddRange(text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        return aliases.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            StringComparer.Ordinal);
    }

    internal static bool MatchesImportAlias(string text, string key)
    {
        var value = text.Trim();
        return ImportAliases.Value.TryGetValue(key, out var aliases) &&
               aliases.Any(alias => alias.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool MatchesNormalizedImportAlias(string normalizedText, string key) =>
        ImportAliases.Value.TryGetValue(key, out var aliases) &&
        aliases.Any(alias => NormalizeAlias(alias).Equals(normalizedText, StringComparison.OrdinalIgnoreCase));

    internal static string NormalizeAlias(string text) =>
        text.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();

    internal static string? ImportTrapConditionAlias(string normalizedText) =>
        TrapConditionAliasKeys
            .Where(item => MatchesNormalizedImportAlias(normalizedText, item.Key))
            .Select(item => item.Condition)
            .FirstOrDefault();

    internal static IReadOnlyList<string[]> SplitClipboardRows(string text)
    {
        var delimiter = text.Contains('\t', StringComparison.Ordinal) ? '\t' : ',';
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var atFieldStart = true;
        var justEndedRow = false;

        void AddField()
        {
            fields.Add(field.ToString());
            field.Clear();
            atFieldStart = true;
        }

        void AddRow()
        {
            AddField();
            rows.Add(fields.ToArray());
            fields.Clear();
            justEndedRow = true;
        }

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                justEndedRow = false;
                continue;
            }

            if (ch == '"' && atFieldStart)
            {
                inQuotes = true;
                atFieldStart = false;
                justEndedRow = false;
                continue;
            }

            if (ch == delimiter)
            {
                AddField();
                justEndedRow = false;
                continue;
            }

            if (ch is '\r' or '\n')
            {
                if (ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                AddRow();
                continue;
            }

            field.Append(ch);
            atFieldStart = false;
            justEndedRow = false;
        }

        if (!justEndedRow || field.Length > 0 || fields.Count > 0)
        {
            AddField();
            rows.Add(fields.ToArray());
        }

        return rows;
    }

    internal static string[] SplitClipboardLine(string line) =>
        line.Length == 0 ? [""] : SplitClipboardRows(line).FirstOrDefault() ?? [""];

    internal static bool IsDeviceClipboardHeader(IReadOnlyList<string> fields)
    {
        if (fields.Count == 0)
        {
            return false;
        }

        var first = fields[0].Trim();
        var second = fields.Count > 1 ? fields[1].Trim() : "";
        return MatchesImportAlias(first, "import.alias.header.address") ||
               MatchesImportAlias(second, "import.alias.header.dataType");
    }

    internal static bool IsCommentClipboardHeader(IReadOnlyList<string> fields)
    {
        if (fields.Count == 0)
        {
            return false;
        }

        var first = fields[0].Trim();
        var second = fields.Count > 1 ? fields[1].Trim() : "";
        return MatchesImportAlias(first, "import.alias.header.address") ||
               MatchesImportAlias(first, "import.alias.header.comment") ||
               MatchesImportAlias(second, "import.alias.header.comment");
    }

    internal static bool IsDeviceDataTypeField(string text) =>
        ProjectFactory.DeviceDataTypes.Any(dataType => dataType.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));

    internal static int FirstValueIndexAfterOptionalDataType(IReadOnlyList<string> fields, bool hasDataType) =>
        hasDataType || fields.Count > 2 && string.IsNullOrWhiteSpace(fields[1]) ? 2 : 1;

    internal static string DeviceCommentFromFields(IReadOnlyList<string> fields, int startIndex) =>
        startIndex >= fields.Count
            ? ""
            : NormalizeDeviceComment(string.Join(
                ",",
                fields.Skip(startIndex)
                    .Reverse()
                    .SkipWhile(string.IsNullOrWhiteSpace)
                    .Reverse()));

    internal static string NormalizeDeviceDataType(string text, string address, string vendor, string keyenceDeviceMode)
    {
        var value = text.Trim();
        return ProjectFactory.DeviceDataTypesForAddress(address, vendor, keyenceDeviceMode).FirstOrDefault(dataType =>
                   dataType.Equals(value, StringComparison.OrdinalIgnoreCase))
               ?? ProjectFactory.GuessDataType(address, vendor, keyenceDeviceMode);
    }

    internal static string NormalizeDeviceComment(string text) =>
        ProjectCommentRules.Normalize(text);

    internal static bool ParseClipboardBoolean(string text)
    {
        var value = text.Trim();
        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("checked", StringComparison.OrdinalIgnoreCase) ||
               MatchesImportAlias(value, "import.alias.boolean.true");
    }

    internal static string NormalizeTrapCondition(string text, string address, string vendor, string keyenceDeviceMode)
    {
        var value = text.Trim();
        var normalized = NormalizeAlias(value);
        var condition = ProjectFactory.TrapConditions.FirstOrDefault(item =>
                            item.Equals(value, StringComparison.OrdinalIgnoreCase))
                        ?? ImportTrapConditionAlias(normalized)
                        ?? ProjectFactory.DefaultTrapConditionForAddress(address, vendor, keyenceDeviceMode);
        return ProjectFactory.CoerceTrapConditionForAddress(address, condition, vendor, keyenceDeviceMode);
    }

    internal static bool IsTrapConditionField(string text)
    {
        var value = text.Trim();
        var normalized = NormalizeAlias(value);
        return ProjectFactory.TrapConditions.Any(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)) ||
               ImportTrapConditionAlias(normalized) is not null;
    }

    internal static bool IsAddressClipboardHeader(IReadOnlyList<string> fields)
    {
        if (fields.Count == 0)
        {
            return false;
        }

        var first = fields[0].Trim();
        return MatchesImportAlias(first, "import.alias.header.address") ||
               MatchesImportAlias(first, "import.alias.header.watch");
    }

    internal static bool IsTrapClipboardHeader(IReadOnlyList<string> fields)
    {
        if (fields.Count == 0)
        {
            return false;
        }

        var first = fields[0].Trim();
        var second = fields.Count > 1 ? fields[1].Trim() : "";
        return MatchesImportAlias(first, "import.alias.header.address") ||
               MatchesImportAlias(second, "import.alias.header.condition");
    }
}
