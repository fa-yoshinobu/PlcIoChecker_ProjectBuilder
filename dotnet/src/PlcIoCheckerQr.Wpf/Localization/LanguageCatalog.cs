using System.Globalization;
using System.IO;
using System.Text.Json;

namespace PlcIoCheckerQr.Wpf.Localization;

internal sealed class LanguageCatalog
{
    private readonly IReadOnlyDictionary<string, string> _texts;

    private LanguageCatalog(string code, IReadOnlyDictionary<string, string> texts)
    {
        Code = code;
        _texts = texts;
    }

    public string Code { get; }

    public static LanguageCatalog Load(string code)
    {
        var normalizedCode = string.Equals(code, "ja", StringComparison.OrdinalIgnoreCase) ? "ja" : "en";
        var path = Path.Combine(AppContext.BaseDirectory, "Languages", $"{normalizedCode}.json");
        if (!File.Exists(path))
        {
            return new LanguageCatalog(normalizedCode, new Dictionary<string, string>());
        }

        try
        {
            using var stream = File.OpenRead(path);
            var texts = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                        ?? new Dictionary<string, string>();
            return new LanguageCatalog(normalizedCode, texts);
        }
        catch (JsonException)
        {
            return new LanguageCatalog(normalizedCode, new Dictionary<string, string>());
        }
    }

    public string Text(string key) =>
        _texts.TryGetValue(key, out var value) ? value : key;

    public string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, Text(key), args);
}
