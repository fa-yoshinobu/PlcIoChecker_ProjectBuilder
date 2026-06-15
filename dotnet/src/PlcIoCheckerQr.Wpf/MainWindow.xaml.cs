using Microsoft.Win32;
using PlcIoCheckerQr.Core;
using PlcIoCheckerQr.Wpf.Localization;
using PlcIoCheckerQr.Wpf.Windows;
using QRCoder;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingSize = System.Drawing.Size;
using static PlcIoCheckerQr.Wpf.ClipboardImport;
using static PlcIoCheckerQr.Wpf.NumericParsing;
using static PlcIoCheckerQr.Wpf.ProjectJsonReader;
using static PlcIoCheckerQr.Wpf.UiValueMapping;

namespace PlcIoCheckerQr.Wpf;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<DeviceRow> _devices = [];
    private readonly ObservableCollection<WatchRow> _watches = [];
    private readonly ObservableCollection<TrapRow> _traps = [];

    private IReadOnlyList<QrChunk> _chunks = [];
    private int _currentIndex;
    private string _lastJson = "";

    private int _chunkSize = 800;
    private int _displaySize = 1000;
    private string _errorCorrection = "L";
    private string _languageCode = "en";
    private bool _isReadyStatus = true;
    private readonly DispatcherTimer _autoQrTimer = new();

    private const double DefaultAutoQrIntervalSeconds = 1.0;
    private const double MinAutoQrIntervalSeconds = 0.0;
    private const double MaxAutoQrIntervalSeconds = 5.0;
    private const double MinDispatcherTimerSeconds = 0.1;

    private static LanguageCatalog CurrentLanguage { get; set; } = LanguageCatalog.Load("en");
    private LanguageCatalog _language = LanguageCatalog.Load("en");

    private static string TrapConditionDisplayText(string condition) =>
        UiValueMapping.TrapConditionDisplayText(condition, CurrentLanguage);

    public MainWindow()
    {
        InitializeComponent();
        MoveProjectTabFirst();
        SetupComboBoxes();
        SetupGrids();
        _autoQrTimer.Tick += AutoQrTimer_Tick;
        _autoQrTimer.Interval = TimeSpan.FromSeconds(DefaultAutoQrIntervalSeconds);
        _traps.CollectionChanged += (_, _) => UpdateTrapLimitUi();
        ApplyLanguage();

        _qrView.Focusable = true;
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(MainWindow_PreviewKeyDown), handledEventsToo: true);
        _vendor.SelectionChanged += (_, _) => ApplyVendorDefaults();
        _keyenceMode.SelectionChanged += (_, _) => ApplyDeviceContextToRows();
        _model.SelectionChanged += (_, _) => UpdateDeviceValidationStatus();
        _projectName.TextChanged += (_, _) => UpdateHeaderProjectName();

        ApplyVendorDefaults();
        UpdateHeaderProjectName();
        UpdateQrMenuChecks();
        Generate(showQrScreen: false);
    }

    private void ApplyVendorDefaults()
    {
        var vendor = Selected(_vendor);
        var models = vendor == "Keyence" ? ProjectFactory.KeyenceCpuModels : ProjectFactory.MelsecCpuModels;

        _model.ItemsSource = models;
        _model.SelectedIndex = 0;
        _port.Text = vendor == "Keyence" ? "8501" : "1025";
        var keyenceVisibility = vendor == "Keyence" ? Visibility.Visible : Visibility.Collapsed;
        _keyenceModeLabel.Visibility = keyenceVisibility;
        _keyenceMode.Visibility = keyenceVisibility;
        var melsecRoutingVisibility = vendor == "Keyence" ? Visibility.Collapsed : Visibility.Visible;
        _networkLabel.Visibility = melsecRoutingVisibility;
        _network.Visibility = melsecRoutingVisibility;
        _stationLabel.Visibility = melsecRoutingVisibility;
        _station.Visibility = melsecRoutingVisibility;
        _moduleIoLabel.Visibility = melsecRoutingVisibility;
        _moduleIo.Visibility = melsecRoutingVisibility;
        _multidropLabel.Visibility = melsecRoutingVisibility;
        _multidrop.Visibility = melsecRoutingVisibility;
        _resetRoutingDefaultsButton.Visibility = melsecRoutingVisibility;
        _remotePasswordLabel.Visibility = melsecRoutingVisibility;
        _remotePassword.Visibility = melsecRoutingVisibility;
        if (vendor != "Keyence")
        {
            SelectItem(_keyenceMode, "Normal");
        }
        else
        {
            _remotePassword.Password = "";
        }

        ApplyDeviceContextToRows();
        UpdateSupportedDeviceNames();
    }

    private void ApplyDeviceContextToRows()
    {
        var vendor = Selected(_vendor);
        var keyenceDeviceMode = SelectedKeyenceDeviceMode();
        var machineLabel = Selected(_model);

        foreach (var row in _devices)
        {
            row.SetDeviceContext(vendor, keyenceDeviceMode);
            if (string.IsNullOrWhiteSpace(row.Address))
            {
                continue;
            }

            row.Address = row.Address.Trim().ToUpperInvariant();
            if (IsSupportedDeviceAddress(row.Address, vendor, keyenceDeviceMode, machineLabel))
            {
                row.EnsureDataTypeAllowed();
            }
        }

        foreach (var row in _watches)
        {
            row.SetDeviceContext(vendor, keyenceDeviceMode);
            if (string.IsNullOrWhiteSpace(row.Address))
            {
                continue;
            }

            row.Address = row.Address.Trim().ToUpperInvariant();
            if (IsSupportedDeviceAddress(row.Address, vendor, keyenceDeviceMode, machineLabel))
            {
                row.EnsureDataTypeAllowed();
            }
        }

        foreach (var trap in _traps)
        {
            trap.SetDeviceContext(vendor, keyenceDeviceMode);
            if (string.IsNullOrWhiteSpace(trap.Address))
            {
                continue;
            }

            trap.Address = trap.Address.Trim().ToUpperInvariant();
            if (IsSupportedDeviceAddress(trap.Address, vendor, keyenceDeviceMode, machineLabel))
            {
                trap.EnsureDataTypeAllowed();
                trap.Condition = ProjectFactory.CoerceTrapConditionForAddress(trap.Address, trap.Condition, vendor, keyenceDeviceMode);
            }
        }

        _devicesGrid.Items.Refresh();
        _watchGrid.Items.Refresh();
        _trapsGrid.Items.Refresh();
        UpdateSupportedDeviceNames();
        UpdateDeviceValidationStatus();
    }

    private void UpdateHeaderProjectName()
    {
        var name = string.IsNullOrWhiteSpace(_projectName.Text)
            ? "PLC QR Project"
            : _projectName.Text.Trim();
        _headerProjectName.Text = name;
    }

    private void NormalizeGridRows()
    {
        foreach (var row in _devices.Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = row.Address.Trim().ToUpperInvariant();
            row.DataType = string.IsNullOrWhiteSpace(row.DataType)
                ? ProjectFactory.GuessDataType(row.Address, Selected(_vendor), SelectedKeyenceDeviceMode())
                : NormalizeDeviceDataType(row.DataType, row.Address);
            row.Comment = NormalizeDeviceComment(row.Comment);
        }
        CommonizeDeviceComments();

        foreach (var row in _watches.Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = row.Address.Trim().ToUpperInvariant();
            row.DataType = string.IsNullOrWhiteSpace(row.DataType)
                ? ProjectFactory.GuessDataType(row.Address, Selected(_vendor), SelectedKeyenceDeviceMode())
                : NormalizeDeviceDataType(row.DataType, row.Address);
        }

        foreach (var row in _traps.Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = row.Address.Trim().ToUpperInvariant();
            row.DataType = string.IsNullOrWhiteSpace(row.DataType)
                ? ProjectFactory.GuessDataType(row.Address, Selected(_vendor), SelectedKeyenceDeviceMode())
                : NormalizeDeviceDataType(row.DataType, row.Address);
            row.Condition = ProjectFactory.CoerceTrapConditionForAddress(row.Address, row.Condition, Selected(_vendor), SelectedKeyenceDeviceMode());
        }

        _devicesGrid.Items.Refresh();
        _watchGrid.Items.Refresh();
        _trapsGrid.Items.Refresh();
    }

    private void CommonizeDeviceComments()
    {
        var commentsByAddress = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _devices.Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            var comment = NormalizeDeviceComment(row.Comment);
            row.Comment = comment;
            if (!string.IsNullOrWhiteSpace(comment) && !commentsByAddress.ContainsKey(row.Address))
            {
                commentsByAddress[row.Address] = comment;
            }
        }

        foreach (var row in _devices.Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            if (commentsByAddress.TryGetValue(row.Address, out var comment))
            {
                row.Comment = comment;
            }
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        _isReadyStatus = false;
        _statusText.Text = message;
        _statusText.Foreground = isError
            ? (Brush)FindResource("ErrorFg")
            : (Brush)FindResource("TextMuted");
    }

    private void SetReadyStatus()
    {
        _isReadyStatus = true;
        _statusText.Text = T("status.ready");
        _statusText.Foreground = (Brush)FindResource("TextMuted");
    }

    private void UpdateDeviceValidationStatus()
    {
        var vendor = Selected(_vendor);
        var keyenceDeviceMode = SelectedKeyenceDeviceMode();
        var machineLabel = Selected(_model);
        var invalidAddress = DeviceAddresses()
            .FirstOrDefault(address => !IsSupportedDeviceAddress(address, vendor, keyenceDeviceMode, machineLabel));
        if (invalidAddress is null)
        {
            SetStatus(T("status.deviceCheckOk"));
            return;
        }

        var context = vendor == "Keyence" ? $"{DisplayVendor(vendor)} {keyenceDeviceMode.ToUpperInvariant()}" : DisplayVendor(vendor);
        SetStatus(Tf("status.unsupportedDevice", context, invalidAddress), isError: true);
    }

    private void UpdateSupportedDeviceNames()
    {
        _supportedDevicesText.Text = string.Join(", ", ProjectFactory.SupportedDeviceNames(Selected(_vendor), SelectedKeyenceDeviceMode()));
    }

    private IEnumerable<string> DeviceAddresses() =>
        _devices.Select(row => row.Address)
            .Concat(_watches.Select(row => row.Address))
            .Concat(_traps.Select(row => row.Address))
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.Trim().ToUpperInvariant());

    private static bool IsSupportedDeviceAddress(string address, string vendor, string keyenceDeviceMode, string? machineLabel)
    {
        try
        {
            ProjectFactory.ValidateDeviceAddress(address, vendor, keyenceDeviceMode, machineLabel);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void SetSummary(PlcProject project)
    {
        _summaryText.Text = Tf(
            "summary.text",
            DisplayVendor(project.Connection.Vendor),
            project.Connection.MachineLabel,
            project.Devices.Count,
            project.TimeChartAddresses.Count,
            ProjectFactory.MaxTimeChartTargets,
            project.Traps.Count,
            ProjectFactory.MaxTrapDefinitions);
    }

    private void Generate_Click(object sender, RoutedEventArgs e) => Generate(showQrScreen: true);

    private void ResetRoutingDefaults_Click(object sender, RoutedEventArgs e)
    {
        _network.Text = "0";
        _station.Text = "255";
        _moduleIo.Text = FormatPrefixedHex(0x03FF, 4);
        _multidrop.Text = FormatPrefixedHex(0, 2);
    }

    private void ManualMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(T("about.manualUrl")) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ProjectBuilderManualLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(T("manual.projectBuilderUrl")) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e) => new AboutWindow(_language) { Owner = this }.ShowDialog();

    private void BackToEditor_Click(object sender, RoutedEventArgs e) => ShowInputScreen();

    private static string Selected(ComboBox comboBox) => comboBox.SelectedItem?.ToString() ?? "";

    private string SelectedKeyenceDeviceMode() =>
        Selected(_vendor) == "Keyence" ? Selected(_keyenceMode) : "Normal";

    private static void SelectItem(ComboBox comboBox, string value)
    {
        if (comboBox.Items.Contains(value))
        {
            comboBox.SelectedItem = value;
        }
    }
}
