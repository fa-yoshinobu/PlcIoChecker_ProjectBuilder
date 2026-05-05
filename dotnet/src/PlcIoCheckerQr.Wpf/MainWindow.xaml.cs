using Microsoft.Win32;
using PlcIoCheckerQr.Core;
using PlcIoCheckerQr.Wpf.Windows;
using QRCoder;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingSize = System.Drawing.Size;

namespace PlcIoCheckerQr.Wpf;

public partial class MainWindow : Window
{
    public sealed class DeviceRow
    {
        public string Address { get; set; } = "";
        public string DataType { get; set; } = "Bit";
    }

    public sealed class WatchRow
    {
        public string Address { get; set; } = "";
    }

    public sealed class TrapConditionOption(string value)
    {
        public string Value { get; } = value;
        public string DisplayText { get; } = MainWindow.TrapConditionDisplayText(value);
    }

    public sealed class TrapRow : INotifyPropertyChanged
    {
        private string _address = "";
        private string _condition = "Change";
        private string _threshold = "";
        private string _vendor = "Melsec";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Address
        {
            get => _address;
            set
            {
                if (_address == value)
                {
                    return;
                }

                _address = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AvailableConditionOptions));
                CoerceCondition();
            }
        }

        public string Condition
        {
            get => _condition;
            set => SetCondition(ProjectFactory.CoerceTrapConditionForAddress(Address, value, _vendor));
        }

        public string ConditionDisplayText => TrapConditionDisplayText(Condition);

        public IReadOnlyList<TrapConditionOption> AvailableConditionOptions =>
            ProjectFactory.TrapConditionsForAddress(Address, _vendor)
                .Select(condition => new TrapConditionOption(condition))
                .ToArray();

        public string Threshold
        {
            get => _threshold;
            set
            {
                var next = ThresholdEnabled ? value : "";
                if (_threshold == next)
                {
                    return;
                }

                _threshold = next;
                OnPropertyChanged();
            }
        }

        public bool ThresholdEnabled => ProjectFactory.TrapConditionRequiresThreshold(Condition);

        public bool Enabled { get; set; } = true;

        public void SetVendor(string vendor)
        {
            var next = string.IsNullOrWhiteSpace(vendor) ? "Melsec" : vendor;
            if (_vendor == next)
            {
                return;
            }

            _vendor = next;
            OnPropertyChanged(nameof(AvailableConditionOptions));
            CoerceCondition();
        }

        private void CoerceCondition() => SetCondition(ProjectFactory.CoerceTrapConditionForAddress(Address, Condition, _vendor));

        private void SetCondition(string condition)
        {
            if (_condition == condition)
            {
                EnsureThresholdState();
                return;
            }

            _condition = condition;
            OnPropertyChanged(nameof(Condition));
            OnPropertyChanged(nameof(ConditionDisplayText));
            OnPropertyChanged(nameof(ThresholdEnabled));
            EnsureThresholdState();
        }

        private void EnsureThresholdState()
        {
            if (ProjectFactory.TrapConditionRequiresThreshold(Condition))
            {
                if (string.IsNullOrWhiteSpace(Threshold))
                {
                    Threshold = "0";
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(Threshold))
            {
                Threshold = "";
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private readonly ObservableCollection<DeviceRow> _devices = [];
    private readonly ObservableCollection<WatchRow> _watches = [];
    private readonly ObservableCollection<TrapRow> _traps = [];

    private IReadOnlyList<QrChunk> _chunks = [];
    private int _currentIndex;
    private string _lastJson = "";

    private int _chunkSize = 800;
    private int _displaySize = 1000;
    private string _errorCorrection = "L";

    private static string TrapConditionDisplayText(string condition) => condition switch
    {
        "Rise" => "立上り (OFF→ON)",
        "Fall" => "立下り (ON→OFF)",
        "Change" => "変化",
        "GreaterOrEqual" => ">=",
        "LessOrEqual" => "<=",
        "Equal" => "==",
        "NotEqual" => "!=",
        _ => condition,
    };

    public MainWindow()
    {
        InitializeComponent();
        SetupComboBoxes();
        SetupGrids();
        LoadDefaultRows();

        _vendor.SelectionChanged += (_, _) => ApplyVendorDefaults();
        _projectName.TextChanged += (_, _) => UpdateHeaderProjectName();

        ApplyVendorDefaults();
        UpdateHeaderProjectName();
        UpdateQrMenuChecks();
        Generate(showQrScreen: false);
    }

    private void SetupComboBoxes()
    {
        Fill(_vendor, ProjectFactory.Vendors, "Melsec");
        Fill(_connectionMode, ProjectFactory.ConnectionModes, "Real");
        Fill(_transport, ProjectFactory.TransportModes, "Tcp");
        Fill(_model, ProjectFactory.MelsecCpuModels, "iQ-R");
        Fill(_keyenceMode, ProjectFactory.KeyenceDeviceModes, "Normal");
        Fill(_density, ProjectFactory.BlockDisplayDensities, "Compact");

        static void Fill(ComboBox comboBox, string[] values, string selected)
        {
            comboBox.ItemsSource = values;
            comboBox.SelectedItem = selected;
        }
    }

    private void SetupGrids()
    {
        _devicesGrid.ItemsSource = _devices;
        _devicesGrid.PreviewKeyDown += DevicesGrid_PreviewKeyDown;
        _devicesGrid.ToolTip = "Excel とタブ区切りでコピー/貼り付けできます。列は アドレス / Data type です。";
        _devicesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "アドレス",
            Binding = new Binding(nameof(DeviceRow.Address)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _devicesGrid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Data type",
            ItemsSource = ProjectFactory.DeviceDataTypes,
            SelectedItemBinding = new Binding(nameof(DeviceRow.DataType)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(170),
        });

        _watchGrid.ItemsSource = _watches;
        _watchGrid.PreviewKeyDown += WatchGrid_PreviewKeyDown;
        _watchGrid.ToolTip = "Excel とタブ区切りでコピー/貼り付けできます。列は アドレス です。";
        _watchGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "アドレス",
            Binding = new Binding(nameof(WatchRow.Address)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });

        _trapsGrid.ItemsSource = _traps;
        _trapsGrid.PreviewKeyDown += TrapsGrid_PreviewKeyDown;
        _trapsGrid.ToolTip = "Excel とタブ区切りでコピー/貼り付けできます。列は アドレス / 検知条件 / しきい値 / 有効 です。";
        _trapsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "アドレス",
            Binding = new Binding(nameof(TrapRow.Address)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _trapsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "検知条件",
            CellTemplate = TrapConditionCellTemplate(),
            CellEditingTemplate = TrapConditionEditingTemplate(),
            Width = new DataGridLength(220),
        });
        _trapsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "しきい値",
            CellTemplate = TrapThresholdCellTemplate(),
            CellEditingTemplate = TrapThresholdEditingTemplate(),
            Width = new DataGridLength(140),
        });
        _trapsGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "有効",
            Binding = new Binding(nameof(TrapRow.Enabled)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(90),
        });
    }

    private static DataTemplate TrapConditionCellTemplate()
    {
        var textBlock = new FrameworkElementFactory(typeof(TextBlock));
        textBlock.SetBinding(TextBlock.TextProperty, new Binding(nameof(TrapRow.ConditionDisplayText)));
        textBlock.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        return new DataTemplate { VisualTree = textBlock };
    }

    private static DataTemplate TrapConditionEditingTemplate()
    {
        var comboBox = new FrameworkElementFactory(typeof(ComboBox));
        comboBox.SetValue(ItemsControl.DisplayMemberPathProperty, nameof(TrapConditionOption.DisplayText));
        comboBox.SetValue(Selector.SelectedValuePathProperty, nameof(TrapConditionOption.Value));
        comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(TrapRow.AvailableConditionOptions)));
        comboBox.SetBinding(Selector.SelectedValueProperty, new Binding(nameof(TrapRow.Condition))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        return new DataTemplate { VisualTree = comboBox };
    }

    private static DataTemplate TrapThresholdCellTemplate()
    {
        var textBlock = new FrameworkElementFactory(typeof(TextBlock));
        textBlock.SetBinding(TextBlock.TextProperty, new Binding(nameof(TrapRow.Threshold)));
        textBlock.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(TrapRow.ThresholdEnabled)));
        textBlock.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        return new DataTemplate { VisualTree = textBlock };
    }

    private static DataTemplate TrapThresholdEditingTemplate()
    {
        var textBox = new FrameworkElementFactory(typeof(TextBox));
        textBox.SetBinding(TextBox.TextProperty, new Binding(nameof(TrapRow.Threshold))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        textBox.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(TrapRow.ThresholdEnabled)));
        return new DataTemplate { VisualTree = textBox };
    }

    private void LoadDefaultRows()
    {
        _devices.Clear();
        _devices.Add(new DeviceRow { Address = "X000", DataType = "Bit" });
        _devices.Add(new DeviceRow { Address = "Y000", DataType = "Bit" });
        _devices.Add(new DeviceRow { Address = "D100", DataType = "Int16" });
        _devices.Add(new DeviceRow { Address = "D102", DataType = "UInt32" });

        _watches.Clear();
        _watches.Add(new WatchRow { Address = "X000" });
        _watches.Add(new WatchRow { Address = "D100" });
        _traps.Clear();
        _traps.Add(new TrapRow { Address = "D100", Condition = "GreaterOrEqual", Threshold = "100", Enabled = true });
        _traps.Add(new TrapRow { Address = "D102", Condition = "Change", Enabled = true });
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
        if (vendor != "Keyence")
        {
            SelectItem(_keyenceMode, "Normal");
        }

        foreach (var trap in _traps)
        {
            trap.SetVendor(vendor);
        }
    }

    private void UpdateHeaderProjectName()
    {
        var name = string.IsNullOrWhiteSpace(_projectName.Text)
            ? "PLC QR Project"
            : _projectName.Text.Trim();
        _headerProjectName.Text = name;
    }

    private void Generate(bool showQrScreen)
    {
        try
        {
            CommitGridEdits();
            var project = BuildProject();
            _chunks = ProjectQrPayload.EncodeProjectChunks(project, _chunkSize);
            _currentIndex = 0;
            _lastJson = Encoding.UTF8.GetString(ProjectQrPayload.ProjectJsonBytes(project));
            ShowCurrentQr();
            SetSummary(project);
            SetStatus($"QR 生成完了: {_chunks.Count} page(s)");

            if (showQrScreen)
            {
                ShowQrScreen();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"生成エラー: {ex.Message}", isError: true);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private PlcProject BuildProject() => ProjectFactory.MakeProject(new ProjectInput(
        Name: _projectName.Text,
        Vendor: Selected(_vendor),
        ConnectionMode: Selected(_connectionMode),
        Host: _host.Text,
        Port: ParseRange(_port, fallback: 1025, min: 1, max: 65535),
        MonitorIntervalMs: ParseRange(_interval, fallback: 500, min: 50, max: 60000),
        TimeoutMs: ParseRange(_timeout, fallback: 2000, min: 100, max: 60000),
        MachineLabel: Selected(_model),
        KeyenceDeviceMode: Selected(_keyenceMode),
        TransportMode: Selected(_transport),
        Network: ParseRange(_network, fallback: 0, min: 0, max: 255),
        Station: ParseRange(_station, fallback: 255, min: 0, max: 255),
        ModuleIo: ParseRange(_moduleIo, fallback: 1023, min: 0, max: 65535),
        Multidrop: ParseRange(_multidrop, fallback: 0, min: 0, max: 255),
        DevicesText: DevicesText(),
        WatchText: WatchText(),
        TrapsText: TrapsText(),
        BlockDisplayDensity: Selected(_density)));

    private void ShowCurrentQr()
    {
        if (_chunks.Count == 0)
        {
            _qrImage.Source = null;
            _pageLabel.Text = "QR 未生成";
            _qrMeta.Text = "";
            return;
        }

        var chunk = _chunks[_currentIndex];
        _pageLabel.Text = $"{chunk.Index} / {chunk.Total} page";
        _qrMeta.Text = $"payload {chunk.Payload.Length} chars / EC {_errorCorrection} / session {chunk.Session} / sha256 {chunk.Checksum[..12]}...";

        using var bitmap = MakeQrBitmap(chunk.Text);
        _qrImage.Width = _displaySize;
        _qrImage.Height = _displaySize;
        _qrImage.Source = BitmapToSource(bitmap);
    }

    private DrawingBitmap MakeQrBitmap(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, ErrorCorrectionLevel());
        using var qr = new QRCode(data);
        using var original = qr.GetGraphic(20, DrawingColor.Black, DrawingColor.White, drawQuietZones: true);
        return new DrawingBitmap(original, new DrawingSize(_displaySize, _displaySize));
    }

    private static BitmapSource BitmapToSource(DrawingBitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;

        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.StreamSource = stream;
        source.EndInit();
        source.Freeze();
        return source;
    }

    private QRCodeGenerator.ECCLevel ErrorCorrectionLevel() => _errorCorrection switch
    {
        "M" => QRCodeGenerator.ECCLevel.M,
        "Q" => QRCodeGenerator.ECCLevel.Q,
        "H" => QRCodeGenerator.ECCLevel.H,
        _ => QRCodeGenerator.ECCLevel.L,
    };

    private string DevicesText() => string.Join(Environment.NewLine,
        _devices
            .Where(row => !string.IsNullOrWhiteSpace(row.Address))
            .Select(row =>
            {
                var address = row.Address.Trim();
                var dataType = string.IsNullOrWhiteSpace(row.DataType)
                    ? ProjectFactory.GuessDataType(address, Selected(_vendor))
                    : row.DataType.Trim();
                return $"{address},{dataType}";
            }));

    private string WatchText() => string.Join(Environment.NewLine, TimeChartAddresses());

    private IReadOnlyList<string> TimeChartAddresses()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addresses = new List<string>();

        foreach (var row in _watches.Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            var address = row.Address.Trim();
            if (seen.Add(address))
            {
                addresses.Add(address);
            }
        }

        if (addresses.Count > ProjectFactory.MaxTimeChartTargets)
        {
            throw new ArgumentException($"タイムチャートに追加できるのは最大 {ProjectFactory.MaxTimeChartTargets} チャンネルです。");
        }

        return addresses;
    }

    private string TrapsText() => string.Join(Environment.NewLine,
        _traps
            .Where(row => !string.IsNullOrWhiteSpace(row.Address) && !string.IsNullOrWhiteSpace(row.Condition))
            .Select(row => $"{row.Address.Trim()},{row.Condition.Trim()},{row.Threshold.Trim()},{(row.Enabled ? "true" : "false")}"));

    private void ShowQrScreen()
    {
        _inputView.Visibility = Visibility.Collapsed;
        _qrView.Visibility = Visibility.Visible;
    }

    private void ShowInputScreen()
    {
        _qrView.Visibility = Visibility.Collapsed;
        _inputView.Visibility = Visibility.Visible;
    }

    private void CommitGridEdits()
    {
        foreach (var grid in new[] { _devicesGrid, _watchGrid, _trapsGrid })
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        _statusText.Text = message;
        _statusText.Foreground = isError
            ? (Brush)FindResource("ErrorFg")
            : (Brush)FindResource("TextMuted");
    }

    private void SetSummary(PlcProject project)
    {
        _summaryText.Text = $"{project.Connection.Vendor} {project.Connection.MachineLabel} / デバイス {project.Devices.Count} / タイムチャート {project.WatchItems.Count}/{ProjectFactory.MaxTimeChartTargets} / トラップ {project.Traps.Count}";
    }

    private void Generate_Click(object sender, RoutedEventArgs e) => Generate(showQrScreen: true);

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    private void BackToEditor_Click(object sender, RoutedEventArgs e) => ShowInputScreen();

    private void DevicesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleMoveShortcut(e, _devicesGrid, _devices, "デバイス"))
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.C)
        {
            CopySelectedDevicesToClipboard();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V)
        {
            PasteDevicesFromClipboard();
            e.Handled = true;
        }
    }

    private bool TryHandleMoveShortcut<T>(
        KeyEventArgs e,
        DataGrid grid,
        ObservableCollection<T> source,
        string label)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.Alt)
        {
            return false;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Up)
        {
            MoveSelectedRows(grid, source, -1, label);
            e.Handled = true;
            return true;
        }

        if (key == Key.Down)
        {
            MoveSelectedRows(grid, source, 1, label);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private void CopySelectedDevicesToClipboard()
    {
        _devicesGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        _devicesGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var rows = _devicesGrid.SelectedItems
            .OfType<DeviceRow>()
            .OrderBy(row => _devices.IndexOf(row))
            .ToList();
        if (rows.Count == 0 && _devicesGrid.CurrentItem is DeviceRow current)
        {
            rows.Add(current);
        }

        if (rows.Count == 0)
        {
            return;
        }

        Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(row =>
            $"{row.Address}\t{row.DataType}")));
        SetStatus($"デバイス {rows.Count} 行をコピーしました");
    }

    private void PasteDevicesFromClipboard()
    {
        if (!Clipboard.ContainsText())
        {
            return;
        }

        var rows = ParseDeviceClipboardRows(Clipboard.GetText()).ToList();
        if (rows.Count == 0)
        {
            SetStatus("貼り付けできるデバイス行がありません", isError: true);
            return;
        }

        _devicesGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        _devicesGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var startIndex = _devicesGrid.SelectedItem is DeviceRow selected
            ? _devices.IndexOf(selected)
            : _devices.Count;
        if (startIndex < 0)
        {
            startIndex = _devices.Count;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var targetIndex = startIndex + index;
            if (targetIndex < _devices.Count)
            {
                _devices[targetIndex] = rows[index];
            }
            else
            {
                _devices.Add(rows[index]);
            }
        }

        _devicesGrid.SelectedItems.Clear();
        foreach (var row in rows)
        {
            _devicesGrid.SelectedItems.Add(row);
        }

        try
        {
            var count = TimeChartAddresses().Count;
            SetStatus($"デバイス {rows.Count} 行を貼り付けました / タイムチャート {count}/{ProjectFactory.MaxTimeChartTargets}");
        }
        catch (ArgumentException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private IEnumerable<DeviceRow> ParseDeviceClipboardRows(string text)
    {
        var isFirstRow = true;
        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = SplitDeviceClipboardLine(line);
            if (isFirstRow && IsDeviceClipboardHeader(fields))
            {
                isFirstRow = false;
                continue;
            }

            isFirstRow = false;
            var address = fields[0].Trim();
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            var dataType = fields.Length > 1
                ? NormalizeDeviceDataType(fields[1], address)
                : ProjectFactory.GuessDataType(address, Selected(_vendor));
            yield return new DeviceRow { Address = address, DataType = dataType };
        }
    }

    private static string[] SplitDeviceClipboardLine(string line) =>
        line.Contains('\t', StringComparison.Ordinal)
            ? line.Split('\t')
            : line.Split(',');

    private static bool IsDeviceClipboardHeader(IReadOnlyList<string> fields)
    {
        if (fields.Count == 0)
        {
            return false;
        }

        var first = fields[0].Trim();
        var second = fields.Count > 1 ? fields[1].Trim() : "";
        return first.Equals("アドレス", StringComparison.OrdinalIgnoreCase) ||
               first.Equals("Address", StringComparison.OrdinalIgnoreCase) ||
               second.Equals("Data type", StringComparison.OrdinalIgnoreCase);
    }

    private string NormalizeDeviceDataType(string text, string address)
    {
        var value = text.Trim();
        return ProjectFactory.DeviceDataTypes.FirstOrDefault(dataType =>
                   dataType.Equals(value, StringComparison.OrdinalIgnoreCase))
               ?? ProjectFactory.GuessDataType(address, Selected(_vendor));
    }

    private static bool ParseClipboardBoolean(string text)
    {
        var value = text.Trim();
        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("checked", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("有効", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("○", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("〇", StringComparison.OrdinalIgnoreCase);
    }

    private void WatchGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleMoveShortcut(e, _watchGrid, _watches, "タイムチャート"))
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.C)
        {
            CopySelectedWatchItemsToClipboard();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V)
        {
            PasteWatchItemsFromClipboard();
            e.Handled = true;
        }
    }

    private void CopySelectedWatchItemsToClipboard()
    {
        _watchGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        _watchGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var rows = SelectedRows(_watchGrid, _watches);
        if (rows.Count == 0)
        {
            return;
        }

        Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(row => row.Address)));
        SetStatus($"タイムチャート {rows.Count} 行をコピーしました");
    }

    private void PasteWatchItemsFromClipboard()
    {
        if (!Clipboard.ContainsText())
        {
            return;
        }

        var rows = ParseWatchClipboardRows(Clipboard.GetText()).ToList();
        if (rows.Count == 0)
        {
            SetStatus("貼り付けできるタイムチャート行がありません", isError: true);
            return;
        }

        _watchGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        _watchGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var startIndex = SelectedIndexOrAppend(_watchGrid, _watches);
        var candidate = _watches.ToList();
        ApplyRows(candidate, startIndex, rows);
        var uniqueCount = UniqueWatchAddressCount(candidate);
        if (uniqueCount > ProjectFactory.MaxTimeChartTargets)
        {
            SetStatus($"タイムチャートに追加できるのは最大 {ProjectFactory.MaxTimeChartTargets} チャンネルです。", isError: true);
            return;
        }

        ApplyRows(_watches, startIndex, rows);
        SelectRows(_watchGrid, rows);
        SetStatus($"タイムチャート {rows.Count} 行を貼り付けました / {uniqueCount}/{ProjectFactory.MaxTimeChartTargets}");
    }

    private static IEnumerable<WatchRow> ParseWatchClipboardRows(string text)
    {
        var isFirstRow = true;
        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var fields = SplitClipboardLine(rawLine.TrimEnd('\r'));
            if (fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (isFirstRow && IsAddressClipboardHeader(fields))
            {
                isFirstRow = false;
                continue;
            }

            isFirstRow = false;
            var address = fields[0].Trim();
            if (!string.IsNullOrWhiteSpace(address))
            {
                yield return new WatchRow { Address = address };
            }
        }
    }

    private static int UniqueWatchAddressCount(IEnumerable<WatchRow> rows) =>
        rows.Where(row => !string.IsNullOrWhiteSpace(row.Address))
            .Select(row => row.Address.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private void TrapsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleMoveShortcut(e, _trapsGrid, _traps, "トラップ"))
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.C)
        {
            CopySelectedTrapsToClipboard();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V)
        {
            PasteTrapsFromClipboard();
            e.Handled = true;
        }
    }

    private void CopySelectedTrapsToClipboard()
    {
        _trapsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        _trapsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var rows = SelectedRows(_trapsGrid, _traps);
        if (rows.Count == 0)
        {
            return;
        }

        Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(row =>
            $"{row.Address}\t{row.ConditionDisplayText}\t{row.Threshold}\t{(row.Enabled ? "TRUE" : "FALSE")}")));
        SetStatus($"トラップ {rows.Count} 行をコピーしました");
    }

    private void PasteTrapsFromClipboard()
    {
        if (!Clipboard.ContainsText())
        {
            return;
        }

        var rows = ParseTrapClipboardRows(Clipboard.GetText()).ToList();
        if (rows.Count == 0)
        {
            SetStatus("貼り付けできるトラップ行がありません", isError: true);
            return;
        }

        _trapsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        _trapsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var startIndex = SelectedIndexOrAppend(_trapsGrid, _traps);
        ApplyRows(_traps, startIndex, rows);
        SelectRows(_trapsGrid, rows);
        SetStatus($"トラップ {rows.Count} 行を貼り付けました");
    }

    private IEnumerable<TrapRow> ParseTrapClipboardRows(string text)
    {
        var isFirstRow = true;
        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var fields = SplitClipboardLine(rawLine.TrimEnd('\r'));
            if (fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (isFirstRow && IsTrapClipboardHeader(fields))
            {
                isFirstRow = false;
                continue;
            }

            isFirstRow = false;
            var address = fields[0].Trim();
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            var row = new TrapRow();
            row.SetVendor(Selected(_vendor));
            row.Address = address;
            row.Condition = fields.Length > 1
                ? NormalizeTrapCondition(fields[1], address)
                : ProjectFactory.DefaultTrapConditionForAddress(address, Selected(_vendor));
            if (fields.Length > 2 && !string.IsNullOrWhiteSpace(fields[2]))
            {
                row.Threshold = fields[2].Trim();
            }

            row.Enabled = fields.Length < 4 || string.IsNullOrWhiteSpace(fields[3]) || ParseClipboardBoolean(fields[3]);
            yield return row;
        }
    }

    private string NormalizeTrapCondition(string text, string address)
    {
        var value = text.Trim();
        var normalized = value.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        var condition = ProjectFactory.TrapConditions.FirstOrDefault(item =>
                            item.Equals(value, StringComparison.OrdinalIgnoreCase))
                        ?? normalized switch
                        {
                            "立上り(off→on)" or "立上り(off->on)" or "立上り" or "立ち上がり" or "risingedge" => "Rise",
                            "立下り(on→off)" or "立下り(on->off)" or "立下り" or "立ち下がり" or "fallingedge" => "Fall",
                            "変化" or "change" => "Change",
                            ">=" or "≧" or "以上" or "greaterorequal" => "GreaterOrEqual",
                            "<=" or "≦" or "以下" or "lessorequal" => "LessOrEqual",
                            "==" or "=" or "等しい" or "equal" => "Equal",
                            "!=" or "<>" or "≠" or "不一致" or "notequal" => "NotEqual",
                            _ => ProjectFactory.DefaultTrapConditionForAddress(address, Selected(_vendor)),
                        };
        return ProjectFactory.CoerceTrapConditionForAddress(address, condition, Selected(_vendor));
    }

    private static string[] SplitClipboardLine(string line) =>
        line.Contains('\t', StringComparison.Ordinal)
            ? line.Split('\t')
            : line.Split(',');

    private static bool IsAddressClipboardHeader(IReadOnlyList<string> fields)
    {
        var first = fields[0].Trim();
        return first.Equals("アドレス", StringComparison.OrdinalIgnoreCase) ||
               first.Equals("Address", StringComparison.OrdinalIgnoreCase) ||
               first.Equals("タイムチャート", StringComparison.OrdinalIgnoreCase) ||
               first.Equals("Time chart", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrapClipboardHeader(IReadOnlyList<string> fields)
    {
        if (fields.Count == 0)
        {
            return false;
        }

        var first = fields[0].Trim();
        var second = fields.Count > 1 ? fields[1].Trim() : "";
        return first.Equals("アドレス", StringComparison.OrdinalIgnoreCase) ||
               first.Equals("Address", StringComparison.OrdinalIgnoreCase) ||
               second.Equals("検知条件", StringComparison.OrdinalIgnoreCase) ||
               second.Equals("Condition", StringComparison.OrdinalIgnoreCase);
    }

    private static List<T> SelectedRows<T>(DataGrid grid, ObservableCollection<T> source)
    {
        var rows = grid.SelectedItems
            .OfType<T>()
            .OrderBy(row => source.IndexOf(row))
            .ToList();
        if (rows.Count == 0 && grid.CurrentItem is T current)
        {
            rows.Add(current);
        }

        return rows;
    }

    private static int SelectedIndexOrAppend<T>(DataGrid grid, ObservableCollection<T> source)
    {
        var index = grid.SelectedItem is T selected ? source.IndexOf(selected) : source.Count;
        return index < 0 ? source.Count : index;
    }

    private static void ApplyRows<T>(IList<T> target, int startIndex, IReadOnlyList<T> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var targetIndex = startIndex + index;
            if (targetIndex < target.Count)
            {
                target[targetIndex] = rows[index];
            }
            else
            {
                target.Add(rows[index]);
            }
        }
    }

    private void MoveSelectedRows<T>(
        DataGrid grid,
        ObservableCollection<T> source,
        int direction,
        string label)
    {
        if (source.Count <= 1)
        {
            return;
        }

        grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var rows = SelectedRows(grid, source);
        if (rows.Count == 0)
        {
            return;
        }

        var indices = rows.Select(source.IndexOf).Where(index => index >= 0).ToArray();
        if (indices.Length == 0)
        {
            return;
        }

        if ((direction < 0 && indices.Min() == 0) ||
            (direction > 0 && indices.Max() == source.Count - 1))
        {
            SetStatus($"{label}行はこれ以上移動できません");
            return;
        }

        var orderedRows = direction < 0
            ? rows.OrderBy(source.IndexOf).ToList()
            : rows.OrderByDescending(source.IndexOf).ToList();
        foreach (var row in orderedRows)
        {
            var currentIndex = source.IndexOf(row);
            if (currentIndex < 0)
            {
                continue;
            }

            source.Move(currentIndex, currentIndex + direction);
        }

        SelectRows(grid, rows);
        grid.ScrollIntoView(rows[0]);
        SetStatus($"{label} {rows.Count} 行を{(direction < 0 ? "上" : "下")}へ移動しました");
    }

    private static void SelectRows<T>(DataGrid grid, IEnumerable<T> rows)
    {
        grid.SelectedItems.Clear();
        foreach (var row in rows)
        {
            grid.SelectedItems.Add(row);
        }
    }

    private void QrOptionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag })
        {
            return;
        }

        var parts = tag.Split(':', 2);
        if (parts.Length != 2)
        {
            return;
        }

        switch (parts[0])
        {
            case "chunk":
                _chunkSize = Clamp(ParseInt(parts[1], _chunkSize), 200, 2400);
                break;
            case "size":
                _displaySize = Clamp(ParseInt(parts[1], _displaySize), 240, 1200);
                break;
            case "ec":
                _errorCorrection = parts[1] is "M" or "Q" or "H" ? parts[1] : "L";
                break;
        }

        UpdateQrMenuChecks();
        Generate(_qrView.Visibility == Visibility.Visible);
    }

    private void UpdateQrMenuChecks() => UpdateCheckedMenuItems(_qrMenu.Items);

    private void UpdateCheckedMenuItems(ItemCollection items)
    {
        foreach (var menuItem in items.OfType<MenuItem>())
        {
            if (menuItem.Tag is string tag)
            {
                menuItem.IsChecked = tag switch
                {
                    _ when tag == $"chunk:{_chunkSize}" => true,
                    _ when tag == $"size:{_displaySize}" => true,
                    _ when tag == $"ec:{_errorCorrection}" => true,
                    _ => false,
                };
            }

            UpdateCheckedMenuItems(menuItem.Items);
        }
    }

    private void PrevQr_Click(object sender, RoutedEventArgs e)
    {
        if (_chunks.Count == 0)
        {
            return;
        }

        _currentIndex = (_currentIndex + _chunks.Count - 1) % _chunks.Count;
        ShowCurrentQr();
        SetStatus($"QR 表示: {_currentIndex + 1}/{_chunks.Count}");
    }

    private void NextQr_Click(object sender, RoutedEventArgs e)
    {
        if (_chunks.Count == 0)
        {
            return;
        }

        _currentIndex = (_currentIndex + 1) % _chunks.Count;
        ShowCurrentQr();
        SetStatus($"QR 表示: {_currentIndex + 1}/{_chunks.Count}");
    }

    private void ShowJson_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastJson))
        {
            Generate(showQrScreen: false);
        }

        var window = new Window
        {
            Title = $"Project JSON - {_lastJson.Length.ToString("N0", CultureInfo.InvariantCulture)} chars",
            Width = 720,
            Height = 680,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)FindResource("PanelBg"),
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var meta = new TextBlock
        {
            Text = $"文字数: {_lastJson.Length.ToString("N0", CultureInfo.InvariantCulture)} / UTF-8: {Encoding.UTF8.GetByteCount(_lastJson).ToString("N0", CultureInfo.InvariantCulture)} bytes",
            Foreground = (Brush)FindResource("TextMuted"),
            Padding = new Thickness(14, 10, 14, 8),
        };
        Grid.SetRow(meta, 0);
        grid.Children.Add(meta);

        var textBox = new TextBox
        {
            Text = _lastJson,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14),
        };
        Grid.SetRow(textBox, 1);
        grid.Children.Add(textBox);

        window.Content = grid;

        window.ShowDialog();
    }

    private void LoadJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json|All files (*.*)|*.*" };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            LoadProjectJson(File.ReadAllText(dialog.FileName));
            Generate(showQrScreen: false);
            ShowInputScreen();
            SetStatus($"JSON 読み込み: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            SetStatus($"JSON 読み込みエラー: {ex.Message}", isError: true);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", DefaultExt = "json" };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            File.WriteAllBytes(dialog.FileName, ProjectQrPayload.ProjectJsonBytes(BuildProject()));
            SetStatus($"JSON 保存: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            SetStatus($"JSON 保存エラー: {ex.Message}", isError: true);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveQrImages_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_chunks.Count == 0)
            {
                Generate(showQrScreen: false);
            }

            if (_chunks.Count == 0)
            {
                return;
            }

            var dialog = new OpenFolderDialog { Title = "QR PNG の保存先フォルダーを選択" };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            foreach (var chunk in _chunks)
            {
                using var bitmap = MakeQrBitmap(chunk.Text);
                var path = Path.Combine(dialog.FolderName, $"plcio-{chunk.Session}-{chunk.Index:00}-of-{chunk.Total:00}.png");
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }

            SetStatus($"QR PNG 保存: {_chunks.Count} file(s)");
        }
        catch (Exception ex)
        {
            SetStatus($"QR PNG 保存エラー: {ex.Message}", isError: true);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddDevice_Click(object sender, RoutedEventArgs e)
    {
        var row = new DeviceRow { Address = "", DataType = "Bit" };
        _devices.Add(row);
        SelectNewRow(_devicesGrid, row);
    }

    private void MoveDeviceUp_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_devicesGrid, _devices, -1, "デバイス");

    private void MoveDeviceDown_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_devicesGrid, _devices, 1, "デバイス");

    private void DeleteDevice_Click(object sender, RoutedEventArgs e) => RemoveSelectedRows(_devicesGrid, _devices);

    private void AddWatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TimeChartAddresses().Count >= ProjectFactory.MaxTimeChartTargets)
            {
                SetStatus($"タイムチャートに追加できるのは最大 {ProjectFactory.MaxTimeChartTargets} チャンネルです。", isError: true);
                return;
            }
        }
        catch (ArgumentException ex)
        {
            SetStatus(ex.Message, isError: true);
            return;
        }

        var row = new WatchRow();
        _watches.Add(row);
        SelectNewRow(_watchGrid, row);
    }

    private void MoveWatchUp_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_watchGrid, _watches, -1, "タイムチャート");

    private void MoveWatchDown_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_watchGrid, _watches, 1, "タイムチャート");

    private void DeleteWatch_Click(object sender, RoutedEventArgs e) => RemoveSelectedRows(_watchGrid, _watches);

    private void AddTrap_Click(object sender, RoutedEventArgs e)
    {
        var row = new TrapRow();
        row.SetVendor(Selected(_vendor));
        _traps.Add(row);
        SelectNewRow(_trapsGrid, row);
    }

    private void MoveTrapUp_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_trapsGrid, _traps, -1, "トラップ");

    private void MoveTrapDown_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_trapsGrid, _traps, 1, "トラップ");

    private void DeleteTrap_Click(object sender, RoutedEventArgs e) => RemoveSelectedRows(_trapsGrid, _traps);

    private static void SelectNewRow(DataGrid grid, object row)
    {
        grid.SelectedItem = row;
        grid.ScrollIntoView(row);
        if (grid.Columns.Count > 0)
        {
            grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[0]);
        }
    }

    private static void RemoveSelectedRows<T>(DataGrid grid, ObservableCollection<T> source)
    {
        var selectedRows = grid.SelectedItems.OfType<T>().ToList();
        foreach (var row in selectedRows)
        {
            source.Remove(row);
        }
    }

    private void LoadProjectJson(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;

        _projectName.Text = ReadString(root, "name", "PLC QR Project");

        var connection = root.GetProperty("connection");
        SelectItem(_vendor, ReadString(connection, "vendor", "Melsec"));
        ApplyVendorDefaults();
        SelectItem(_connectionMode, ReadString(connection, "connectionMode", "Real"));
        _host.Text = ReadString(connection, "host", "192.168.250.100");
        _port.Text = ReadInt(connection, "port", 1025).ToString(CultureInfo.InvariantCulture);
        SelectItem(_model, ReadString(connection, "machineLabel", Selected(_model)));
        SelectItem(_keyenceMode, ReadString(connection, "keyenceDeviceMode", "Normal"));
        SelectItem(_transport, ReadString(connection, "transportMode", "Tcp"));
        _interval.Text = ReadInt(connection, "monitorIntervalMs", 500).ToString(CultureInfo.InvariantCulture);
        _timeout.Text = ReadInt(connection, "timeoutMs", 2000).ToString(CultureInfo.InvariantCulture);
        _network.Text = ReadInt(connection, "network", 0).ToString(CultureInfo.InvariantCulture);
        _station.Text = ReadInt(connection, "station", 255).ToString(CultureInfo.InvariantCulture);
        _moduleIo.Text = ReadInt(connection, "moduleIo", 1023).ToString(CultureInfo.InvariantCulture);
        _multidrop.Text = ReadInt(connection, "multidrop", 0).ToString(CultureInfo.InvariantCulture);

        if (root.TryGetProperty("settings", out var settings))
        {
            SelectItem(_density, ReadString(settings, "blockDisplayDensity", "Compact"));
        }

        var watchItems = ReadWatchItems(root);

        if (root.TryGetProperty("devices", out var devicesElement) &&
            devicesElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            _devices.Clear();
            foreach (var device in devicesElement.EnumerateArray())
            {
                var address = ReadString(device, "address", "");
                _devices.Add(new DeviceRow
                {
                    Address = address,
                    DataType = ReadString(device, "dataType", "Int16"),
                });
            }
        }

        _watches.Clear();
        foreach (var address in watchItems)
        {
            _watches.Add(new WatchRow { Address = address });
        }

        if (root.TryGetProperty("traps", out var trapsElement) &&
            trapsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            _traps.Clear();
            foreach (var trap in trapsElement.EnumerateArray())
            {
                var threshold = trap.TryGetProperty("threshold", out var thresholdValue) &&
                                thresholdValue.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? thresholdValue.GetDouble().ToString(CultureInfo.InvariantCulture)
                    : "";
                var enabled = !(trap.TryGetProperty("enabled", out var enabledValue) &&
                                enabledValue.ValueKind == System.Text.Json.JsonValueKind.False);

                var row = new TrapRow();
                row.SetVendor(Selected(_vendor));
                row.Address = ReadString(trap, "address", "");
                row.Condition = ReadString(trap, "condition", "Change");
                row.Threshold = threshold;
                row.Enabled = enabled;
                _traps.Add(row);
            }
        }
    }

    private static IReadOnlyList<string> ReadWatchItems(System.Text.Json.JsonElement root)
    {
        if (!root.TryGetProperty("watchItems", out var watchItems) ||
            watchItems.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return [];
        }

        return watchItems
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToList();
    }

    private static string Selected(ComboBox comboBox) => comboBox.SelectedItem?.ToString() ?? "";

    private static void SelectItem(ComboBox comboBox, string value)
    {
        if (comboBox.Items.Contains(value))
        {
            comboBox.SelectedItem = value;
        }
    }

    private static int ParseRange(TextBox textBox, int fallback, int min, int max)
    {
        var value = Clamp(ParseInt(textBox.Text, fallback), min, max);
        textBox.Text = value.ToString(CultureInfo.InvariantCulture);
        return value;
    }

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    private static string ReadString(System.Text.Json.JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static int ReadInt(System.Text.Json.JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == System.Text.Json.JsonValueKind.Number &&
        value.TryGetInt32(out var result)
            ? result
            : fallback;
}
