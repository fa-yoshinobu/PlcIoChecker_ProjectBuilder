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
    public abstract class DataTypedAddressRow : INotifyPropertyChanged
    {
        private string _address = "";
        private string _dataType = "Bit";
        private string _keyenceDeviceMode = "Normal";
        private string _vendor = "Melsec";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Address
        {
            get => _address;
            set
            {
                var next = value;
                if (_address == next)
                {
                    return;
                }

                _address = next;
                OnPropertyChanged();
                OnAddressChanged();
                RefreshDataTypeOptions();
            }
        }

        public string DataType
        {
            get => _dataType;
            set
            {
                var next = NormalizeDataType(value);
                if (_dataType == next)
                {
                    return;
                }

                _dataType = next;
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<string> AvailableDataTypes =>
            ProjectFactory.DeviceDataTypesForAddress(Address, Vendor, KeyenceDeviceMode);

        protected string Vendor => _vendor;

        protected string KeyenceDeviceMode => _keyenceDeviceMode;

        public void SetVendor(string vendor) => SetDeviceContext(vendor, "Normal");

        public void SetDeviceContext(string vendor, string keyenceDeviceMode)
        {
            var nextVendor = string.IsNullOrWhiteSpace(vendor) ? "Melsec" : vendor;
            var nextMode = string.IsNullOrWhiteSpace(keyenceDeviceMode) ? "Normal" : keyenceDeviceMode;
            if (_vendor == nextVendor && _keyenceDeviceMode == nextMode)
            {
                return;
            }

            _vendor = nextVendor;
            _keyenceDeviceMode = nextMode;
            OnDeviceContextChanged();
            RefreshDataTypeOptions();
        }

        public void EnsureDataTypeAllowed() => DataType = NormalizeDataType(DataType);

        protected virtual void OnAddressChanged()
        {
        }

        protected virtual void OnDeviceContextChanged()
        {
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void RefreshDataTypeOptions()
        {
            OnPropertyChanged(nameof(AvailableDataTypes));
            EnsureDataTypeAllowed();
        }

        private string NormalizeDataType(string value)
        {
            var allowed = AvailableDataTypes;
            var match = allowed.FirstOrDefault(dataType => dataType.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }

            return string.IsNullOrWhiteSpace(Address)
                ? "Bit"
                : ProjectFactory.GuessDataType(Address, Vendor, KeyenceDeviceMode);
        }
    }

    public sealed class DeviceRow : DataTypedAddressRow
    {
    }

    public sealed class WatchRow : DataTypedAddressRow
    {
    }

    public sealed class TrapConditionOption(string value)
    {
        public string Value { get; } = value;
        public string DisplayText { get; } = MainWindow.TrapConditionDisplayText(value);
    }

    public sealed class TrapRow : DataTypedAddressRow
    {
        private string _condition = "Change";
        private string _threshold = "";

        public string Condition
        {
            get => _condition;
            set => SetCondition(ProjectFactory.CoerceTrapConditionForAddress(Address, value, Vendor, KeyenceDeviceMode));
        }

        public string ConditionDisplayText => TrapConditionDisplayText(Condition);

        public IReadOnlyList<TrapConditionOption> AvailableConditionOptions =>
            ProjectFactory.TrapConditionsForAddress(Address, Vendor, KeyenceDeviceMode)
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

        protected override void OnAddressChanged()
        {
            OnPropertyChanged(nameof(AvailableConditionOptions));
            CoerceCondition();
        }

        protected override void OnDeviceContextChanged()
        {
            OnPropertyChanged(nameof(AvailableConditionOptions));
            CoerceCondition();
        }

        private void CoerceCondition() => SetCondition(ProjectFactory.CoerceTrapConditionForAddress(Address, Condition, Vendor, KeyenceDeviceMode));

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
    private bool _useEnglish = true;

    private static bool UseEnglishLabels { get; set; } = true;

    private static string TrapConditionDisplayText(string condition) => condition switch
    {
        "Rise" => UseEnglishLabels ? "Rise (OFF->ON)" : "立上り (OFF->ON)",
        "Fall" => UseEnglishLabels ? "Fall (ON->OFF)" : "立下り (ON->OFF)",
        "Change" => UseEnglishLabels ? "Change" : "変化",
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
        ApplyLanguage();

        _vendor.SelectionChanged += (_, _) => ApplyVendorDefaults();
        _keyenceMode.SelectionChanged += (_, _) => ApplyDeviceContextToRows();
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
        _devicesGrid.Columns.Add(DeviceDataTypeColumn<DeviceRow>());

        _watchGrid.ItemsSource = _watches;
        _watchGrid.PreviewKeyDown += WatchGrid_PreviewKeyDown;
        _watchGrid.ToolTip = "Excel とタブ区切りでコピー/貼り付けできます。列は アドレス です。";
        _watchGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "アドレス",
            Binding = new Binding(nameof(WatchRow.Address)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _watchGrid.Columns.Add(DeviceDataTypeColumn<WatchRow>());

        _trapsGrid.ItemsSource = _traps;
        _trapsGrid.PreviewKeyDown += TrapsGrid_PreviewKeyDown;
        _trapsGrid.ToolTip = "Excel とタブ区切りでコピー/貼り付けできます。列は アドレス / 検知条件 / しきい値 / 有効 です。";
        _trapsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "アドレス",
            Binding = new Binding(nameof(TrapRow.Address)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _trapsGrid.Columns.Add(DeviceDataTypeColumn<TrapRow>());
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

    private static DataGridTemplateColumn DeviceDataTypeColumn<T>() where T : DataTypedAddressRow =>
        new()
        {
            Header = "Data type",
            CellTemplate = DeviceDataTypeCellTemplate<T>(),
            CellEditingTemplate = DeviceDataTypeEditingTemplate<T>(),
            Width = new DataGridLength(170),
        };

    private static DataTemplate DeviceDataTypeCellTemplate<T>() where T : DataTypedAddressRow
    {
        var textBlock = new FrameworkElementFactory(typeof(TextBlock));
        textBlock.SetBinding(TextBlock.TextProperty, new Binding(nameof(DataTypedAddressRow.DataType)));
        textBlock.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        return new DataTemplate { VisualTree = textBlock };
    }

    private static DataTemplate DeviceDataTypeEditingTemplate<T>() where T : DataTypedAddressRow
    {
        var comboBox = new FrameworkElementFactory(typeof(ComboBox));
        comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(DataTypedAddressRow.AvailableDataTypes)));
        comboBox.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(DataTypedAddressRow.DataType))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        return new DataTemplate { VisualTree = comboBox };
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
        _watches.Clear();
        _traps.Clear();
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

        ApplyDeviceContextToRows();
    }

    private void ApplyDeviceContextToRows()
    {
        var vendor = Selected(_vendor);
        var keyenceDeviceMode = SelectedKeyenceDeviceMode();

        foreach (var row in _devices)
        {
            row.SetDeviceContext(vendor, keyenceDeviceMode);
            if (string.IsNullOrWhiteSpace(row.Address))
            {
                continue;
            }

            row.Address = row.Address.Trim().ToUpperInvariant();
            if (IsSupportedDeviceAddress(row.Address, vendor, keyenceDeviceMode))
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
            if (IsSupportedDeviceAddress(row.Address, vendor, keyenceDeviceMode))
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
            if (IsSupportedDeviceAddress(trap.Address, vendor, keyenceDeviceMode))
            {
                trap.EnsureDataTypeAllowed();
                trap.Condition = ProjectFactory.CoerceTrapConditionForAddress(trap.Address, trap.Condition, vendor, keyenceDeviceMode);
            }
        }

        _devicesGrid.Items.Refresh();
        _watchGrid.Items.Refresh();
        _trapsGrid.Items.Refresh();
        UpdateDeviceValidationStatus();
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
            SetStatus(L($"QR 生成完了: {_chunks.Count} page(s)", $"QR generated: {_chunks.Count} page(s)"));

            if (showQrScreen)
            {
                ShowQrScreen();
            }
        }
        catch (Exception ex)
        {
            SetStatus(L($"生成エラー: {ex.Message}", $"Generation error: {ex.Message}"), isError: true);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private PlcProject BuildProject()
    {
        NormalizeGridRows();

        return ProjectFactory.MakeProject(new ProjectInput(
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
            TrapsText: TrapsText()));
    }

    private void ShowCurrentQr()
    {
        if (_chunks.Count == 0)
        {
            _qrImage.Source = null;
            _pageLabel.Text = L("QR 未生成", "QR not generated");
            _qrMeta.Text = "";
            _prevQrButton.Visibility = Visibility.Collapsed;
            _nextQrButton.Visibility = Visibility.Collapsed;
            return;
        }

        var chunk = _chunks[_currentIndex];
        _pageLabel.Text = $"QR {chunk.Index} / {chunk.Total}";
        var pageButtonVisibility = chunk.Total > 1 ? Visibility.Visible : Visibility.Collapsed;
        _prevQrButton.Visibility = pageButtonVisibility;
        _nextQrButton.Visibility = pageButtonVisibility;
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
                    ? ProjectFactory.GuessDataType(address, Selected(_vendor), SelectedKeyenceDeviceMode())
                    : CoerceDataTypeForAddress(row.DataType.Trim(), address);
                return $"{address},{dataType}";
            }));

    private string WatchText() => string.Join(Environment.NewLine,
        _watches
            .Where(row => !string.IsNullOrWhiteSpace(row.Address))
            .Select(row =>
            {
                var address = row.Address.Trim();
                var dataType = string.IsNullOrWhiteSpace(row.DataType)
                    ? ProjectFactory.GuessDataType(address, Selected(_vendor), SelectedKeyenceDeviceMode())
                    : row.DataType.Trim();
                return $"{address},{dataType}";
            }));

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
            .Select(row =>
            {
                var address = row.Address.Trim();
                var dataType = string.IsNullOrWhiteSpace(row.DataType)
                    ? ProjectFactory.GuessDataType(address, Selected(_vendor), SelectedKeyenceDeviceMode())
                    : CoerceDataTypeForAddress(row.DataType.Trim(), address);
                return $"{address},{dataType},{row.Condition.Trim()},{row.Threshold.Trim()},{(row.Enabled ? "true" : "false")}";
            }));

    private void ShowQrScreen()
    {
        _inputView.Visibility = Visibility.Collapsed;
        _navInputArea.Visibility = Visibility.Collapsed;
        _qrView.Visibility = Visibility.Visible;
        _navQrArea.Visibility = Visibility.Visible;
    }

    private void ShowInputScreen()
    {
        _qrView.Visibility = Visibility.Collapsed;
        _navQrArea.Visibility = Visibility.Collapsed;
        _inputView.Visibility = Visibility.Visible;
        _navInputArea.Visibility = Visibility.Visible;
    }

    private void CommitGridEdits()
    {
        foreach (var grid in new[] { _devicesGrid, _watchGrid, _trapsGrid })
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        }
    }

    private string L(string ja, string en) => _useEnglish ? en : ja;

    private void ApplyLanguage()
    {
        UseEnglishLabels = _useEnglish;
        _langLabel.Text = _useEnglish ? "EN" : "JP";

        _fileMenu.Header = L("ファイル", "File");
        _loadJsonMenuItem.Header = L("JSON 読み込み", "Load JSON");
        _saveJsonMenuItem.Header = L("JSON 保存", "Save JSON");
        _showJsonMenuItem.Header = L("JSON 表示", "Show JSON");
        _qrMenu.Header = "QR";
        _qrMenu.ToolTip = L("QR の分割、表示サイズ、誤り訂正レベルを設定します。", "Configure QR chunking, display size, and error correction level.");
        _qrChunkSizeMenu.Header = L("分割サイズ", "Chunk size");
        _qrChunkSizeMenu.ToolTip = L("1つのQRに入れる文字数です。小さいほどページ数は増えますが、QRは粗くなり読み取りやすくなります。", "Characters per QR page. Smaller values create more pages but easier-to-read QR codes.");
        _qrDisplaySizeMenu.Header = L("表示サイズ", "Display size");
        _qrDisplaySizeMenu.ToolTip = L("画面上に表示するQR画像の大きさです。読み取り端末や画面サイズに合わせて変更します。", "QR image size on screen. Adjust for the scanner device and display size.");
        _qrErrorCorrectionMenu.Header = L("誤り訂正", "Error correction");
        _qrErrorCorrectionMenu.ToolTip = L("QRが一部欠けたり汚れたりしても復元できる強さです。強くするほど復元力は上がりますが、QRは細かくなります。", "Recovery strength when a QR code is partly damaged or dirty. Higher levels recover better but make the QR denser.");
        _helpMenu.Header = L("ヘルプ", "Help");
        _aboutMenuItem.Header = L("バージョン情報", "About");

        _generateQrButton.Content = L("▦  QR 生成", "▦  Generate QR");
        _backToEditorButton.Content = L("←  入力へ戻る", "←  Back to editor");
        _prevQrButton.Content = L("←  前へ", "←  Previous");
        _nextQrButton.Content = L("次へ  →", "Next  →");
        _saveQrImagesButton.Content = L("↓  PNG 保存", "↓  Save PNG");

        _devicesTab.Header = L("デバイス", "Devices");
        _watchTab.Header = L("タイムチャート", "Time Chart");
        _trapsTab.Header = L("トラップ", "Traps");
        _projectTab.Header = L("プロジェクト/接続", "Project / Connection");

        _devicesTitle.Text = L("デバイス", "Devices");
        _devicesMeta.Text = L("アドレスとデータ型を編集します", "Edit addresses and data types.");
        _watchTitle.Text = L("タイムチャート対象", "Time Chart Targets");
        _watchMeta.Text = L($"Android のタイムチャートに表示するアドレスを指定します（最大{ProjectFactory.MaxTimeChartTargets}件）", $"Choose addresses shown in the Android time chart, up to {ProjectFactory.MaxTimeChartTargets}.");
        _trapsTitle.Text = L("トラップ", "Traps");
        _trapsMeta.Text = L("条件、しきい値、有効状態を編集します", "Edit conditions, thresholds, and enabled state.");

        _moveDeviceUpButton.Content = _moveWatchUpButton.Content = _moveTrapUpButton.Content = L("↑  上へ", "↑  Up");
        _moveDeviceDownButton.Content = _moveWatchDownButton.Content = _moveTrapDownButton.Content = L("↓  下へ", "↓  Down");
        _addDeviceButton.Content = _addWatchButton.Content = _addTrapButton.Content = L("+  行を追加", "+  Add row");
        _deleteDeviceButton.Content = _deleteWatchButton.Content = _deleteTrapButton.Content = L("−  削除", "−  Delete");

        _moveDeviceUpButton.ToolTip = L("選択したデバイス行を上へ移動します。Alt+↑ でも移動できます。", "Move selected device rows up. Alt+Up also works.");
        _moveDeviceDownButton.ToolTip = L("選択したデバイス行を下へ移動します。Alt+↓ でも移動できます。", "Move selected device rows down. Alt+Down also works.");
        _moveWatchUpButton.ToolTip = L("選択したタイムチャート行を上へ移動します。Alt+↑ でも移動できます。", "Move selected time chart rows up. Alt+Up also works.");
        _moveWatchDownButton.ToolTip = L("選択したタイムチャート行を下へ移動します。Alt+↓ でも移動できます。", "Move selected time chart rows down. Alt+Down also works.");
        _moveTrapUpButton.ToolTip = L("選択したトラップ行を上へ移動します。Alt+↑ でも移動できます。", "Move selected trap rows up. Alt+Up also works.");
        _moveTrapDownButton.ToolTip = L("選択したトラップ行を下へ移動します。Alt+↓ でも移動できます。", "Move selected trap rows down. Alt+Down also works.");

        _projectSectionTitle.Text = L("プロジェクト", "Project");
        _projectSectionMeta.Text = L("Android 側に渡す基本情報", "Basic information passed to Android.");
        _projectNameLabel.Text = L("プロジェクト名", "Project name");
        _vendorLabel.Text = L("メーカー", "Vendor");
        _modelLabel.Text = "CPU model";
        _keyenceModeLabel.Text = L("KEYENCE 表示", "KEYENCE display");

        _connectionSectionTitle.Text = L("接続", "Connection");
        _connectionSectionMeta.Text = L("PLC 通信設定", "PLC communication settings.");
        _connectionModeLabel.Text = "Connection mode";
        _hostLabel.Text = "IP address";
        _portLabel.Text = "Port";
        _transportLabel.Text = "Transport";
        _intervalLabel.Text = L("監視周期 ms", "Polling interval ms");
        _timeoutLabel.Text = "Timeout ms";

        _devicesGrid.ToolTip = L("Excel とタブ区切りでコピー/貼り付けできます。列は アドレス / Data type です。", "Copy and paste tab-separated rows from Excel. Columns are Address / Data type.");
        _watchGrid.ToolTip = L("Excel とタブ区切りでコピー/貼り付けできます。列は アドレス / Data type です。", "Copy and paste tab-separated rows from Excel. Columns are Address / Data type.");
        _trapsGrid.ToolTip = L("Excel とタブ区切りでコピー/貼り付けできます。列は アドレス / Data type / 検知条件 / しきい値 / 有効 です。", "Copy and paste tab-separated rows from Excel. Columns are Address / Data type / Condition / Threshold / Enabled.");
        if (_devicesGrid.Columns.Count >= 2)
        {
            _devicesGrid.Columns[0].Header = L("アドレス", "Address");
            _devicesGrid.Columns[1].Header = "Data type";
        }

        if (_watchGrid.Columns.Count >= 2)
        {
            _watchGrid.Columns[0].Header = L("アドレス", "Address");
            _watchGrid.Columns[1].Header = "Data type";
        }

        if (_trapsGrid.Columns.Count >= 5)
        {
            _trapsGrid.Columns[0].Header = L("アドレス", "Address");
            _trapsGrid.Columns[1].Header = "Data type";
            _trapsGrid.Columns[2].Header = L("検知条件", "Condition");
            _trapsGrid.Columns[3].Header = L("しきい値", "Threshold");
            _trapsGrid.Columns[4].Header = L("有効", "Enabled");
        }

        _trapsGrid.Items.Refresh();
        if (_chunks.Count == 0)
        {
            _pageLabel.Text = L("QR 未生成", "QR not generated");
        }
        else
        {
            ShowCurrentQr();
        }

        if (string.IsNullOrWhiteSpace(_statusText.Text) || _statusText.Text is "準備完了" or "Ready")
        {
            _statusText.Text = L("準備完了", "Ready");
        }
    }

    private void NormalizeGridRows()
    {
        foreach (var row in _devices.Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = row.Address.Trim().ToUpperInvariant();
            row.DataType = string.IsNullOrWhiteSpace(row.DataType)
                ? ProjectFactory.GuessDataType(row.Address, Selected(_vendor), SelectedKeyenceDeviceMode())
                : CoerceDataTypeForAddress(row.DataType, row.Address);
        }

        foreach (var row in _watches.Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = row.Address.Trim().ToUpperInvariant();
            row.DataType = string.IsNullOrWhiteSpace(row.DataType)
                ? ProjectFactory.GuessDataType(row.Address, Selected(_vendor), SelectedKeyenceDeviceMode())
                : CoerceDataTypeForAddress(row.DataType, row.Address);
        }

        foreach (var row in _traps.Where(row => !string.IsNullOrWhiteSpace(row.Address)))
        {
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = row.Address.Trim().ToUpperInvariant();
            row.DataType = string.IsNullOrWhiteSpace(row.DataType)
                ? ProjectFactory.GuessDataType(row.Address, Selected(_vendor), SelectedKeyenceDeviceMode())
                : CoerceDataTypeForAddress(row.DataType, row.Address);
            row.Condition = ProjectFactory.CoerceTrapConditionForAddress(row.Address, row.Condition, Selected(_vendor), SelectedKeyenceDeviceMode());
        }

        _devicesGrid.Items.Refresh();
        _watchGrid.Items.Refresh();
        _trapsGrid.Items.Refresh();
    }

    private void SetStatus(string message, bool isError = false)
    {
        _statusText.Text = message;
        _statusText.Foreground = isError
            ? (Brush)FindResource("ErrorFg")
            : (Brush)FindResource("TextMuted");
    }

    private void UpdateDeviceValidationStatus()
    {
        var vendor = Selected(_vendor);
        var keyenceDeviceMode = SelectedKeyenceDeviceMode();
        var invalidAddress = DeviceAddresses()
            .FirstOrDefault(address => !IsSupportedDeviceAddress(address, vendor, keyenceDeviceMode));

        if (invalidAddress is null)
        {
            SetStatus(L("Device check OK", "Device check OK"));
            return;
        }

        var context = vendor == "Keyence" ? $"{vendor} {keyenceDeviceMode}" : vendor;
        SetStatus($"Unsupported device for {context}: {invalidAddress}", isError: true);
    }

    private IEnumerable<string> DeviceAddresses() =>
        _devices.Select(row => row.Address)
            .Concat(_watches.Select(row => row.Address))
            .Concat(_traps.Select(row => row.Address))
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.Trim().ToUpperInvariant());

    private static bool IsSupportedDeviceAddress(string address, string vendor, string keyenceDeviceMode)
    {
        try
        {
            ProjectFactory.ValidateDeviceAddress(address, vendor, keyenceDeviceMode);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void SetSummary(PlcProject project)
    {
        _summaryText.Text = _useEnglish
            ? $"{project.Connection.Vendor} {project.Connection.MachineLabel} / Devices {project.Devices.Count} / Time Chart {project.WatchItems.Count}/{ProjectFactory.MaxTimeChartTargets} / Traps {project.Traps.Count}"
            : $"{project.Connection.Vendor} {project.Connection.MachineLabel} / デバイス {project.Devices.Count} / タイムチャート {project.WatchItems.Count}/{ProjectFactory.MaxTimeChartTargets} / トラップ {project.Traps.Count}";
    }

    private void Generate_Click(object sender, RoutedEventArgs e) => Generate(showQrScreen: true);

    private void LangButton_Click(object sender, RoutedEventArgs e)
    {
        _useEnglish = !_useEnglish;
        ApplyLanguage();
        SetStatus(L("表示言語: 日本語", "Display language: English"));
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e) => new AboutWindow(_useEnglish) { Owner = this }.ShowDialog();

    private void BackToEditor_Click(object sender, RoutedEventArgs e) => ShowInputScreen();

    private void DevicesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleMoveShortcut(e, _devicesGrid, _devices, L("デバイス", "device")))
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
        SetStatus(L($"デバイス {rows.Count} 行をコピーしました", $"Copied {rows.Count} device row(s)"));
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
            SetStatus(L("貼り付けできるデバイス行がありません", "No device rows can be pasted"), isError: true);
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
            SetStatus(L(
                $"デバイス {rows.Count} 行を貼り付けました / タイムチャート {count}/{ProjectFactory.MaxTimeChartTargets}",
                $"Pasted {rows.Count} device row(s) / Time Chart {count}/{ProjectFactory.MaxTimeChartTargets}"));
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
                : ProjectFactory.GuessDataType(address, Selected(_vendor), SelectedKeyenceDeviceMode());
            var row = new DeviceRow();
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = address;
            row.DataType = dataType;
            yield return row;
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
        return ProjectFactory.DeviceDataTypesForAddress(address, Selected(_vendor), SelectedKeyenceDeviceMode()).FirstOrDefault(dataType =>
                   dataType.Equals(value, StringComparison.OrdinalIgnoreCase))
               ?? ProjectFactory.GuessDataType(address, Selected(_vendor), SelectedKeyenceDeviceMode());
    }

    private string CoerceDataTypeForAddress(string dataType, string address) =>
        NormalizeDeviceDataType(dataType, address);

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
        if (TryHandleMoveShortcut(e, _watchGrid, _watches, L("タイムチャート", "time chart")))
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

        Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(row => $"{row.Address}\t{row.DataType}")));
        SetStatus(L($"タイムチャート {rows.Count} 行をコピーしました", $"Copied {rows.Count} time chart row(s)"));
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
            SetStatus(L("貼り付けできるタイムチャート行がありません", "No time chart rows can be pasted"), isError: true);
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
            SetStatus(L(
                $"タイムチャートに追加できるのは最大 {ProjectFactory.MaxTimeChartTargets} チャンネルです。",
                $"Time chart can contain up to {ProjectFactory.MaxTimeChartTargets} channels."),
                isError: true);
            return;
        }

        ApplyRows(_watches, startIndex, rows);
        SelectRows(_watchGrid, rows);
        SetStatus(L(
            $"タイムチャート {rows.Count} 行を貼り付けました / {uniqueCount}/{ProjectFactory.MaxTimeChartTargets}",
            $"Pasted {rows.Count} time chart row(s) / {uniqueCount}/{ProjectFactory.MaxTimeChartTargets}"));
    }

    private IEnumerable<WatchRow> ParseWatchClipboardRows(string text)
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
                var row = new WatchRow();
                row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
                row.Address = address;
                row.DataType = fields.Length > 1 ? CoerceDataTypeForAddress(fields[1], address) : ProjectFactory.GuessDataType(address, Selected(_vendor), SelectedKeyenceDeviceMode());
                yield return row;
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
        if (TryHandleMoveShortcut(e, _trapsGrid, _traps, L("トラップ", "trap")))
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
            $"{row.Address}\t{row.DataType}\t{row.ConditionDisplayText}\t{row.Threshold}\t{(row.Enabled ? "TRUE" : "FALSE")}")));
        SetStatus(L($"トラップ {rows.Count} 行をコピーしました", $"Copied {rows.Count} trap row(s)"));
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
            SetStatus(L("貼り付けできるトラップ行がありません", "No trap rows can be pasted"), isError: true);
            return;
        }

        _trapsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        _trapsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var startIndex = SelectedIndexOrAppend(_trapsGrid, _traps);
        ApplyRows(_traps, startIndex, rows);
        SelectRows(_trapsGrid, rows);
        SetStatus(L($"トラップ {rows.Count} 行を貼り付けました", $"Pasted {rows.Count} trap row(s)"));
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
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = address;
            var hasDataType = fields.Length > 2 && ProjectFactory.DeviceDataTypes.Any(dataType => dataType.Equals(fields[1].Trim(), StringComparison.OrdinalIgnoreCase));
            row.DataType = hasDataType ? CoerceDataTypeForAddress(fields[1], address) : ProjectFactory.GuessDataType(address, Selected(_vendor), SelectedKeyenceDeviceMode());
            var conditionIndex = hasDataType ? 2 : 1;
            var thresholdIndex = hasDataType ? 3 : 2;
            var enabledIndex = hasDataType ? 4 : 3;
            row.Condition = fields.Length > conditionIndex
                ? NormalizeTrapCondition(fields[conditionIndex], address)
                : ProjectFactory.DefaultTrapConditionForAddress(address, Selected(_vendor), SelectedKeyenceDeviceMode());
            if (fields.Length > thresholdIndex && !string.IsNullOrWhiteSpace(fields[thresholdIndex]))
            {
                row.Threshold = fields[thresholdIndex].Trim();
            }

            row.Enabled = fields.Length <= enabledIndex || string.IsNullOrWhiteSpace(fields[enabledIndex]) || ParseClipboardBoolean(fields[enabledIndex]);
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
                            _ => ProjectFactory.DefaultTrapConditionForAddress(address, Selected(_vendor), SelectedKeyenceDeviceMode()),
                        };
        return ProjectFactory.CoerceTrapConditionForAddress(address, condition, Selected(_vendor), SelectedKeyenceDeviceMode());
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

    private IEnumerable<DeviceRow> BuildDeviceBlockRows(TextBox startTextBox, TextBox countTextBox, int maxCount)
    {
        var vendor = Selected(_vendor);
        var keyenceDeviceMode = SelectedKeyenceDeviceMode();
        var count = ParseRange(countTextBox, fallback: 1, min: 1, max: maxCount);
        foreach (var address in ProjectFactory.BuildDeviceBlock(startTextBox.Text, count, vendor, keyenceDeviceMode))
        {
            var row = new DeviceRow();
            row.SetDeviceContext(vendor, keyenceDeviceMode);
            row.Address = address;
            row.DataType = ProjectFactory.GuessDataType(address, vendor, keyenceDeviceMode);
            yield return row;
        }
    }

    private IEnumerable<WatchRow> BuildWatchBlockRows(TextBox startTextBox, TextBox countTextBox, int maxCount)
    {
        var vendor = Selected(_vendor);
        var keyenceDeviceMode = SelectedKeyenceDeviceMode();
        var count = ParseRange(countTextBox, fallback: 1, min: 1, max: maxCount);
        foreach (var address in ProjectFactory.BuildDeviceBlock(startTextBox.Text, count, vendor, keyenceDeviceMode))
        {
            var row = new WatchRow();
            row.SetDeviceContext(vendor, keyenceDeviceMode);
            row.Address = address;
            row.DataType = ProjectFactory.GuessDataType(address, vendor, keyenceDeviceMode);
            yield return row;
        }
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
            SetStatus(_useEnglish
                ? $"{label} rows cannot be moved further"
                : $"{label}行はこれ以上移動できません");
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
        SetStatus(_useEnglish
            ? $"Moved {rows.Count} {label} row(s) {(direction < 0 ? "up" : "down")}"
            : $"{label} {rows.Count} 行を{(direction < 0 ? "上" : "下")}へ移動しました");
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
        SetStatus(L($"QR 表示: {_currentIndex + 1}/{_chunks.Count}", $"QR page: {_currentIndex + 1}/{_chunks.Count}"));
    }

    private void NextQr_Click(object sender, RoutedEventArgs e)
    {
        if (_chunks.Count == 0)
        {
            return;
        }

        _currentIndex = (_currentIndex + 1) % _chunks.Count;
        ShowCurrentQr();
        SetStatus(L($"QR 表示: {_currentIndex + 1}/{_chunks.Count}", $"QR page: {_currentIndex + 1}/{_chunks.Count}"));
    }

    private void ShowJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitGridEdits();
            _lastJson = Encoding.UTF8.GetString(ProjectQrPayload.ProjectJsonBytes(BuildProject()));
        }
        catch (Exception ex)
        {
            SetStatus(L($"JSON 表示エラー: {ex.Message}", $"JSON display error: {ex.Message}"), isError: true);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
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
            Text = L(
                $"文字数: {_lastJson.Length.ToString("N0", CultureInfo.InvariantCulture)} / UTF-8: {Encoding.UTF8.GetByteCount(_lastJson).ToString("N0", CultureInfo.InvariantCulture)} bytes",
                $"Characters: {_lastJson.Length.ToString("N0", CultureInfo.InvariantCulture)} / UTF-8: {Encoding.UTF8.GetByteCount(_lastJson).ToString("N0", CultureInfo.InvariantCulture)} bytes"),
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
            SetStatus(L($"JSON 読み込み: {Path.GetFileName(dialog.FileName)}", $"JSON loaded: {Path.GetFileName(dialog.FileName)}"));
        }
        catch (Exception ex)
        {
            SetStatus(L($"JSON 読み込みエラー: {ex.Message}", $"JSON load error: {ex.Message}"), isError: true);
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

            CommitGridEdits();
            var jsonBytes = ProjectQrPayload.ProjectJsonBytes(BuildProject());
            _lastJson = Encoding.UTF8.GetString(jsonBytes);
            File.WriteAllBytes(dialog.FileName, jsonBytes);
            SetStatus(L($"JSON 保存: {Path.GetFileName(dialog.FileName)}", $"JSON saved: {Path.GetFileName(dialog.FileName)}"));
        }
        catch (Exception ex)
        {
            SetStatus(L($"JSON 保存エラー: {ex.Message}", $"JSON save error: {ex.Message}"), isError: true);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveQrImages_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitGridEdits();
            var project = BuildProject();
            _chunks = ProjectQrPayload.EncodeProjectChunks(project, _chunkSize);
            _currentIndex = 0;
            _lastJson = Encoding.UTF8.GetString(ProjectQrPayload.ProjectJsonBytes(project));
            ShowCurrentQr();

            if (_chunks.Count == 0)
            {
                return;
            }

            var dialog = new OpenFolderDialog { Title = L("QR PNG の保存先フォルダーを選択", "Select QR PNG output folder") };
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

            SetStatus(L($"QR PNG 保存: {_chunks.Count} file(s)", $"QR PNG saved: {_chunks.Count} file(s)"));
        }
        catch (Exception ex)
        {
            SetStatus(L($"QR PNG 保存エラー: {ex.Message}", $"QR PNG save error: {ex.Message}"), isError: true);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddDevice_Click(object sender, RoutedEventArgs e)
    {
        var row = new DeviceRow { Address = "", DataType = "Bit" };
        row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
        _devices.Add(row);
        SelectNewRow(_devicesGrid, row);
    }

    private void AddDeviceBlock_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitGridEdits();
            var rows = BuildDeviceBlockRows(_deviceBlockStart, _deviceBlockCount, maxCount: 1000).ToList();
            foreach (var row in rows)
            {
                _devices.Add(row);
            }

            SelectRows(_devicesGrid, rows);
            SetStatus(L(
                $"Device block added: {rows.Count} point(s)",
                $"Device block added: {rows.Count} point(s)"));
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void MoveDeviceUp_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_devicesGrid, _devices, -1, L("デバイス", "device"));

    private void MoveDeviceDown_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_devicesGrid, _devices, 1, L("デバイス", "device"));

    private void DeleteDevice_Click(object sender, RoutedEventArgs e) => RemoveSelectedRows(_devicesGrid, _devices);

    private void AddWatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TimeChartAddresses().Count >= ProjectFactory.MaxTimeChartTargets)
            {
                SetStatus(L(
                    $"タイムチャートに追加できるのは最大 {ProjectFactory.MaxTimeChartTargets} チャンネルです。",
                    $"Time chart can contain up to {ProjectFactory.MaxTimeChartTargets} channels."),
                    isError: true);
                return;
            }
        }
        catch (ArgumentException ex)
        {
            SetStatus(ex.Message, isError: true);
            return;
        }

        var row = new WatchRow();
        row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
        _watches.Add(row);
        SelectNewRow(_watchGrid, row);
    }

    private void MoveWatchUp_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_watchGrid, _watches, -1, L("タイムチャート", "time chart"));

    private void MoveWatchDown_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_watchGrid, _watches, 1, L("タイムチャート", "time chart"));

    private void DeleteWatch_Click(object sender, RoutedEventArgs e) => RemoveSelectedRows(_watchGrid, _watches);

    private void AddWatchBlock_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitGridEdits();
            var rows = BuildWatchBlockRows(_watchBlockStart, _watchBlockCount, maxCount: ProjectFactory.MaxTimeChartTargets).ToList();
            var candidate = _watches.Concat(rows).ToList();
            var uniqueCount = UniqueWatchAddressCount(candidate);
            if (uniqueCount > ProjectFactory.MaxTimeChartTargets)
            {
                SetStatus($"Time chart can contain up to {ProjectFactory.MaxTimeChartTargets} channels.", isError: true);
                return;
            }

            foreach (var row in rows)
            {
                _watches.Add(row);
            }

            SelectRows(_watchGrid, rows);
            SetStatus($"Time chart block added: {rows.Count} point(s) / {uniqueCount}/{ProjectFactory.MaxTimeChartTargets}");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void AddTrap_Click(object sender, RoutedEventArgs e)
    {
        var row = new TrapRow();
        row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
        _traps.Add(row);
        SelectNewRow(_trapsGrid, row);
    }

    private void MoveTrapUp_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_trapsGrid, _traps, -1, L("トラップ", "trap"));

    private void MoveTrapDown_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_trapsGrid, _traps, 1, L("トラップ", "trap"));

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
        RequireProjectJsonV2(root);

        _ = ReadRequiredString(root, "projectId");
        _ = ReadRequiredInt64(root, "updatedAtEpochMs");
        _projectName.Text = ReadRequiredString(root, "projectName");

        var plc = ReadRequiredObject(root, "plc");
        var connection = ReadRequiredObject(plc, "connection");
        var vendor = ToUiVendor(ReadRequiredString(plc, "vendor"));
        SelectItem(_vendor, vendor);
        ApplyVendorDefaults();
        SelectItem(_connectionMode, ToUiConnectionMode(ReadRequiredString(connection, "mode")));
        _host.Text = ReadRequiredString(connection, "host");
        var port = ReadRequiredInt(connection, "port");
        if (port is < 1 or > 65_535)
        {
            throw new InvalidOperationException($"Unsupported connection port: {port}");
        }
        _port.Text = port.ToString(CultureInfo.InvariantCulture);
        SelectItem(_model, ReadRequiredString(plc, "cpuModel"));
        if (vendor == "Keyence")
        {
            var keyence = ReadRequiredObject(plc, "keyence");
            SelectItem(_keyenceMode, ToUiKeyenceMode(ReadRequiredString(keyence, "deviceMode")));
        }

        SelectItem(_transport, ToUiTransport(ReadRequiredString(connection, "transport")));
        _interval.Text = ReadRequiredInt(connection, "pollingIntervalMs").ToString(CultureInfo.InvariantCulture);
        _timeout.Text = ReadRequiredInt(connection, "timeoutMs").ToString(CultureInfo.InvariantCulture);
        if (vendor == "Melsec")
        {
            var melsec = ReadRequiredObject(plc, "melsec");
            _network.Text = ReadRequiredInt(melsec, "networkNo").ToString(CultureInfo.InvariantCulture);
            _station.Text = ReadRequiredInt(melsec, "stationNo").ToString(CultureInfo.InvariantCulture);
            _moduleIo.Text = ReadRequiredInt(melsec, "moduleIoNo").ToString(CultureInfo.InvariantCulture);
            _multidrop.Text = ReadRequiredInt(melsec, "multidropNo").ToString(CultureInfo.InvariantCulture);
        }

        var devicesElement = ReadRequiredArray(root, "deviceList");
        _devices.Clear();
        foreach (var device in devicesElement.EnumerateArray())
        {
            var address = ReadRequiredString(device, "address");
            var row = new DeviceRow();
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = address;
            row.DataType = CoerceDataTypeForAddress(ToUiDataType(ReadRequiredString(device, "dataType")), address);
            _devices.Add(row);
        }

        var timeChartElement = ReadRequiredArray(root, "timeChart");
        _watches.Clear();
        foreach (var target in timeChartElement.EnumerateArray())
        {
            var address = ReadRequiredString(target, "address");
            var row = new WatchRow();
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = address;
            row.DataType = CoerceDataTypeForAddress(ToUiDataType(ReadRequiredString(target, "dataType")), address);
            _watches.Add(row);
        }

        var trapsElement = ReadRequiredArray(root, "traps");
        _traps.Clear();
        foreach (var trap in trapsElement.EnumerateArray())
        {
            var threshold = "";
            if (trap.TryGetProperty("comparisonValue", out var thresholdValue) &&
                thresholdValue.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                if (thresholdValue.ValueKind != System.Text.Json.JsonValueKind.Number)
                {
                    throw new InvalidOperationException("Project JSON value 'traps.comparisonValue' must be a number or null.");
                }
                threshold = thresholdValue.GetDouble().ToString(CultureInfo.InvariantCulture);
            }

            var row = new TrapRow();
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = ReadRequiredString(trap, "address");
            row.DataType = CoerceDataTypeForAddress(ToUiDataType(ReadRequiredString(trap, "dataType")), row.Address);
            row.Condition = ToUiTrapCondition(ReadRequiredString(trap, "condition"));
            row.Threshold = threshold;
            row.Enabled = ReadRequiredBool(trap, "enabled");
            _traps.Add(row);
        }
    }

    private static string ToUiVendor(string value) => value.Trim().ToUpperInvariant() switch
    {
        "MELSEC" => "Melsec",
        "KEYENCE" => "Keyence",
        _ => throw new InvalidOperationException($"Unsupported PLC vendor: {value}"),
    };

    private static string ToUiConnectionMode(string value) => value.Trim().ToUpperInvariant() switch
    {
        "REAL" => "Real",
        "DEMO_MOCK" => "DemoMock",
        _ => throw new InvalidOperationException($"Unsupported connection mode: {value}"),
    };

    private static string ToUiKeyenceMode(string value) => value.Trim().ToUpperInvariant() switch
    {
        "NORMAL" => "Normal",
        "XYM" => "Xym",
        _ => throw new InvalidOperationException($"Unsupported KEYENCE device mode: {value}"),
    };

    private static string ToUiTransport(string value) => value.Trim().ToUpperInvariant() switch
    {
        "TCP" => "Tcp",
        "UDP" => "Udp",
        _ => throw new InvalidOperationException($"Unsupported transport: {value}"),
    };

    private static string ToUiDataType(string value) => value.Trim().ToUpperInvariant() switch
    {
        "BIT" => "Bit",
        "INT16" => "Int16",
        "UINT16" => "UInt16",
        "INT32" => "Int32",
        "UINT32" => "UInt32",
        "FLOAT32" => "Float32",
        _ => throw new InvalidOperationException($"Unsupported device data type: {value}"),
    };

    private static string ToUiTrapCondition(string value) => value.Trim().ToUpperInvariant() switch
    {
        "RISING_EDGE" => "Rise",
        "FALLING_EDGE" => "Fall",
        "CHANGE" => "Change",
        "GREATER_OR_EQUAL" => "GreaterOrEqual",
        "LESS_OR_EQUAL" => "LessOrEqual",
        "EQUAL" => "Equal",
        "NOT_EQUAL" => "NotEqual",
        _ => throw new InvalidOperationException($"Unsupported trap condition: {value}"),
    };

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

    private static int ParseRange(TextBox textBox, int fallback, int min, int max)
    {
        var value = Clamp(ParseInt(textBox.Text, fallback), min, max);
        textBox.Text = value.ToString(CultureInfo.InvariantCulture);
        return value;
    }

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    private static void RequireProjectJsonV2(System.Text.Json.JsonElement root)
    {
        var schema = ReadRequiredString(root, "schema");
        if (schema != "plc-io-checker-project")
        {
            throw new InvalidOperationException($"Unsupported project schema: {schema}");
        }

        var version = ReadRequiredInt(root, "schemaVersion");
        if (version != 2)
        {
            throw new InvalidOperationException($"Unsupported project schema version: {version}");
        }
    }

    private static System.Text.Json.JsonElement ReadRequiredObject(System.Text.Json.JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Project JSON value '{name}' must be an object.");
        }

        return value;
    }

    private static System.Text.Json.JsonElement ReadRequiredArray(System.Text.Json.JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Project JSON value '{name}' must be an array.");
        }

        return value;
    }

    private static string ReadRequiredString(System.Text.Json.JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.String ||
            value.GetString() is not { } result)
        {
            throw new InvalidOperationException($"Project JSON value '{name}' must be a string.");
        }

        return result;
    }

    private static int ReadRequiredInt(System.Text.Json.JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.Number ||
            !value.TryGetInt32(out var result))
        {
            throw new InvalidOperationException($"Project JSON value '{name}' must be an integer.");
        }

        return result;
    }

    private static long ReadRequiredInt64(System.Text.Json.JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.Number ||
            !value.TryGetInt64(out var result))
        {
            throw new InvalidOperationException($"Project JSON value '{name}' must be an integer.");
        }

        return result;
    }

    private static bool ReadRequiredBool(System.Text.Json.JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
        {
            throw new InvalidOperationException($"Project JSON value '{name}' must be a boolean.");
        }

        return value.GetBoolean();
    }
}
