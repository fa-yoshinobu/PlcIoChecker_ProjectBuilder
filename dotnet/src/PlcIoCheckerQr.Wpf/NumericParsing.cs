using System.Globalization;
using System.Windows.Controls;

namespace PlcIoCheckerQr.Wpf;

internal static class NumericParsing
{
    internal static int ParseRange(TextBox textBox, int min, int max)
    {
        var value = Clamp(ParseInt(textBox.Text), min, max);
        textBox.Text = value.ToString(CultureInfo.InvariantCulture);
        return value;
    }

    internal static int ParseHexRange(TextBox textBox, int min, int max, int width)
    {
        var value = Clamp(ParseHexInt(textBox.Text), min, max);
        textBox.Text = UiValueMapping.FormatPrefixedHex(value, width);
        return value;
    }

    internal static double ParseDoubleRange(TextBox textBox, double min, double max)
    {
        var value = Clamp(ParseDouble(textBox.Text), min, max);
        value = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        textBox.Text = FormatSeconds(value);
        return value;
    }

    internal static int ParseInt(string text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        throw new FormatException($"Invalid integer value: {text.Trim()}");
    }

    internal static double ParseDouble(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            double.IsNaN(value) ||
            double.IsInfinity(value))
        {
            throw new FormatException($"Invalid numeric value: {text.Trim()}");
        }

        return value;
    }

    internal static int ParseHexInt(string text)
    {
        var token = text.Trim();
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            token = token[2..];
        }

        if (int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        throw new FormatException($"Invalid hexadecimal value: {text.Trim()}");
    }

    internal static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    internal static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    internal static string FormatSeconds(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}
