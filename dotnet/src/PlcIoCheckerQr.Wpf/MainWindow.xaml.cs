using Microsoft.Win32;
using System.Collections.Specialized;
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
    private readonly ObservableCollection<CommentRow> _comments = [];
    private readonly ObservableCollection<WatchRow> _watches = [];
    private readonly ObservableCollection<TrapRow> _traps = [];
    private readonly Stack<Action> _undoStack = new();
    private readonly HashSet<DataTypedAddressRow> _trackedRows = [];

    private IReadOnlyList<QrChunk> _chunks = [];
    private int _currentIndex;
    private string _lastJson = "";

    private int _chunkSize = 800;
    private int _displaySize = 1000;
    private string _errorCorrection = "L";
    private string _languageCode = "en";
    private bool _isReadyStatus = true;
    private bool _isSyncingRowValues;
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
        SetupRowSynchronization();
        _autoQrTimer.Tick += AutoQrTimer_Tick;
        _autoQrTimer.Interval = TimeSpan.FromSeconds(DefaultAutoQrIntervalSeconds);
        _mainTabs.SelectionChanged += MainTabs_SelectionChanged;
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
        if (vendor != "Keyence")
        {
            SelectItem(_keyenceMode, "Normal");
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

            row.Address = NormalizeAddressText(row.Address);
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

            row.Address = NormalizeAddressText(row.Address);
            if (IsSupportedDeviceAddress(row.Address, vendor, keyenceDeviceMode, machineLabel))
            {
                row.EnsureDataTypeAllowed();
            }
        }

        foreach (var row in _comments)
        {
            row.SetDeviceContext(vendor, keyenceDeviceMode);
            if (string.IsNullOrWhiteSpace(row.Address))
            {
                continue;
            }

            row.Address = NormalizeAddressText(row.Address);
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

            trap.Address = NormalizeAddressText(trap.Address);
            if (IsSupportedDeviceAddress(trap.Address, vendor, keyenceDeviceMode, machineLabel))
            {
                trap.EnsureDataTypeAllowed();
                trap.EnsureConditionAllowed();
            }
        }

        _devicesGrid.Items.Refresh();
        _commentsGrid.Items.Refresh();
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
            NormalizeAddressRow(row);
            if (!string.IsNullOrWhiteSpace(row.DataType))
            {
                row.DataType = NormalizeDeviceDataType(row.DataType, row.Address);
            }
            row.Comment = NormalizeDeviceComment(row.Comment);
        }

        foreach (var row in _watches.Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            NormalizeAddressRow(row);
            if (!string.IsNullOrWhiteSpace(row.DataType))
            {
                row.DataType = NormalizeDeviceDataType(row.DataType, row.Address);
            }
        }

        foreach (var row in _traps.Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            NormalizeAddressRow(row);
            if (!string.IsNullOrWhiteSpace(row.DataType))
            {
                row.DataType = NormalizeDeviceDataType(row.DataType, row.Address);
            }
            row.EnsureConditionAllowed();
        }
        CommonizeDeviceDataTypes();
        CommonizeDeviceComments();
        NormalizeCommentRows();
        CommonizeDeviceDataTypes();

        _devicesGrid.Items.Refresh();
        _commentsGrid.Items.Refresh();
        _watchGrid.Items.Refresh();
        _trapsGrid.Items.Refresh();
        UpdateDeviceValidationStatus();
    }

    private void SetupRowSynchronization()
    {
        TrackRows(_devices);
        TrackRows(_comments);
        TrackRows(_watches);
        TrackRows(_traps);

        _devices.CollectionChanged += Rows_CollectionChanged;
        _comments.CollectionChanged += Rows_CollectionChanged;
        _watches.CollectionChanged += Rows_CollectionChanged;
        _traps.CollectionChanged += Traps_CollectionChanged;
    }

    private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        TrackRowChanges(e);

    private void Traps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        TrackRowChanges(e);
        UpdateTrapLimitUi();
    }

    private void TrackRows<T>(IEnumerable<T> rows) where T : DataTypedAddressRow
    {
        foreach (var row in rows)
        {
            SubscribeRow(row);
        }
    }

    private void TrackRowChanges(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DataTypedAddressRow row in e.OldItems)
            {
                UnsubscribeRow(row);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RebuildRowSubscriptions();
            return;
        }

        if (e.NewItems is not null)
        {
            foreach (DataTypedAddressRow row in e.NewItems)
            {
                SubscribeRow(row);
            }
        }
    }

    private void RebuildRowSubscriptions()
    {
        foreach (var row in _trackedRows.ToArray())
        {
            UnsubscribeRow(row);
        }

        TrackRows(DeviceAddressRows());
    }

    private void SubscribeRow(DataTypedAddressRow row)
    {
        if (_trackedRows.Add(row))
        {
            row.PropertyChanged += Row_PropertyChanged;
        }
    }

    private void UnsubscribeRow(DataTypedAddressRow row)
    {
        if (_trackedRows.Remove(row))
        {
            row.PropertyChanged -= Row_PropertyChanged;
        }
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSyncingRowValues ||
            e.PropertyName != nameof(DataTypedAddressRow.DataType) ||
            sender is not DataTypedAddressRow row)
        {
            return;
        }

        ApplyEditedDataType(row);
    }

    private void Grid_CurrentCellChanged(object? sender, EventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        Dispatcher.BeginInvoke(() => CommitGridEditAndSynchronize(grid), DispatcherPriority.Background);
    }

    private void Grid_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (!grid.IsKeyboardFocusWithin)
            {
                CommitGridEditAndSynchronize(grid);
            }
        }, DispatcherPriority.Background);
    }

    private void CommitGridEditAndSynchronize(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        SynchronizeRowsAfterGridCommit();
    }

    private void SynchronizeRowsAfterGridCommit()
    {
        if (_isSyncingRowValues)
        {
            return;
        }

        _isSyncingRowValues = true;
        try
        {
            CommonizeDeviceDataTypes();
            CommonizeDeviceComments();
            UpdateDeviceValidationStatus();
        }
        finally
        {
            _isSyncingRowValues = false;
        }
    }

    private void CommonizeDeviceDataTypes()
    {
        var dataTypesByAddress = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Remember(DataTypedAddressRow row)
        {
            if (string.IsNullOrWhiteSpace(row.Address) || string.IsNullOrWhiteSpace(row.DataType))
            {
                return;
            }

            dataTypesByAddress.TryAdd(row.Address.Trim().ToUpperInvariant(), row.DataType);
        }

        foreach (var row in _comments)
        {
            Remember(row);
        }

        foreach (var row in _devices)
        {
            Remember(row);
        }

        foreach (var row in _watches)
        {
            Remember(row);
        }

        foreach (var row in _traps)
        {
            Remember(row);
        }

        void Apply(DataTypedAddressRow row)
        {
            if (string.IsNullOrWhiteSpace(row.Address))
            {
                return;
            }

            var address = row.Address.Trim().ToUpperInvariant();
            if (dataTypesByAddress.TryGetValue(address, out var dataType))
            {
                row.DataType = dataType;
            }
        }

        foreach (var row in _devices)
        {
            Apply(row);
        }

        foreach (var row in _watches)
        {
            Apply(row);
        }

        foreach (var row in _traps)
        {
            Apply(row);
        }

        foreach (var row in _comments)
        {
            Apply(row);
        }
    }

    private void CommonizeDeviceComments()
    {
        foreach (var row in CommentSourceRows().Where(row =>
                     !string.IsNullOrWhiteSpace(row.Address) && !string.IsNullOrWhiteSpace(row.Comment)))
        {
            AddOrFillCommentRow(row.Address, NormalizeDeviceComment(row.Comment), row.DataType);
        }

        var commentsByAddress = CommentRowsByAddress();
        foreach (var row in CommentSourceRows().Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            row.Comment = commentsByAddress.GetValueOrDefault(row.Address.Trim().ToUpperInvariant(), "");
        }

        foreach (var row in _comments.Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            NormalizeAddressRow(row);
            var address = row.Address;
            row.Address = address;
            if (string.IsNullOrWhiteSpace(row.DataType))
            {
                row.DataType = KnownDataTypeForAddress(address);
            }
            else
            {
                row.DataType = NormalizeDeviceDataType(row.DataType, address);
            }
            if (commentsByAddress.TryGetValue(address, out var comment))
            {
                row.Comment = comment;
            }
        }
    }

    private IEnumerable<DataTypedAddressRow> CommentSourceRows() =>
        _devices.Cast<DataTypedAddressRow>()
            .Concat(_watches)
            .Concat(_traps);

    private void NormalizeCommentRows()
    {
        foreach (var row in _comments.Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            NormalizeAddressRow(row);
            if (string.IsNullOrWhiteSpace(row.DataType))
            {
                row.DataType = KnownDataTypeForAddress(row.Address);
            }
            else
            {
                row.DataType = NormalizeDeviceDataType(row.DataType, row.Address);
            }
            row.Comment = NormalizeDeviceComment(row.Comment);
        }
    }

    private Dictionary<string, string> CommentRowsByAddress()
    {
        var commentsByAddress = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _comments.Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            var address = row.Address.Trim().ToUpperInvariant();
            var comment = NormalizeDeviceComment(row.Comment);
            if (!commentsByAddress.TryAdd(address, comment) &&
                string.IsNullOrWhiteSpace(commentsByAddress[address]) &&
                !string.IsNullOrWhiteSpace(comment))
            {
                commentsByAddress[address] = comment;
            }
        }

        return commentsByAddress;
    }

    private void AddOrFillCommentRow(string address, string comment, string? dataType = null, bool overwriteComment = false)
    {
        var normalizedAddress = NormalizeAddressText(address);
        if (string.IsNullOrWhiteSpace(normalizedAddress))
        {
            return;
        }

        var existing = _comments.FirstOrDefault(row =>
            row.Address.Trim().Equals(normalizedAddress, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            var row = new CommentRow { Address = normalizedAddress, Comment = comment };
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.DataType = string.IsNullOrWhiteSpace(dataType)
                ? KnownDataTypeForAddress(normalizedAddress)
                : NormalizeDeviceDataType(dataType, normalizedAddress);
            _comments.Add(row);
            return;
        }

        if (!string.IsNullOrWhiteSpace(dataType))
        {
            existing.DataType = NormalizeDeviceDataType(dataType, normalizedAddress);
        }

        if (overwriteComment || string.IsNullOrWhiteSpace(existing.Comment) && !string.IsNullOrWhiteSpace(comment))
        {
            existing.Comment = comment;
        }
    }

    private void EnsureCommentRowsForRegisteredAddresses()
    {
        CommonizeDeviceComments();
        foreach (var address in RegisteredAddresses())
        {
            AddOrFillCommentRow(address, "");
        }

        CommonizeDeviceDataTypes();
        _commentsGrid.Items.Refresh();
    }

    private void AddressGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        var bindingPath = ColumnBindingPath(e.Column);
        if (e.EditAction != DataGridEditAction.Commit ||
            bindingPath is not (nameof(DataTypedAddressRow.Address) or nameof(DataTypedAddressRow.DataType) or nameof(DataTypedAddressRow.Comment)))
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (e.Row.Item is DataTypedAddressRow row)
            {
                if (bindingPath == nameof(DataTypedAddressRow.Address))
                {
                    NormalizeAddressRow(row);
                    if (row is TrapRow trap && !string.IsNullOrWhiteSpace(trap.Address))
                    {
                        trap.EnsureConditionAllowed();
                    }

                    CommonizeDeviceComments();
                }
                else if (bindingPath == nameof(DataTypedAddressRow.DataType))
                {
                    ApplyEditedDataType(row);
                }
                else
                {
                    ApplyEditedComment(row);
                }
            }

            UpdateDeviceValidationStatus();
        }, DispatcherPriority.Background);
    }

    private static string? ColumnBindingPath(DataGridColumn column) =>
        column is DataGridBoundColumn { Binding: Binding binding }
            ? binding.Path.Path
            : !string.IsNullOrWhiteSpace(column.SortMemberPath)
                ? column.SortMemberPath
            : null;

    private void ApplyEditedDataType(DataTypedAddressRow row)
    {
        if (_isSyncingRowValues)
        {
            return;
        }

        NormalizeAddressRow(row);
        if (string.IsNullOrWhiteSpace(row.Address))
        {
            return;
        }

        _isSyncingRowValues = true;
        try
        {
            row.EnsureDataTypeAllowed();
            ApplyDataTypeToRows(row.Address, row.DataType);
            AddOrFillCommentRow(row.Address, NormalizeDeviceComment(row.Comment), row.DataType);
        }
        finally
        {
            _isSyncingRowValues = false;
        }
    }

    private void ApplyEditedComment(DataTypedAddressRow row)
    {
        NormalizeAddressRow(row);
        if (string.IsNullOrWhiteSpace(row.Address))
        {
            return;
        }

        var comment = NormalizeDeviceComment(row.Comment);
        row.Comment = comment;
        AddOrFillCommentRow(row.Address, comment, row.DataType, overwriteComment: true);
        ApplyCommentToRows(row.Address, comment);
    }

    private void ApplyCommentToRows(string address, string comment)
    {
        var normalizedAddress = NormalizeAddressText(address);
        if (string.IsNullOrWhiteSpace(normalizedAddress))
        {
            return;
        }

        foreach (var row in DeviceAddressRows().Where(row =>
                     row.Address.Trim().Equals(normalizedAddress, StringComparison.OrdinalIgnoreCase)))
        {
            row.Comment = comment;
        }
    }

    private void ApplyDataTypeToRows(string address, string dataType)
    {
        var normalizedAddress = NormalizeAddressText(address);
        if (string.IsNullOrWhiteSpace(normalizedAddress))
        {
            return;
        }

        foreach (var row in DeviceAddressRows().Where(row =>
                     row.Address.Trim().Equals(normalizedAddress, StringComparison.OrdinalIgnoreCase)))
        {
            row.DataType = dataType;
        }
    }

    private string KnownDataTypeForAddress(string address)
    {
        var normalizedAddress = NormalizeAddressText(address);
        if (string.IsNullOrWhiteSpace(normalizedAddress))
        {
            return "";
        }

        return DeviceAddressRows()
            .Where(row => row.Address.Trim().Equals(normalizedAddress, StringComparison.OrdinalIgnoreCase))
            .Select(row => row.DataType)
            .FirstOrDefault(dataType => !string.IsNullOrWhiteSpace(dataType)) ?? "";
    }

    private void NormalizeAddressRow(DataTypedAddressRow row)
    {
        row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
        if (string.IsNullOrWhiteSpace(row.Address))
        {
            return;
        }

        row.Address = NormalizeAddressText(row.Address);
        if (IsSupportedDeviceAddress(row.Address, Selected(_vendor), SelectedKeyenceDeviceMode(), Selected(_model)))
        {
            row.EnsureDataTypeAllowed();
        }
    }

    private string NormalizeAddressText(string address)
    {
        var value = address.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        try
        {
            return ProjectFactory.NormalizeDeviceAddress(value, Selected(_vendor), SelectedKeyenceDeviceMode(), Selected(_model));
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            return value.ToUpperInvariant();
        }
    }

    private IEnumerable<string> RegisteredAddresses()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in _devices.Select(row => row.Address)
                     .Concat(_watches.Select(row => row.Address))
                     .Concat(_traps.Select(row => row.Address))
                     .Where(address => !string.IsNullOrWhiteSpace(address))
                     .Select(address => address.Trim().ToUpperInvariant()))
        {
            if (seen.Add(address))
            {
                yield return address;
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
        string? invalidAddress = null;

        foreach (var row in DeviceAddressRows())
        {
            var address = row.Address.Trim();
            var isUnsupported = !string.IsNullOrWhiteSpace(address) &&
                !IsSupportedDeviceAddress(address, vendor, keyenceDeviceMode, machineLabel);
            row.IsUnsupportedDevice = isUnsupported;
            if (isUnsupported && invalidAddress is null)
            {
                invalidAddress = address.ToUpperInvariant();
            }
        }

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
        _supportedDevicesText.Text = string.Join(
            ", ",
            ProjectFactory.SupportedDeviceNames(Selected(_vendor), SelectedKeyenceDeviceMode(), Selected(_model)));
    }

    private IEnumerable<string> DeviceAddresses() =>
        _devices.Select(row => row.Address)
            .Concat(_comments.Select(row => row.Address))
            .Concat(_watches.Select(row => row.Address))
            .Concat(_traps.Select(row => row.Address))
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.Trim().ToUpperInvariant());

    private IEnumerable<DataTypedAddressRow> DeviceAddressRows() =>
        _devices.Cast<DataTypedAddressRow>()
            .Concat(_comments)
            .Concat(_watches)
            .Concat(_traps);

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == _mainTabs && _mainTabs.SelectedItem == _commentsTab)
        {
            EnsureCommentRowsForRegisteredAddresses();
        }
    }

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

    protected override void OnClosed(EventArgs e)
    {
        _autoQrTimer.Stop();
        base.OnClosed(e);
    }
}
