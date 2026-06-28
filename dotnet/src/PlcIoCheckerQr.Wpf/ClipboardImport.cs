using System.Text;
using PlcIoCheckerQr.Core;

namespace PlcIoCheckerQr.Wpf;

internal static class ClipboardImport
{
    private static bool IsCanonicalHeader(string text, string header) =>
        text.Trim().Equals(header, StringComparison.OrdinalIgnoreCase);

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
        return IsCanonicalHeader(first, "Address") ||
               IsCanonicalHeader(second, "Data type");
    }

    internal static bool IsCommentClipboardHeader(IReadOnlyList<string> fields)
    {
        if (fields.Count == 0)
        {
            return false;
        }

        var first = fields[0].Trim();
        var second = fields.Count > 1 ? fields[1].Trim() : "";
        return IsCanonicalHeader(first, "Address") ||
               IsCanonicalHeader(first, "Comment") ||
               IsCanonicalHeader(second, "Comment") ||
               IsCanonicalHeader(second, "Data type");
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
        var dataType = ProjectFactory.DeviceDataTypesForAddress(address, vendor, keyenceDeviceMode).FirstOrDefault(candidate =>
            candidate.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (dataType is null)
        {
            var allowed = string.Join(", ", ProjectFactory.DeviceDataTypesForAddress(address, vendor, keyenceDeviceMode));
            throw new ArgumentException($"Invalid device data type for {address}: {value}. Use one of: {allowed}");
        }

        return dataType;
    }

    internal static string NormalizeDeviceComment(string text) =>
        ProjectCommentRules.Normalize(text);

    internal static bool ParseClipboardBoolean(string text)
    {
        var value = text.Trim();
        if (value.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new ArgumentException($"Invalid clipboard boolean: {value}. Use TRUE or FALSE.");
    }

    internal static string NormalizeTrapCondition(string text, string address, string vendor, string keyenceDeviceMode)
    {
        var value = text.Trim();
        var condition = ProjectFactory.TrapConditions.FirstOrDefault(item =>
            item.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (condition is null)
        {
            throw new ArgumentException($"Invalid trap condition for {address}: {value}. Use one of: {string.Join(", ", ProjectFactory.TrapConditions)}");
        }

        return ProjectFactory.ValidateTrapConditionForAddress(address, condition, vendor, keyenceDeviceMode);
    }

    internal static bool IsTrapConditionField(string text)
    {
        var value = text.Trim();
        return ProjectFactory.TrapConditions.Any(item => item.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsAddressClipboardHeader(IReadOnlyList<string> fields)
    {
        if (fields.Count == 0)
        {
            return false;
        }

        var first = fields[0].Trim();
        return IsCanonicalHeader(first, "Address") ||
               IsCanonicalHeader(first, "Time chart");
    }

    internal static bool IsTrapClipboardHeader(IReadOnlyList<string> fields)
    {
        if (fields.Count == 0)
        {
            return false;
        }

        var first = fields[0].Trim();
        var second = fields.Count > 1 ? fields[1].Trim() : "";
        return IsCanonicalHeader(first, "Address") ||
               IsCanonicalHeader(second, "Condition");
    }
}
