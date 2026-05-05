using PlcIoCheckerQr.Core;
using QRCoder;
using System.Reflection;
using System.Windows;

namespace PlcIoCheckerQr.Wpf.Windows;

internal sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var appVersion = GetAssemblyVersionText(Assembly.GetExecutingAssembly());
        VersionTextBlock.Text = $"Version: {appVersion}";

        LibrariesListView.ItemsSource = new[]
        {
            new LibraryInfo("PLC IO Checker QR Builder", appVersion, "Application"),
            new LibraryInfo("PlcIoCheckerQr.Core", GetAssemblyVersionText(typeof(ProjectQrPayload).Assembly), "QR payload models and encoding"),
            new LibraryInfo("QRCoder", GetAssemblyVersionText(typeof(QRCodeGenerator).Assembly), "QR code generation"),
            new LibraryInfo(".NET Runtime", Environment.Version.ToString(), "Application runtime"),
        };
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

    private sealed record LibraryInfo(string Name, string Version, string Notes);
}
