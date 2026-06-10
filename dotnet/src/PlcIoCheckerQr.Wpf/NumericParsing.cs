using System.Globalization;
using System.Windows.Controls;

namespace PlcIoCheckerQr.Wpf;

internal static class NumericParsing
{
    internal static int ParseRange(TextBox textBox, int fallback, int min, int max)
    {
        var value = Clamp(ParseInt(textBox.Text, fallback), min, max);
        textBox.Text = value.ToString(CultureInfo.InvariantCulture);
        return value;
    }

    internal static int ParseHexRange(TextBox textBox, int fallback, int min, int max, int width)
    {
        var value = Clamp(ParseHexInt(textBox.Text, fallback), min, max);
        textBox.Text = UiValueMapping.FormatPrefixedHex(value, width);
        return value;
    }

    internal static double ParseDoubleRange(TextBox textBox, double fallback, double min, double max)
    {
        var value = Clamp(ParseDouble(textBox.Text, fallback), min, max);
        value = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        textBox.Text = FormatSeconds(value);
        return value;
    }

    internal static int ParseInt(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    internal static double ParseDouble(string text, double fallback)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            double.IsNaN(value) ||
            double.IsInfinity(value))
        {
            return fallback;
        }

        return value;
    }

    internal static int ParseHexInt(string text, int fallback)
    {
        var token = text.Trim();
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            token = token[2..];
        }
        return int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    internal static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    internal static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    internal static string FormatSeconds(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}
