using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PlcIoCheckerQr.Core;
using QRCoder;
using static PlcIoCheckerQr.Wpf.ClipboardImport;
using static PlcIoCheckerQr.Wpf.NumericParsing;

using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingSize = System.Drawing.Size;

namespace PlcIoCheckerQr.Wpf;

public partial class MainWindow
{
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

        var host = _host.Text.Trim();
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            throw new ArgumentException(T("error.invalidHost"));
        }

        var intervalMs = ParseRange(_interval, fallback: 500, min: 50, max: 60000);
        var timeoutMs = ParseRange(_timeout, fallback: 2000, min: 100, max: 60000);
        if (timeoutMs < intervalMs)
        {
            throw new ArgumentException(T("error.timeoutShorterThanInterval"));
        }

        return ProjectFactory.MakeProject(new ProjectInput(
            Name: _projectName.Text,
            Vendor: Selected(_vendor),
            ConnectionMode: Selected(_connectionMode),
            Host: host,
            Port: ParseRange(_port, fallback: 1025, min: 1, max: 65535),
            MonitorIntervalMs: intervalMs,
            TimeoutMs: timeoutMs,
            MachineLabel: Selected(_model),
            KeyenceDeviceMode: Selected(_keyenceMode),
            TransportMode: Selected(_transport),
            Network: ParseRange(_network, fallback: 0, min: 0, max: 255),
            Station: ParseRange(_station, fallback: 255, min: 0, max: 255),
            ModuleIo: ParseHexRange(_moduleIo, fallback: 0x03FF, min: 0, max: 0xFFFF, width: 4),
            Multidrop: ParseHexRange(_multidrop, fallback: 0, min: 0, max: 0xFF, width: 2),
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
                    : NormalizeDeviceDataType(row.DataType.Trim(), address);
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
                    : NormalizeDeviceDataType(row.DataType.Trim(), address);
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
}
