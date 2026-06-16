using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PlcIoCheckerQr.Core;
using static PlcIoCheckerQr.Wpf.ProjectJsonReader;
using static PlcIoCheckerQr.Wpf.UiValueMapping;

namespace PlcIoCheckerQr.Wpf;

public partial class MainWindow
{
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
        catch (ProjectJsonException ex)
        {
            var msg = Tf(ex.LocalizationKey, ex.Message);
            SetStatus(msg, isError: true);
            MessageBox.Show(this, msg, Title, MessageBoxButton.OK, MessageBoxImage.Error);
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
            var jsonSavedName = Path.GetFileName(dialog.FileName);
            SetStatus(Tf("status.jsonSaved", jsonSavedName));
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

    private void LoadProjectJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireProjectJsonV4(root);

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
        SelectItem(_model, ToUiMachineLabel(vendor, ReadRequiredString(plc, "cpuModel")));
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
            if (melsec.TryGetProperty("remotePassword", out _))
            {
                throw new ProjectJsonException("error.remotePasswordUnsupported", "plc.melsec.remotePassword");
            }
        }

        var devicesElement = ReadRequiredArray(root, "deviceList");
        _devices.Clear();
        foreach (var device in devicesElement.EnumerateArray())
        {
            var address = ReadRequiredString(device, "address");
            var row = new DeviceRow();
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = address;
            row.DataType = NormalizeDeviceDataType(ToUiDataType(ReadRequiredString(device, "dataType")), address);
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
            row.DataType = NormalizeDeviceDataType(ToUiDataType(ReadRequiredString(target, "dataType")), address);
            _watches.Add(row);
        }

        var trapsElement = ReadRequiredArray(root, "traps");
        _traps.Clear();
        foreach (var trap in trapsElement.EnumerateArray())
        {
            var threshold = "";
            if (trap.TryGetProperty("comparisonValue", out var thresholdValue) &&
                thresholdValue.ValueKind != JsonValueKind.Null)
            {
                if (thresholdValue.ValueKind != JsonValueKind.Number)
                {
                    throw new InvalidOperationException("Project JSON value 'traps.comparisonValue' must be a number or null.");
                }
                threshold = thresholdValue.GetDouble().ToString(CultureInfo.InvariantCulture);
            }

            var row = new TrapRow();
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = ReadRequiredString(trap, "address");
            row.DataType = NormalizeDeviceDataType(ToUiDataType(ReadRequiredString(trap, "dataType")), row.Address);
            row.Condition = ToUiTrapCondition(ReadRequiredString(trap, "condition"));
            row.Threshold = threshold;
            row.Enabled = ReadRequiredBool(trap, "enabled");
            _traps.Add(row);
        }

        CommonizeDeviceComments();
    }
}
