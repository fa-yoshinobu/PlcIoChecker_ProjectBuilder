using System.Globalization;
using System.IO;
using System.Text.Json;

namespace PlcIoCheckerQr.Wpf.Localization;

internal sealed class LanguageCatalog
{
    private const string DefaultCode = "en";
    private const string ResourcePrefix = "PlcIoCheckerQr.Wpf.Languages.";
    private const string ResourceSuffix = ".json";

    private readonly IReadOnlyDictionary<string, string> _texts;

    private LanguageCatalog(string code, IReadOnlyDictionary<string, string> texts)
    {
        Code = code;
        _texts = texts;
    }

    public string Code { get; }

    public static LanguageCatalog Load(string code)
    {
        var normalizedCode = NormalizeCode(code);
        try
        {
            var texts = ReadLanguageTexts(normalizedCode);
            if (!string.Equals(normalizedCode, DefaultCode, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var fallback in ReadLanguageTexts(DefaultCode))
                {
                    texts.TryAdd(fallback.Key, fallback.Value);
                }
            }

            return new LanguageCatalog(normalizedCode, texts);
        }
        catch (JsonException)
        {
            return new LanguageCatalog(normalizedCode, new Dictionary<string, string>());
        }
    }

    public static bool HasLanguage(string code) =>
        AvailableCodes().Contains(code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Codes() => AvailableCodes();

    public static string NextCode(string currentCode)
    {
        var codes = AvailableCodes();
        if (codes.Count == 0)
        {
            return DefaultCode;
        }

        var currentIndex = codes
            .Select((code, index) => new { code, index })
            .FirstOrDefault(item => item.code.Equals(currentCode, StringComparison.OrdinalIgnoreCase))
            ?.index ?? 0;
        return codes[(currentIndex + 1) % codes.Count];
    }

    public string Text(string key) =>
        _texts.TryGetValue(key, out var value) ? value : key;

    public string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, Text(key), args);

    private static string NormalizeCode(string code)
    {
        var requestedCode = string.IsNullOrWhiteSpace(code)
            ? DefaultCode
            : code.Trim().ToLowerInvariant();
        if (HasLanguage(requestedCode))
        {
            return requestedCode;
        }

        return HasLanguage(DefaultCode)
            ? DefaultCode
            : AvailableCodes().FirstOrDefault() ?? DefaultCode;
    }

    private static List<string> AvailableCodes()
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var languageDirectory = Path.Combine(AppContext.BaseDirectory, "Languages");
        if (Directory.Exists(languageDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(languageDirectory, "*.json"))
            {
                codes.Add(Path.GetFileNameWithoutExtension(path));
            }
        }

        foreach (var resourceName in typeof(LanguageCatalog).Assembly.GetManifestResourceNames())
        {
            if (resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal) &&
                resourceName.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                codes.Add(resourceName[ResourcePrefix.Length..^ResourceSuffix.Length]);
            }
        }

        return codes
            .OrderBy(code => code.Equals(DefaultCode, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> ReadLanguageTexts(string code)
    {
        using var stream = OpenLanguageStream(code);
        if (stream is null)
        {
            return [];
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
               ?? [];
    }

    private static Stream? OpenLanguageStream(string code)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Languages", $"{code}.json");
        if (File.Exists(path))
        {
            return File.OpenRead(path);
        }

        return typeof(LanguageCatalog).Assembly.GetManifestResourceStream($"{ResourcePrefix}{code}{ResourceSuffix}");
    }
}
