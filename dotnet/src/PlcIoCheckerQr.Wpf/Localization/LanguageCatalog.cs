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
        try
        {
            using var stream = OpenLanguageStream(normalizedCode);
            if (stream is null)
            {
                return new LanguageCatalog(normalizedCode, new Dictionary<string, string>());
            }

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

    private static Stream? OpenLanguageStream(string code)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Languages", $"{code}.json");
        if (File.Exists(path))
        {
            return File.OpenRead(path);
        }

        return typeof(LanguageCatalog).Assembly.GetManifestResourceStream($"PlcIoCheckerQr.Wpf.Languages.{code}.json");
    }
}
