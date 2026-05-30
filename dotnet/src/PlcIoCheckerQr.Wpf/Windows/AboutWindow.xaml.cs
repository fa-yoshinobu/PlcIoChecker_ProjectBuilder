using PlcIoCheckerQr.Wpf.Localization;
using QRCoder;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using ZstdSharp;

namespace PlcIoCheckerQr.Wpf.Windows;

internal sealed partial class AboutWindow : Window
{
    private readonly LanguageCatalog _language;

    public AboutWindow(LanguageCatalog? language = null)
    {
        InitializeComponent();
        _language = language ?? LanguageCatalog.Load("en");

        var appVersion = GetAssemblyVersionText(Assembly.GetExecutingAssembly(), _language);
        var manualUrl = _language.Text("about.manualUrl");
        Title = _language.Text("about.title");
        VersionTextBlock.Text = _language.Format("about.version", appVersion);
        ManualTitleTextBlock.Text = _language.Text("about.manual");
        ManualHyperlink.NavigateUri = new Uri(manualUrl);
        ManualUrlRun.Text = manualUrl;
        LibrariesTitleTextBlock.Text = _language.Text("about.libraries");
        CloseButton.Content = _language.Text("about.close");

        LibrariesListView.ItemsSource = new[]
        {
            new LibraryInfo(
                "PLC IO Checker Project Builder",
                appVersion,
                _language.Text("about.library.application"),
                "MIT License",
                "FA Labo(fa_yoshinobu)",
                "https://github.com/fa-yoshinobu/PlcIoChecker_ProjectBuilder",
                _language.Format("about.libraryDetails", "MIT License", "FA Labo(fa_yoshinobu)")),
            new LibraryInfo(
                "QRCoder",
                GetAssemblyVersionText(typeof(QRCodeGenerator).Assembly, _language),
                _language.Text("about.library.qrcode"),
                "MIT License",
                "Raffael Herrmann",
                "https://github.com/codebude/QRCoder/",
                _language.Format("about.libraryDetails", "MIT License", "Raffael Herrmann")),
            new LibraryInfo(
                "ZstdSharp.Port",
                GetAssemblyVersionText(typeof(Compressor).Assembly, _language),
                _language.Text("about.library.zstd"),
                "MIT License",
                "Oleg Stepanischev",
                "https://github.com/oleg-st/ZstdSharp",
                _language.Format("about.libraryDetails", "MIT License", "Oleg Stepanischev")),
            new LibraryInfo(
                ".NET Runtime",
                Environment.Version.ToString(),
                _language.Text("about.library.runtime"),
                "MIT License",
                "Microsoft Corporation",
                "https://dotnet.microsoft.com/",
                _language.Format("about.libraryDetails", "MIT License", "Microsoft Corporation")),
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void UrlHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string GetAssemblyVersionText(Assembly assembly, LanguageCatalog language)
    {
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plusIndex = info.IndexOf('+', StringComparison.Ordinal);
            return plusIndex >= 0 ? info[..plusIndex] : info;
        }

        var version = assembly.GetName().Version;
        return version?.ToString() ?? language.Text("about.unknownVersion");
    }

    private sealed record LibraryInfo(
        string Name,
        string Version,
        string Notes,
        string License,
        string Author,
        string Url,
        string Details);
}
