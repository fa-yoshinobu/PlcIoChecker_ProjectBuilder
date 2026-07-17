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

            var fileInfo = new FileInfo(dialog.FileName);
            if (fileInfo.Length > ProjectFactory.MaxProjectJsonBytes)
            {
                throw new InvalidOperationException($"Project JSON exceeds {ProjectFactory.MaxProjectJsonBytes} bytes.");
            }
            var jsonBytes = File.ReadAllBytes(dialog.FileName);
            if (jsonBytes.Length > ProjectFactory.MaxProjectJsonBytes)
            {
                throw new InvalidOperationException($"Project JSON exceeds {ProjectFactory.MaxProjectJsonBytes} bytes.");
            }
            LoadProjectJson(Encoding.UTF8.GetString(jsonBytes));
            if (!Generate(showQrScreen: false))
            {
                return;
            }
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
            WriteFileAtomically(dialog.FileName, jsonBytes);
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
            WriteFileAtomically(Path.Combine(dialog.FolderName, jsonFileName), jsonBytes);

            SetStatus(Tf("status.qrPngSaved", _chunks.Count, jsonFileName));
        }
        catch (Exception ex)
        {
            SetStatus(Tf("status.qrPngSaveError", ex.Message), isError: true);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed record EditorRowState(string Address, string DataType, string Comment);

    private sealed record EditorTrapState(
        string Id,
        string Address,
        string DataType,
        string Comment,
        string Condition,
        string Threshold,
        bool Enabled);

    private sealed record EditorState(
        string? ProjectId,
        string ProjectName,
        string Vendor,
        string ConnectionMode,
        string Host,
        string Port,
        string Model,
        string Transport,
        string Interval,
        string Timeout,
        string Network,
        string Station,
        string ModuleIo,
        IReadOnlyList<EditorRowState> Devices,
        IReadOnlyList<EditorRowState> Comments,
        IReadOnlyList<EditorRowState> Watches,
        IReadOnlyList<EditorTrapState> Traps);

    private EditorState CaptureEditorState() => new(
        _projectId,
        _projectName.Text,
        Selected(_vendor),
        Selected(_connectionMode),
        _host.Text,
        _port.Text,
        Selected(_model),
        Selected(_transport),
        _interval.Text,
        _timeout.Text,
        _network.Text,
        _station.Text,
        Selected(_moduleIo),
        _devices.Select(row => new EditorRowState(row.Address, row.DataType, row.Comment)).ToArray(),
        _comments.Select(row => new EditorRowState(row.Address, row.DataType, row.Comment)).ToArray(),
        _watches.Select(row => new EditorRowState(row.Address, row.DataType, row.Comment)).ToArray(),
        _traps.Select(row => new EditorTrapState(
            row.Id,
            row.Address,
            row.DataType,
            row.Comment,
            row.Condition,
            row.Threshold,
            row.Enabled)).ToArray());

    private void RestoreEditorState(EditorState state)
    {
        _projectId = state.ProjectId;
        _projectName.Text = state.ProjectName;
        SelectItem(_vendor, state.Vendor);
        ApplyVendorDefaults();
        SelectItem(_connectionMode, state.ConnectionMode);
        _host.Text = state.Host;
        _port.Text = state.Port;
        SelectItem(_model, state.Model);
        SelectItem(_transport, state.Transport);
        _interval.Text = state.Interval;
        _timeout.Text = state.Timeout;
        _network.Text = state.Network;
        _station.Text = state.Station;
        SelectItem(_moduleIo, state.ModuleIo);

        var vendor = Selected(_vendor);
        var keyenceDeviceMode = SelectedKeyenceDeviceMode();
        _devices.Clear();
        foreach (var item in state.Devices)
        {
            var row = new DeviceRow();
            row.SetDeviceContext(vendor, keyenceDeviceMode);
            row.Address = item.Address;
            row.DataType = item.DataType;
            row.Comment = item.Comment;
            _devices.Add(row);
        }
        _comments.Clear();
        foreach (var item in state.Comments)
        {
            var row = new CommentRow();
            row.SetDeviceContext(vendor, keyenceDeviceMode);
            row.Address = item.Address;
            row.DataType = item.DataType;
            row.Comment = item.Comment;
            _comments.Add(row);
        }
        _watches.Clear();
        foreach (var item in state.Watches)
        {
            var row = new WatchRow();
            row.SetDeviceContext(vendor, keyenceDeviceMode);
            row.Address = item.Address;
            row.DataType = item.DataType;
            row.Comment = item.Comment;
            _watches.Add(row);
        }
        _traps.Clear();
        foreach (var item in state.Traps)
        {
            var row = new TrapRow();
            row.SetDeviceContext(vendor, keyenceDeviceMode);
            row.Id = item.Id;
            row.Address = item.Address;
            row.DataType = item.DataType;
            row.Comment = item.Comment;
            row.Condition = item.Condition;
            row.Threshold = item.Threshold;
            row.Enabled = item.Enabled;
            _traps.Add(row);
        }
        CommonizeDeviceComments();
        CommonizeDeviceDataTypes();
    }

    private void LoadProjectJson(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > ProjectFactory.MaxProjectJsonBytes)
        {
            throw new InvalidOperationException($"Project JSON exceeds {ProjectFactory.MaxProjectJsonBytes} bytes.");
        }

        var previous = CaptureEditorState();
        try
        {
            LoadProjectJsonCore(json);
            var project = BuildProject();
            _ = ProjectQrPayload.EncodeProjectChunks(project, _chunkSize);
        }
        catch
        {
            RestoreEditorState(previous);
            throw;
        }
    }

    private void LoadProjectJsonCore(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var root = document.RootElement;
        RequireProjectJsonV2(root);
        RequireOnlyProperties(
            root,
            "project",
            "schema", "schemaVersion", "exportInfo", "projectId", "projectName", "plc",
            "deviceList", "timeChart", "deviceMeta", "traps", "settings", "updatedAtEpochMs");
        if (root.TryGetProperty("exportInfo", out var exportInfo))
        {
            RequireOnlyProperties(exportInfo, "exportInfo", "source", "version");
            _ = ReadRequiredString(exportInfo, "source");
            _ = ReadRequiredString(exportInfo, "version");
        }
        if (root.TryGetProperty("settings", out var settings))
        {
            RequireOnlyProperties(
                settings,
                "settings",
                "blockDisplayDensity", "timeChartChannelDensity", "wordBarUpperLimit");
            var blockDensity = ReadRequiredString(settings, "blockDisplayDensity");
            var chartDensity = ReadRequiredString(settings, "timeChartChannelDensity");
            var wordBarUpperLimit = ReadRequiredInt(settings, "wordBarUpperLimit");
            if (blockDensity is not ("compact" or "detailed") ||
                chartDensity is not ("compact" or "detailed") ||
                wordBarUpperLimit <= 0)
            {
                throw new InvalidOperationException("Project JSON settings contain an unsupported value.");
            }
        }

        var projectId = ReadRequiredString(root, "projectId").Trim();
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new InvalidOperationException("Project JSON value 'projectId' must not be empty.");
        }
        _projectId = projectId;
        _ = ReadRequiredInt64(root, "updatedAtEpochMs");
        var projectName = ReadRequiredString(root, "projectName").Trim();
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new InvalidOperationException("Project JSON value 'projectName' must not be empty.");
        }
        _projectName.Text = projectName;

        var plc = ReadRequiredObject(root, "plc");
        RequireOnlyProperties(plc, "plc", "vendor", "cpuModel", "connection", "melsec");
        var connection = ReadRequiredObject(plc, "connection");
        RequireOnlyProperties(
            connection,
            "plc.connection",
            "mode", "host", "port", "transport", "pollingIntervalMs", "timeoutMs");
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

        SelectItem(_transport, ToUiTransport(ReadRequiredString(connection, "transport")));
        _interval.Text = ReadRequiredInt(connection, "pollingIntervalMs").ToString(CultureInfo.InvariantCulture);
        _timeout.Text = ReadRequiredInt(connection, "timeoutMs").ToString(CultureInfo.InvariantCulture);
        if (vendor == "Melsec")
        {
            var melsec = ReadRequiredObject(plc, "melsec");
            RequireOnlyProperties(melsec, "plc.melsec", "networkNo", "stationNo", "moduleIo");
            _network.Text = ReadRequiredInt(melsec, "networkNo").ToString(CultureInfo.InvariantCulture);
            _station.Text = ReadRequiredInt(melsec, "stationNo").ToString(CultureInfo.InvariantCulture);
            var moduleIoName = ReadRequiredString(melsec, "moduleIo");
            if (!ProjectFactory.ModuleIoTargets.Contains(moduleIoName))
            {
                throw new ProjectJsonException("error.invalidModuleIo", moduleIoName);
            }
            SelectItem(_moduleIo, moduleIoName);
            if (melsec.TryGetProperty("remotePassword", out _))
            {
                throw new ProjectJsonException("error.remotePasswordUnsupported", "plc.melsec.remotePassword");
            }
        }

        var deviceMetaByAddress = ReadDeviceMetaByAddress(root);
        _comments.Clear();
        var normalizedMetaAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (address, meta) in deviceMetaByAddress)
        {
            var row = new CommentRow();
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = NormalizeAddressText(address);
            row.DataType = NormalizeDeviceDataType(meta.DataType, row.Address);
            row.Comment = meta.Comment;
            if (!normalizedMetaAddresses.Add(row.Address))
            {
                throw new InvalidOperationException($"Duplicate normalized project JSON deviceMeta address: {row.Address}");
            }
            _comments.Add(row);
        }

        var devicesElement = ReadRequiredArray(root, "deviceList");
        RequireObjectArrayProperties(devicesElement, "deviceList", "address");
        if (devicesElement.GetArrayLength() > ProjectFactory.MaxDevices)
        {
            throw new InvalidOperationException($"Project JSON deviceList can contain up to {ProjectFactory.MaxDevices} rows.");
        }
        _devices.Clear();
        var deviceAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devicesElement.EnumerateArray())
        {
            var address = ReadRequiredString(device, "address");
            var meta = RequireDeviceMeta(deviceMetaByAddress, address);
            var row = new DeviceRow();
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = NormalizeAddressText(address);
            row.DataType = NormalizeDeviceDataType(meta.DataType, row.Address);
            row.Comment = meta.Comment;
            if (!deviceAddresses.Add(row.Address))
            {
                throw new InvalidOperationException($"Duplicate project JSON deviceList address: {row.Address}");
            }
            _devices.Add(row);
        }

        var timeChartElement = ReadRequiredArray(root, "timeChart");
        RequireObjectArrayProperties(timeChartElement, "timeChart", "address");
        if (timeChartElement.GetArrayLength() > ProjectFactory.MaxTimeChartTargets)
        {
            throw new InvalidOperationException($"Project JSON timeChart can contain up to {ProjectFactory.MaxTimeChartTargets} rows.");
        }
        _watches.Clear();
        var watchAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in timeChartElement.EnumerateArray())
        {
            var address = ReadRequiredString(target, "address");
            var meta = RequireDeviceMeta(deviceMetaByAddress, address);
            var row = new WatchRow();
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = NormalizeAddressText(address);
            row.DataType = NormalizeDeviceDataType(meta.DataType, row.Address);
            row.Comment = meta.Comment;
            if (!watchAddresses.Add(row.Address))
            {
                throw new InvalidOperationException($"Duplicate project JSON timeChart address: {row.Address}");
            }
            _watches.Add(row);
        }

        var trapsElement = ReadRequiredArray(root, "traps");
        RequireObjectArrayProperties(trapsElement, "traps", "id", "enabled", "address", "condition", "comparisonValue");
        if (trapsElement.GetArrayLength() > ProjectFactory.MaxTrapDefinitions)
        {
            throw new InvalidOperationException($"Project JSON traps can contain up to {ProjectFactory.MaxTrapDefinitions} rows.");
        }
        _traps.Clear();
        var trapIds = new HashSet<string>(StringComparer.Ordinal);
        var trapDefinitions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var trap in trapsElement.EnumerateArray())
        {
            var threshold = "";
            var hasThreshold = false;
            if (trap.TryGetProperty("comparisonValue", out var thresholdValue) &&
                thresholdValue.ValueKind != JsonValueKind.Null)
            {
                if (thresholdValue.ValueKind != JsonValueKind.Number)
                {
                    throw new InvalidOperationException("Project JSON value 'traps.comparisonValue' must be a number or null.");
                }
                var numericThreshold = thresholdValue.GetDouble();
                if (!double.IsFinite(numericThreshold))
                {
                    throw new InvalidOperationException("Project JSON value 'traps.comparisonValue' must be finite.");
                }
                threshold = numericThreshold.ToString("R", CultureInfo.InvariantCulture);
                hasThreshold = true;
            }

            var row = new TrapRow();
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Id = ReadRequiredString(trap, "id").Trim();
            if (string.IsNullOrWhiteSpace(row.Id) || !trapIds.Add(row.Id))
            {
                throw new InvalidOperationException($"Project JSON trap ID is empty or duplicated: {row.Id}");
            }
            var address = ReadRequiredString(trap, "address");
            var meta = RequireDeviceMeta(deviceMetaByAddress, address);
            row.Address = NormalizeAddressText(address);
            row.DataType = NormalizeDeviceDataType(meta.DataType, row.Address);
            row.Comment = meta.Comment;
            var condition = ToUiTrapCondition(ReadRequiredString(trap, "condition"));
            if (hasThreshold != ProjectFactory.TrapConditionRequiresThreshold(condition))
            {
                throw new InvalidOperationException($"Project JSON trap threshold does not match condition: {row.Id}");
            }
            row.Condition = condition;
            row.Threshold = threshold;
            row.Enabled = ReadRequiredBool(trap, "enabled");
            var definitionKey = $"{row.Address}|{row.Condition}|{row.Threshold}";
            if (!trapDefinitions.Add(definitionKey))
            {
                throw new InvalidOperationException($"Duplicate project JSON trap definition: {row.Address}");
            }
            _traps.Add(row);
        }

        CommonizeDeviceComments();
        CommonizeDeviceDataTypes();
    }

    private sealed record ProjectDeviceMeta(string DataType, string Comment);

    private static Dictionary<string, ProjectDeviceMeta> ReadDeviceMetaByAddress(JsonElement root)
    {
        var result = new Dictionary<string, ProjectDeviceMeta>(StringComparer.OrdinalIgnoreCase);
        var deviceMeta = ReadRequiredArray(root, "deviceMeta");
        RequireObjectArrayProperties(deviceMeta, "deviceMeta", "address", "dataType", "comment");
        if (deviceMeta.GetArrayLength() > ProjectFactory.MaxDeviceMeta)
        {
            throw new InvalidOperationException($"Project JSON deviceMeta can contain up to {ProjectFactory.MaxDeviceMeta} rows.");
        }
        foreach (var meta in deviceMeta.EnumerateArray())
        {
            var address = ReadRequiredString(meta, "address").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new InvalidOperationException("Project JSON value 'deviceMeta.address' must not be empty.");
            }

            if (!result.TryAdd(address, new ProjectDeviceMeta(
                    DataType: ToUiDataType(ReadRequiredString(meta, "dataType")),
                    Comment: ReadOptionalString(meta, "comment"))))
            {
                throw new InvalidOperationException($"Duplicate project JSON deviceMeta address: {address}");
            }
        }

        return result;
    }

    private static ProjectDeviceMeta RequireDeviceMeta(
        IReadOnlyDictionary<string, ProjectDeviceMeta> deviceMetaByAddress,
        string address)
    {
        var normalizedAddress = address.Trim().ToUpperInvariant();
        if (deviceMetaByAddress.TryGetValue(normalizedAddress, out var meta))
        {
            return meta;
        }

        throw new InvalidOperationException($"Project JSON value 'deviceMeta' is missing address '{normalizedAddress}'.");
    }

    private static void WriteFileAtomically(string path, byte[] data)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The output directory is invalid.");
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, data);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
