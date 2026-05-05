using PlcIoCheckerQr.Wpf.Localization;
using QRCoder;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

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
                "MIT License",
                "FA Labo(fa_yoshinobu)",
                "https://github.com/fa-yoshinobu/PlcIoChecker_ProjectBuilder"),
            new LibraryInfo(
                "QRCoder",
                GetAssemblyVersionText(typeof(QRCodeGenerator).Assembly),
                "QR code generation",
                "MIT License",
                "Raffael Herrmann",
                "https://github.com/codebude/QRCoder/"),
            new LibraryInfo(
                ".NET Runtime",
                Environment.Version.ToString(),
                "Application runtime",
                "MIT License",
                "Microsoft Corporation",
                "https://dotnet.microsoft.com/"),
        };
    }

    public AboutWindow(bool useEnglish) : this(LanguageCatalog.Load(useEnglish ? "en" : "ja"))
    {
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
        string Author,
        string Url)
    {
        public string Details => $"License: {License}\nAuthor: {Author}";
    }
}
