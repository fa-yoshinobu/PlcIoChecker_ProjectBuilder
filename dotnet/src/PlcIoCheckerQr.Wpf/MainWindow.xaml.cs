using Microsoft.Win32;
using PlcIoCheckerQr.Core;
using PlcIoCheckerQr.Wpf.Localization;
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
using System.Windows.Threading;

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
        private string _comment = "";

        public string Comment
        {
            get => _comment;
            set
            {
                var next = value ?? "";
                if (_comment == next)
                {
                    return;
                }

                _comment = next;
                OnPropertyChanged();
            }
        }
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
    private string _languageCode = "en";
    private bool _statusIsReady = true;
    private readonly DispatcherTimer _autoQrTimer = new();

    private const double DefaultAutoQrIntervalSeconds = 1.0;
    private const double MinAutoQrIntervalSeconds = 0.0;
    private const double MaxAutoQrIntervalSeconds = 5.0;
    private const double MinDispatcherTimerSeconds = 0.1;

    private static LanguageCatalog CurrentLanguage { get; set; } = LanguageCatalog.Load("en");
    private static readonly string[] ImportAliasKeys =
    [
        "import.alias.boolean.true",
        "import.alias.header.address",
        "import.alias.header.condition",
        "import.alias.header.watch",
        "import.alias.trap.change",
        "import.alias.trap.equal",
        "import.alias.trap.fall",
        "import.alias.trap.greaterOrEqual",
        "import.alias.trap.lessOrEqual",
        "import.alias.trap.notEqual",
        "import.alias.trap.rise",
    ];

    private static readonly (string Key, string Condition)[] TrapConditionAliasKeys =
    [
        ("import.alias.trap.rise", "Rise"),
        ("import.alias.trap.fall", "Fall"),
        ("import.alias.trap.change", "Change"),
        ("import.alias.trap.greaterOrEqual", "GreaterOrEqual"),
        ("import.alias.trap.lessOrEqual", "LessOrEqual"),
        ("import.alias.trap.equal", "Equal"),
        ("import.alias.trap.notEqual", "NotEqual"),
    ];

    private static readonly Lazy<IReadOnlyDictionary<string, string[]>> ImportAliases = new(LoadImportAliases);
    private LanguageCatalog _language = LanguageCatalog.Load("en");

    private static string TrapConditionDisplayText(string condition) => condition switch
    {
        "Rise" => CurrentLanguage.Text("trap.rise"),
        "Fall" => CurrentLanguage.Text("trap.fall"),
        "Change" => CurrentLanguage.Text("trap.change"),
        "GreaterOrEqual" => ">=",
        "LessOrEqual" => "<=",
        "Equal" => "==",
        "NotEqual" => "!=",
        _ => condition,
    };

    private static IReadOnlyDictionary<string, string[]> LoadImportAliases()
    {
        var aliases = ImportAliasKeys.ToDictionary(
            key => key,
            _ => new List<string>(),
            StringComparer.Ordinal);

        foreach (var code in LanguageCatalog.Codes())
        {
            var catalog = LanguageCatalog.Load(code);
            foreach (var key in ImportAliasKeys)
            {
                var text = catalog.Text(key);
                if (text.Equals(key, StringComparison.Ordinal))
                {
                    continue;
                }

                aliases[key].AddRange(text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        return aliases.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            StringComparer.Ordinal);
    }

    private static bool MatchesImportAlias(string text, string key)
    {
        var value = text.Trim();
        return ImportAliases.Value.TryGetValue(key, out var aliases) &&
               aliases.Any(alias => alias.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesNormalizedImportAlias(string normalizedText, string key) =>
        ImportAliases.Value.TryGetValue(key, out var aliases) &&
        aliases.Any(alias => NormalizeAlias(alias).Equals(normalizedText, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeAlias(string text) =>
        text.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();

    private static string? ImportTrapConditionAlias(string normalizedText) =>
        TrapConditionAliasKeys
            .Where(item => MatchesNormalizedImportAlias(normalizedText, item.Key))
            .Select(item => item.Condition)
            .FirstOrDefault();

    public MainWindow()
    {
        InitializeComponent();
        MoveProjectTabFirst();
        SetupComboBoxes();
        SetupGrids();
        LoadDefaultRows();
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

    private void MoveProjectTabFirst()
    {
        if (!_mainTabs.Items.Contains(_projectTab))
        {
            return;
        }

        _mainTabs.Items.Remove(_projectTab);
        _mainTabs.Items.Insert(0, _projectTab);
        _mainTabs.SelectedItem = _projectTab;
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
        _devicesGrid.ToolTip = "Copy and paste tab-separated rows from Excel. Columns are Address / Data type / Comment.";
        _devicesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Address",
            Binding = new Binding(nameof(DeviceRow.Address)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _devicesGrid.Columns.Add(DeviceDataTypeColumn<DeviceRow>());
        _devicesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Comment",
            Binding = new Binding(nameof(DeviceRow.Comment)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1.4, DataGridLengthUnitType.Star),
        });

        _watchGrid.ItemsSource = _watches;
        _watchGrid.PreviewKeyDown += WatchGrid_PreviewKeyDown;
        _watchGrid.ToolTip = "Copy and paste tab-separated rows from Excel. Columns are Address.";
        _watchGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Address",
            Binding = new Binding(nameof(WatchRow.Address)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _watchGrid.Columns.Add(DeviceDataTypeColumn<WatchRow>());

        _trapsGrid.ItemsSource = _traps;
        _trapsGrid.PreviewKeyDown += TrapsGrid_PreviewKeyDown;
        _trapsGrid.ToolTip = "Copy and paste tab-separated rows from Excel. Columns are Address / Data type / Condition / Threshold / Enabled.";
        _trapsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Address",
            Binding = new Binding(nameof(TrapRow.Address)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _trapsGrid.Columns.Add(DeviceDataTypeColumn<TrapRow>());
        _trapsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Condition",
            CellTemplate = TrapConditionCellTemplate(),
            CellEditingTemplate = TrapConditionEditingTemplate(),
            Width = new DataGridLength(220),
        });
        _trapsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Threshold",
            CellTemplate = TrapThresholdCellTemplate(),
            CellEditingTemplate = TrapThresholdEditingTemplate(),
            Width = new DataGridLength(140),
        });
        _trapsGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Enabled",
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

    private void Generate(bool showQrScreen)
    {
        try
        {
            StopAutoQr(updateStatus: false);
            CommitGridEdits();
            var project = BuildProject();
            _chunks = ProjectQrPayload.EncodeProjectChunks(project, _chunkSize);
            _currentIndex = 0;
            _lastJson = Encoding.UTF8.GetString(ProjectQrPayload.ProjectJsonBytes(project));
            ShowCurrentQr();
            SetSummary(project);
            SetStatus(Tf("status.qrGenerated", _chunks.Count));

            if (showQrScreen)
            {
                ShowQrScreen();
            }
        }
        catch (Exception ex)
        {
            SetStatus(Tf("status.generationError", ex.Message), isError: true);
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
            ModuleIo: ParseHexRange(_moduleIo, fallback: 0x03FF, min: 0, max: 0xFFFF, width: 4),
            Multidrop: ParseHexRange(_multidrop, fallback: 0, min: 0, max: 0xFF, width: 2),
            RemotePassword: Selected(_vendor) == "Melsec" ? _remotePassword.Password : "",
            DevicesText: DevicesText(),
            WatchText: WatchText(),
            TrapsText: TrapsText()));
    }

    private void ShowCurrentQr()
    {
        if (_chunks.Count == 0)
        {
            StopAutoQr(updateStatus: false);
            _qrImage.Source = null;
            _pageLabel.Text = T("status.qrNotGenerated");
            _qrMeta.Text = "";
            UpdateAutoQrUi();
            return;
        }

        var chunk = _chunks[_currentIndex];
        _pageLabel.Text = Tf("qr.page", chunk.Index, chunk.Total);
        if (chunk.Total <= 1)
        {
            StopAutoQr(updateStatus: false);
        }

        UpdateAutoQrUi();
        _qrMeta.Text = Tf("qr.meta", chunk.Payload.Length, _errorCorrection, chunk.Session, chunk.Checksum[..12]);

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
                var comment = NormalizeDeviceComment(row.Comment);
                return string.IsNullOrWhiteSpace(comment)
                    ? $"{address},{dataType}"
                    : $"{address},{dataType},{comment}";
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
            throw new ArgumentException(Tf("status.timeChartMax", ProjectFactory.MaxTimeChartTargets));
        }

        return addresses;
    }

    private string TrapsText()
    {
        var rows = _traps
            .Where(row => !string.IsNullOrWhiteSpace(row.Address) && !string.IsNullOrWhiteSpace(row.Condition))
            .ToList();
        if (rows.Count > ProjectFactory.MaxTrapDefinitions)
        {
            throw new ArgumentException(Tf("status.trapMax", ProjectFactory.MaxTrapDefinitions));
        }

        return string.Join(Environment.NewLine,
            rows.Select(row =>
            {
                var address = row.Address.Trim();
                var dataType = string.IsNullOrWhiteSpace(row.DataType)
                    ? ProjectFactory.GuessDataType(address, Selected(_vendor), SelectedKeyenceDeviceMode())
                    : CoerceDataTypeForAddress(row.DataType.Trim(), address);
                return $"{address},{dataType},{row.Condition.Trim()},{row.Threshold.Trim()},{(row.Enabled ? "true" : "false")}";
            }));
    }

    private void ShowQrScreen()
    {
        _inputView.Visibility = Visibility.Collapsed;
        _navInputArea.Visibility = Visibility.Collapsed;
        _qrView.Visibility = Visibility.Visible;
        _navQrArea.Visibility = Visibility.Visible;
        _qrView.Focus();
        Keyboard.Focus(_qrView);
    }

    private void ShowInputScreen()
    {
        StopAutoQr(updateStatus: false);
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

    private string T(string key) => _language.Text(key);

    private string Tf(string key, params object?[] args) => _language.Format(key, args);

    private void ApplyLanguage()
    {
        _language = LanguageCatalog.Load(_languageCode);
        _languageCode = _language.Code;
        CurrentLanguage = _language;
        _langLabel.Text = T("language.buttonLabel");
        _langButton.ToolTip = T("language.switchTooltip");

        _fileMenu.Header = T("menu.file");
        _loadJsonMenuItem.Header = T("menu.loadJson");
        _saveJsonMenuItem.Header = T("menu.saveJson");
        _showJsonMenuItem.Header = T("menu.showJson");
        _qrMenu.Header = T("menu.qr");
        _qrMenu.ToolTip = T("menu.qr.tooltip");
        _qrChunkSizeMenu.Header = T("menu.qrChunkSize");
        _qrChunkSizeMenu.ToolTip = T("menu.qrChunkSize.tooltip");
        _qrDisplaySizeMenu.Header = T("menu.qrDisplaySize");
        _qrDisplaySizeMenu.ToolTip = T("menu.qrDisplaySize.tooltip");
        _qrErrorCorrectionMenu.Header = T("menu.errorCorrection");
        _qrErrorCorrectionMenu.ToolTip = T("menu.errorCorrection.tooltip");
        _saveQrImagesMenuItem.Header = T("menu.savePng");
        _helpMenu.Header = T("menu.help");
        _aboutMenuItem.Header = T("menu.about");
        ApplyQrOptionTooltips();

        _generateQrButton.Content = T("button.generateQr");
        _backToEditorButton.Content = T("button.backToEditor");
        _prevQrButton.Content = T("button.previous");
        _nextQrButton.Content = T("button.next");
        _autoQrIntervalLabel.Text = T("field.autoQrInterval");
        _autoQrSecondsUnit.Text = T("unit.secondsShort");
        _autoQrSeconds.ToolTip = T("tooltip.autoQrSeconds");
        UpdateAutoQrUi();

        _devicesTab.Header = T("tab.devices");
        _watchTab.Header = T("tab.watch");
        _trapsTab.Header = T("tab.traps");
        _projectTab.Header = T("tab.project");

        _devicesTitle.Text = T("section.devices.title");
        _devicesMeta.Text = T("section.devices.meta");
        _watchTitle.Text = T("section.watch.title");
        _watchMeta.Text = Tf("section.watch.meta", ProjectFactory.MaxTimeChartTargets);
        _trapsTitle.Text = T("section.traps.title");
        _trapsMeta.Text = Tf("section.traps.meta", ProjectFactory.MaxTrapDefinitions);

        _moveDeviceUpButton.Content = _moveWatchUpButton.Content = _moveTrapUpButton.Content = T("button.up");
        _moveDeviceDownButton.Content = _moveWatchDownButton.Content = _moveTrapDownButton.Content = T("button.down");
        _addDeviceButton.Content = _addWatchButton.Content = _addTrapButton.Content = T("button.addRow");
        _addDeviceBlockButton.Content = _addWatchBlockButton.Content = T("button.addBlock");
        _deleteDeviceButton.Content = _deleteWatchButton.Content = _deleteTrapButton.Content = T("button.delete");

        _moveDeviceUpButton.ToolTip = T("tooltip.moveDeviceUp");
        _moveDeviceDownButton.ToolTip = T("tooltip.moveDeviceDown");
        _moveWatchUpButton.ToolTip = T("tooltip.moveWatchUp");
        _moveWatchDownButton.ToolTip = T("tooltip.moveWatchDown");
        _moveTrapUpButton.ToolTip = T("tooltip.moveTrapUp");
        _moveTrapDownButton.ToolTip = T("tooltip.moveTrapDown");
        _deviceBlockStart.ToolTip = _watchBlockStart.ToolTip = T("tooltip.blockStart");
        _deviceBlockCount.ToolTip = _watchBlockCount.ToolTip = T("tooltip.blockCount");

        _projectSectionTitle.Text = T("section.project.title");
        _projectSectionMeta.Text = T("section.project.meta");
        _projectNameLabel.Text = T("field.projectName");
        _vendorLabel.Text = T("field.vendor");
        _modelLabel.Text = T("field.model");
        _keyenceModeLabel.Text = T("field.keyenceMode");
        _supportedDevicesLabel.Text = T("field.supportedDevices");

        _connectionSectionTitle.Text = T("section.connection.title");
        _connectionSectionMeta.Text = T("section.connection.meta");
        _connectionModeLabel.Text = T("field.connectionMode");
        _hostLabel.Text = T("field.host");
        _portLabel.Text = T("field.port");
        _transportLabel.Text = T("field.transport");
        _intervalLabel.Text = T("field.interval");
        _timeoutLabel.Text = T("field.timeout");
        _networkLabel.Text = T("field.network");
        _stationLabel.Text = T("field.station");
        _moduleIoLabel.Text = T("field.moduleIo");
        _multidropLabel.Text = T("field.multidrop");
        _resetRoutingDefaultsButton.Content = T("button.resetRoutingDefaults");
        _remotePasswordLabel.Text = T("field.remotePassword");

        _devicesGrid.ToolTip = T("tooltip.devicesGrid");
        _watchGrid.ToolTip = T("tooltip.watchGrid");
        _trapsGrid.ToolTip = T("tooltip.trapsGrid");
        if (_devicesGrid.Columns.Count >= 3)
        {
            _devicesGrid.Columns[0].Header = T("column.address");
            _devicesGrid.Columns[1].Header = T("column.dataType");
            _devicesGrid.Columns[2].Header = T("column.comment");
        }

        if (_watchGrid.Columns.Count >= 2)
        {
            _watchGrid.Columns[0].Header = T("column.address");
            _watchGrid.Columns[1].Header = T("column.dataType");
        }

        if (_trapsGrid.Columns.Count >= 5)
        {
            _trapsGrid.Columns[0].Header = T("column.address");
            _trapsGrid.Columns[1].Header = T("column.dataType");
            _trapsGrid.Columns[2].Header = T("column.condition");
            _trapsGrid.Columns[3].Header = T("column.threshold");
            _trapsGrid.Columns[4].Header = T("column.enabled");
        }

        _trapsGrid.Items.Refresh();
        UpdateTrapLimitUi();
        UpdateSupportedDeviceNames();
        if (_chunks.Count == 0)
        {
            _pageLabel.Text = T("status.qrNotGenerated");
        }
        else
        {
            ShowCurrentQr();
        }

        if (_statusIsReady)
        {
            _statusText.Text = T("status.ready");
        }
    }

    private void ApplyQrOptionTooltips()
    {
        ApplyTaggedMenuTooltips(_qrChunkSizeMenu.Items, "menu.qrChunkSize.option");
        ApplyTaggedMenuTooltips(_qrDisplaySizeMenu.Items, "menu.qrDisplaySize.option");
        ApplyTaggedMenuTooltips(_qrErrorCorrectionMenu.Items, "menu.errorCorrection.option");
    }

    private void ApplyTaggedMenuTooltips(ItemCollection items, string keyPrefix)
    {
        foreach (var menuItem in items.OfType<MenuItem>())
        {
            if (menuItem.Tag is string tag)
            {
                var separatorIndex = tag.IndexOf(':', StringComparison.Ordinal);
                var option = separatorIndex >= 0 ? tag[(separatorIndex + 1)..] : tag;
                menuItem.ToolTip = T($"{keyPrefix}.{option}.tooltip");
            }

            ApplyTaggedMenuTooltips(menuItem.Items, keyPrefix);
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
            row.Comment = NormalizeDeviceComment(row.Comment);
        }
        CommonizeDeviceComments();

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
        _statusIsReady = message == T("status.ready");
        _statusText.Text = message;
        _statusText.Foreground = isError
            ? (Brush)FindResource("ErrorFg")
            : (Brush)FindResource("TextMuted");
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

    private void LangButton_Click(object sender, RoutedEventArgs e)
    {
        var nextCode = T("language.next");
        _languageCode = LanguageCatalog.HasLanguage(nextCode)
            ? nextCode
            : LanguageCatalog.NextCode(_language.Code);
        ApplyLanguage();
        SetStatus(T("status.languageChanged"));
    }

    private void ResetRoutingDefaults_Click(object sender, RoutedEventArgs e)
    {
        _network.Text = "0";
        _station.Text = "255";
        _moduleIo.Text = FormatPrefixedHex(0x03FF, 4);
        _multidrop.Text = FormatPrefixedHex(0, 2);
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e) => new AboutWindow(_language) { Owner = this }.ShowDialog();

    private void BackToEditor_Click(object sender, RoutedEventArgs e) => ShowInputScreen();

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_qrView.Visibility != Visibility.Visible || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Left)
        {
            NavigateQr(offset: -1);
            e.Handled = true;
        }
        else if (key == Key.Right)
        {
            NavigateQr(offset: 1);
            e.Handled = true;
        }
    }

    private void DevicesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleMoveShortcut(e, _devicesGrid, _devices, T("noun.device")))
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
            $"{row.Address}\t{row.DataType}\t{NormalizeDeviceComment(row.Comment)}")));
        SetStatus(Tf("status.copiedDeviceRows", rows.Count));
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
            SetStatus(T("status.noDeviceRowsPasted"), isError: true);
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
            SetStatus(Tf("status.pastedDeviceRows", rows.Count, count, ProjectFactory.MaxTimeChartTargets));
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

            var hasDataType = fields.Length > 1 && IsDeviceDataTypeField(fields[1]);
            var dataType = hasDataType
                ? NormalizeDeviceDataType(fields[1], address)
                : ProjectFactory.GuessDataType(address, Selected(_vendor), SelectedKeyenceDeviceMode());
            var commentIndex = hasDataType ? 2 : fields.Length > 2 && string.IsNullOrWhiteSpace(fields[1]) ? 2 : 1;
            var row = new DeviceRow();
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = address;
            row.DataType = dataType;
            row.Comment = DeviceCommentFromFields(fields, commentIndex);
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
        return MatchesImportAlias(first, "import.alias.header.address") ||
               second.Equals("Data type", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeviceDataTypeField(string text) =>
        ProjectFactory.DeviceDataTypes.Any(dataType => dataType.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string DeviceCommentFromFields(IReadOnlyList<string> fields, int startIndex) =>
        startIndex >= fields.Count
            ? ""
            : NormalizeDeviceComment(string.Join(",", fields.Skip(startIndex)));

    private string NormalizeDeviceDataType(string text, string address)
    {
        var value = text.Trim();
        return ProjectFactory.DeviceDataTypesForAddress(address, Selected(_vendor), SelectedKeyenceDeviceMode()).FirstOrDefault(dataType =>
                   dataType.Equals(value, StringComparison.OrdinalIgnoreCase))
               ?? ProjectFactory.GuessDataType(address, Selected(_vendor), SelectedKeyenceDeviceMode());
    }

    private string CoerceDataTypeForAddress(string dataType, string address) =>
        NormalizeDeviceDataType(dataType, address);

    private static string NormalizeDeviceComment(string text) =>
        text.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

    private static bool ParseClipboardBoolean(string text)
    {
        var value = text.Trim();
        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("checked", StringComparison.OrdinalIgnoreCase) ||
               MatchesImportAlias(value, "import.alias.boolean.true");
    }

    private void WatchGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleMoveShortcut(e, _watchGrid, _watches, T("noun.watch")))
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.C)
        {
            CopySelectedTimeChartRowsToClipboard();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V)
        {
            PasteTimeChartRowsFromClipboard();
            e.Handled = true;
        }
    }

    private void CopySelectedTimeChartRowsToClipboard()
    {
        _watchGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        _watchGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var rows = SelectedRows(_watchGrid, _watches);
        if (rows.Count == 0)
        {
            return;
        }

        Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(row => $"{row.Address}\t{row.DataType}")));
        SetStatus(Tf("status.copiedWatchRows", rows.Count));
    }

    private void PasteTimeChartRowsFromClipboard()
    {
        if (!Clipboard.ContainsText())
        {
            return;
        }

        var rows = ParseWatchClipboardRows(Clipboard.GetText()).ToList();
        if (rows.Count == 0)
        {
            SetStatus(T("status.noWatchRowsPasted"), isError: true);
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
            SetStatus(Tf("status.timeChartMax", ProjectFactory.MaxTimeChartTargets), isError: true);
            return;
        }

        ApplyRows(_watches, startIndex, rows);
        SelectRows(_watchGrid, rows);
        SetStatus(Tf("status.pastedWatchRows", rows.Count, uniqueCount, ProjectFactory.MaxTimeChartTargets));
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
        if (TryHandleMoveShortcut(e, _trapsGrid, _traps, T("noun.trap")))
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
        SetStatus(Tf("status.copiedTrapRows", rows.Count));
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
            SetStatus(T("status.noTrapRowsPasted"), isError: true);
            return;
        }

        _trapsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        _trapsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var startIndex = SelectedIndexOrAppend(_trapsGrid, _traps);
        var finalCount = Math.Max(_traps.Count, startIndex + rows.Count);
        if (finalCount > ProjectFactory.MaxTrapDefinitions)
        {
            SetStatus(Tf("status.trapMax", ProjectFactory.MaxTrapDefinitions), isError: true);
            return;
        }

        ApplyRows(_traps, startIndex, rows);
        SelectRows(_trapsGrid, rows);
        SetStatus(Tf("status.pastedTrapRows", rows.Count));
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
        var normalized = NormalizeAlias(value);
        var condition = ProjectFactory.TrapConditions.FirstOrDefault(item =>
                            item.Equals(value, StringComparison.OrdinalIgnoreCase))
                        ?? ImportTrapConditionAlias(normalized)
                        ?? ProjectFactory.DefaultTrapConditionForAddress(address, Selected(_vendor), SelectedKeyenceDeviceMode());
        return ProjectFactory.CoerceTrapConditionForAddress(address, condition, Selected(_vendor), SelectedKeyenceDeviceMode());
    }

    private static string[] SplitClipboardLine(string line) =>
        line.Contains('\t', StringComparison.Ordinal)
            ? line.Split('\t')
            : line.Split(',');

    private static bool IsAddressClipboardHeader(IReadOnlyList<string> fields)
    {
        var first = fields[0].Trim();
        return MatchesImportAlias(first, "import.alias.header.address") ||
               MatchesImportAlias(first, "import.alias.header.watch");
    }

    private static bool IsTrapClipboardHeader(IReadOnlyList<string> fields)
    {
        if (fields.Count == 0)
        {
            return false;
        }

        var first = fields[0].Trim();
        var second = fields.Count > 1 ? fields[1].Trim() : "";
        return MatchesImportAlias(first, "import.alias.header.address") ||
               MatchesImportAlias(second, "import.alias.header.condition");
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
        var machineLabel = Selected(_model);
        var count = ParseRange(countTextBox, fallback: 1, min: 1, max: maxCount);
        foreach (var address in ProjectFactory.BuildDeviceBlock(startTextBox.Text, count, vendor, keyenceDeviceMode, machineLabel))
        {
            var row = new DeviceRow();
            row.SetDeviceContext(vendor, keyenceDeviceMode);
            row.Address = address;
            row.DataType = ProjectFactory.GuessDataType(address, vendor, keyenceDeviceMode, machineLabel);
            yield return row;
        }
    }

    private IEnumerable<WatchRow> BuildWatchBlockRows(TextBox startTextBox, TextBox countTextBox, int maxCount)
    {
        var vendor = Selected(_vendor);
        var keyenceDeviceMode = SelectedKeyenceDeviceMode();
        var machineLabel = Selected(_model);
        var count = ParseRange(countTextBox, fallback: 1, min: 1, max: maxCount);
        foreach (var address in ProjectFactory.BuildDeviceBlock(startTextBox.Text, count, vendor, keyenceDeviceMode, machineLabel))
        {
            var row = new WatchRow();
            row.SetDeviceContext(vendor, keyenceDeviceMode);
            row.Address = address;
            row.DataType = ProjectFactory.GuessDataType(address, vendor, keyenceDeviceMode, machineLabel);
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
            SetStatus(Tf("status.rowsCannotMoveFurther", label));
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
        SetStatus(Tf("status.rowsMoved", rows.Count, label, _language.Text(direction < 0 ? "direction.up" : "direction.down")));
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
        NavigateQr(offset: -1);
    }

    private void NextQr_Click(object sender, RoutedEventArgs e)
    {
        NavigateQr(offset: 1);
    }

    private void AutoQr_Click(object sender, RoutedEventArgs e)
    {
        if (_autoQrTimer.IsEnabled)
        {
            StopAutoQr();
            return;
        }

        StartAutoQr();
    }

    private void AutoQrTimer_Tick(object? sender, EventArgs e)
    {
        if (_chunks.Count <= 1 || _qrView.Visibility != Visibility.Visible)
        {
            StopAutoQr(updateStatus: false);
            return;
        }

        NavigateQr(offset: 1);
    }

    private void StartAutoQr()
    {
        if (_chunks.Count <= 1)
        {
            StopAutoQr(updateStatus: false);
            SetStatus(T("status.qrAutoNeedsMultiplePages"));
            return;
        }

        var seconds = ParseDoubleRange(
            _autoQrSeconds,
            fallback: DefaultAutoQrIntervalSeconds,
            min: MinAutoQrIntervalSeconds,
            max: MaxAutoQrIntervalSeconds);
        var timerSeconds = Math.Max(seconds, MinDispatcherTimerSeconds);
        _autoQrTimer.Interval = TimeSpan.FromSeconds(timerSeconds);
        _autoQrTimer.Start();
        UpdateAutoQrUi();
        SetStatus(Tf("status.qrAutoStarted", FormatSeconds(seconds)));
    }

    private void StopAutoQr(bool updateStatus = true)
    {
        var wasEnabled = _autoQrTimer.IsEnabled;
        _autoQrTimer.Stop();
        UpdateAutoQrUi();
        if (updateStatus && wasEnabled)
        {
            SetStatus(T("status.qrAutoStopped"));
        }
    }

    private void UpdateAutoQrUi()
    {
        var visibility = _chunks.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _prevQrButton.Visibility = visibility;
        _nextQrButton.Visibility = visibility;
        _autoQrControls.Visibility = visibility;
        _autoQrButton.Content = _autoQrTimer.IsEnabled
            ? T("button.stopAutoQr")
            : T("button.startAutoQr");
        _autoQrButton.ToolTip = T("tooltip.autoQrToggle");
        _autoQrSeconds.ToolTip = T("tooltip.autoQrSeconds");
    }

    private void NavigateQr(int offset)
    {
        if (_chunks.Count == 0)
        {
            return;
        }

        _currentIndex = (_currentIndex + offset + _chunks.Count) % _chunks.Count;
        ShowCurrentQr();
        SetStatus(Tf("status.qrPage", _currentIndex + 1, _chunks.Count));
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
            SetStatus(Tf("status.jsonDisplayError", ex.Message), isError: true);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var window = new Window
        {
            Title = Tf("dialog.jsonTitle", _lastJson.Length.ToString("N0", CultureInfo.InvariantCulture)),
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
            Text = Tf(
                "dialog.jsonMeta",
                _lastJson.Length.ToString("N0", CultureInfo.InvariantCulture),
                Encoding.UTF8.GetByteCount(_lastJson).ToString("N0", CultureInfo.InvariantCulture)),
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
            var dialog = new OpenFileDialog { Filter = T("dialog.openJsonFilter") };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            LoadProjectJson(File.ReadAllText(dialog.FileName));
            Generate(showQrScreen: false);
            ShowInputScreen();
            SetStatus(Tf("status.jsonLoaded", Path.GetFileName(dialog.FileName)));
        }
        catch (Exception ex)
        {
            SetStatus(Tf("status.jsonLoadError", ex.Message), isError: true);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog { Filter = T("dialog.saveJsonFilter"), DefaultExt = "json" };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            CommitGridEdits();
            var jsonBytes = ProjectQrPayload.ProjectJsonBytes(BuildProject());
            _lastJson = Encoding.UTF8.GetString(jsonBytes);
            File.WriteAllBytes(dialog.FileName, jsonBytes);
            SetStatus(Tf("status.jsonSaved", Path.GetFileName(dialog.FileName)));
        }
        catch (Exception ex)
        {
            SetStatus(Tf("status.jsonSaveError", ex.Message), isError: true);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveQrImages_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitGridEdits();
            var project = BuildProject();
            var jsonBytes = ProjectQrPayload.ProjectJsonBytes(project);
            _chunks = ProjectQrPayload.EncodeProjectChunks(project, _chunkSize);
            _currentIndex = 0;
            _lastJson = Encoding.UTF8.GetString(jsonBytes);
            ShowCurrentQr();

            if (_chunks.Count == 0)
            {
                return;
            }

            var dialog = new OpenFolderDialog { Title = T("dialog.selectQrPngFolder") };
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

            var jsonFileName = $"plcio-{_chunks[0].Session}.json";
            File.WriteAllBytes(Path.Combine(dialog.FolderName, jsonFileName), jsonBytes);

            SetStatus(Tf("status.qrPngSaved", _chunks.Count, jsonFileName));
        }
        catch (Exception ex)
        {
            SetStatus(Tf("status.qrPngSaveError", ex.Message), isError: true);
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
            SetStatus(Tf("status.deviceBlockAdded", rows.Count));
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void MoveDeviceUp_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_devicesGrid, _devices, -1, T("noun.device"));

    private void MoveDeviceDown_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_devicesGrid, _devices, 1, T("noun.device"));

    private void DeleteDevice_Click(object sender, RoutedEventArgs e) => RemoveSelectedRows(_devicesGrid, _devices);

    private void AddWatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TimeChartAddresses().Count >= ProjectFactory.MaxTimeChartTargets)
            {
                SetStatus(Tf("status.timeChartMax", ProjectFactory.MaxTimeChartTargets), isError: true);
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
        MoveSelectedRows(_watchGrid, _watches, -1, T("noun.watch"));

    private void MoveWatchDown_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_watchGrid, _watches, 1, T("noun.watch"));

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
                SetStatus(Tf("status.timeChartMax", ProjectFactory.MaxTimeChartTargets), isError: true);
                return;
            }

            foreach (var row in rows)
            {
                _watches.Add(row);
            }

            SelectRows(_watchGrid, rows);
            SetStatus(Tf("status.watchBlockAdded", rows.Count, uniqueCount, ProjectFactory.MaxTimeChartTargets));
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void AddTrap_Click(object sender, RoutedEventArgs e)
    {
        if (_traps.Count >= ProjectFactory.MaxTrapDefinitions)
        {
            SetStatus(Tf("status.trapMax", ProjectFactory.MaxTrapDefinitions), isError: true);
            return;
        }

        var row = new TrapRow();
        row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
        _traps.Add(row);
        SelectNewRow(_trapsGrid, row);
    }

    private void UpdateTrapLimitUi()
    {
        var limitReached = _traps.Count >= ProjectFactory.MaxTrapDefinitions;
        _addTrapButton.IsEnabled = !limitReached;
        _addTrapButton.ToolTip = limitReached
            ? Tf("status.trapMax", ProjectFactory.MaxTrapDefinitions)
            : T("button.addRow");
    }

    private void MoveTrapUp_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_trapsGrid, _traps, -1, T("noun.trap"));

    private void MoveTrapDown_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_trapsGrid, _traps, 1, T("noun.trap"));

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
            _moduleIo.Text = FormatPrefixedHex(ReadRequiredHexInt(melsec, "moduleIoNo", 0, 0xFFFF), 4);
            _multidrop.Text = FormatPrefixedHex(ReadRequiredHexInt(melsec, "multidropNo", 0, 0xFF), 2);
            _remotePassword.Password = ReadRequiredString(melsec, "remotePassword");
        }
        else
        {
            _remotePassword.Password = "";
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
            row.Comment = ReadOptionalString(device, "comment");
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

        CommonizeDeviceComments();
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

    private static string DisplayVendor(string vendor) => vendor.Trim().ToUpperInvariant() switch
    {
        "MELSEC" => "MELSEC",
        "KEYENCE" => "KEYENCE",
        _ => vendor,
    };

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

    private static int ParseHexRange(TextBox textBox, int fallback, int min, int max, int width)
    {
        var value = Clamp(ParseHexInt(textBox.Text, fallback), min, max);
        textBox.Text = FormatPrefixedHex(value, width);
        return value;
    }

    private static double ParseDoubleRange(TextBox textBox, double fallback, double min, double max)
    {
        var value = Clamp(ParseDouble(textBox.Text, fallback), min, max);
        value = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        textBox.Text = FormatSeconds(value);
        return value;
    }

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static double ParseDouble(string text, double fallback)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            double.IsNaN(value) ||
            double.IsInfinity(value))
        {
            return fallback;
        }

        return value;
    }

    private static int ParseHexInt(string text, int fallback)
    {
        var token = text.Trim();
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            token = token[2..];
        }
        return int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private static string FormatPrefixedHex(int value, int width) =>
        $"0x{value.ToString($"X{width}", CultureInfo.InvariantCulture)}";

    private static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    private static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    private static string FormatSeconds(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static void RequireProjectJsonV2(System.Text.Json.JsonElement root)
    {
        var schema = ReadRequiredString(root, "schema");
        if (schema != "plc-io-checker-project")
        {
            throw new InvalidOperationException($"Unsupported project schema: {schema}");
        }

        var version = ReadRequiredInt(root, "schemaVersion");
        if (version != 3)
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

    private static string ReadOptionalString(System.Text.Json.JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined)
        {
            return "";
        }

        if (value.ValueKind == System.Text.Json.JsonValueKind.String &&
            value.GetString() is { } result)
        {
            return result;
        }

        throw new InvalidOperationException($"Project JSON value '{name}' must be a string.");
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

    private static int ReadRequiredHexInt(System.Text.Json.JsonElement element, string name, int min, int max)
    {
        var text = ReadRequiredString(element, name).Trim();
        if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result) ||
            result < min ||
            result > max)
        {
            throw new InvalidOperationException($"Project JSON value '{name}' must be a 0x-prefixed hexadecimal string.");
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
