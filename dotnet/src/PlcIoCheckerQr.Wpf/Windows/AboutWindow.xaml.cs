using PlcIoCheckerQr.Wpf.Localization;
using QRCoder;
using System.Reflection;
using System.Windows;

namespace PlcIoCheckerQr.Wpf.Windows;

internal sealed partial class AboutWindow : Window
{
    private readonly LanguageCatalog _language;

    public AboutWindow(LanguageCatalog? language = null)
    {
        InitializeComponent();
        _language = language ?? LanguageCatalog.Load("en");

        var appVersion = GetAssemblyVersionText(Assembly.GetExecutingAssembly());
        Title = _language.Text("about.title");
        VersionTextBlock.Text = $"Version: {appVersion}";
        LibrariesTitleTextBlock.Text = _language.Text("about.libraries");
        CloseButton.Content = _language.Text("about.close");

        LibrariesListView.ItemsSource = new[]
        {
            new LibraryInfo(
                "PLC IO Checker Project Builder",
                appVersion,
                "Application",
                "MIT",
                "Not specified",
                "https://github.com/fa-yoshinobu/PlcIoChecker_QR"),
            new LibraryInfo(
                "QRCoder",
                GetAssemblyVersionText(typeof(QRCodeGenerator).Assembly),
                "QR code generation",
                "MIT",
                "Raffael Herrmann",
                "https://github.com/codebude/QRCoder/"),
            new LibraryInfo(
                ".NET Runtime",
                Environment.Version.ToString(),
                "Application runtime",
                "MIT",
                "Microsoft Corporation",
                "https://dotnet.microsoft.com/"),
        };
    }

    public AboutWindow(bool useEnglish) : this(LanguageCatalog.Load(useEnglish ? "en" : "ja"))
    {
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string GetAssemblyVersionText(Assembly assembly)
    {
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plusIndex = info.IndexOf('+', StringComparison.Ordinal);
            return plusIndex >= 0 ? info[..plusIndex] : info;
        }

        var version = assembly.GetName().Version;
        return version?.ToString() ?? "Unknown";
    }

    private sealed record LibraryInfo(
        string Name,
        string Version,
        string Notes,
        string License,
        string Copyright,
        string Url)
    {
        public string Details => $"License: {License}\nCopyright: {Copyright}\nURL: {Url}";
    }
}
