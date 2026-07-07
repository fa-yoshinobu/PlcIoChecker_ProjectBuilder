using System.Windows;
using System.Windows.Controls;
using PlcIoCheckerQr.Core;
using PlcIoCheckerQr.Wpf.Localization;

namespace PlcIoCheckerQr.Wpf;

public partial class MainWindow
{
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
        _manualMenuItem.Header = T("about.manual");
        _aboutMenuItem.Header = T("menu.about");
        _projectBuilderManualText.Text = T("manual.projectBuilder");
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
        _commentsTab.Header = T("tab.comments");
        _watchTab.Header = T("tab.watch");
        _trapsTab.Header = T("tab.traps");
        _projectTab.Header = T("tab.project");

        _devicesTitle.Text = T("section.devices.title");
        _devicesMeta.Text = T("section.devices.meta");
        _commentsTitle.Text = T("section.comments.title");
        _commentsMeta.Text = T("section.comments.meta");
        _watchTitle.Text = T("section.watch.title");
        _watchMeta.Text = Tf("section.watch.meta", ProjectFactory.MaxTimeChartTargets);
        _trapsTitle.Text = T("section.traps.title");
        _trapsMeta.Text = Tf("section.traps.meta", ProjectFactory.MaxTrapDefinitions);

        _deviceExcelButton.Content = _commentExcelButton.Content = _watchExcelButton.Content = _trapExcelButton.Content = T("button.excel");
        _deviceExcelButton.ToolTip = _commentExcelButton.ToolTip = _watchExcelButton.ToolTip = _trapExcelButton.ToolTip = T("tooltip.excelMenu");
        _moveDeviceUpButton.Content = _moveCommentUpButton.Content = _moveWatchUpButton.Content = _moveTrapUpButton.Content = T("button.up");
        _moveDeviceDownButton.Content = _moveCommentDownButton.Content = _moveWatchDownButton.Content = _moveTrapDownButton.Content = T("button.down");
        _addDeviceButton.Content = _addCommentButton.Content = _addWatchButton.Content = _addTrapButton.Content = T("button.addRow");
        _addDeviceBlockButton.Content = T("button.addBlock");

        _moveDeviceUpButton.ToolTip = T("tooltip.moveDeviceUp");
        _moveDeviceDownButton.ToolTip = T("tooltip.moveDeviceDown");
        _moveCommentUpButton.ToolTip = T("tooltip.moveCommentUp");
        _moveCommentDownButton.ToolTip = T("tooltip.moveCommentDown");
        _moveWatchUpButton.ToolTip = T("tooltip.moveWatchUp");
        _moveWatchDownButton.ToolTip = T("tooltip.moveWatchDown");
        _moveTrapUpButton.ToolTip = T("tooltip.moveTrapUp");
        _moveTrapDownButton.ToolTip = T("tooltip.moveTrapDown");
        _deviceBlockStart.ToolTip = T("tooltip.blockStart");
        _deviceBlockCount.ToolTip = T("tooltip.blockCount");

        _projectSectionTitle.Text = T("section.project.title");
        _projectSectionMeta.Text = T("section.project.meta");
        _projectNameLabel.Text = T("field.projectName");
        _vendorLabel.Text = T("field.vendor");
        _modelLabel.Text = T("field.model");
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
        RefreshModuleIoOptions();
        _resetRoutingDefaultsButton.Content = T("button.resetRoutingDefaults");

        _devicesGrid.ToolTip = T("tooltip.devicesGrid");
        _commentsGrid.ToolTip = T("tooltip.commentsGrid");
        _watchGrid.ToolTip = T("tooltip.watchGrid");
        _trapsGrid.ToolTip = T("tooltip.trapsGrid");
        if (_devicesGrid.Columns.Count >= 3)
        {
            _devicesGrid.Columns[0].Header = T("column.address");
            _devicesGrid.Columns[1].Header = T("column.dataType");
            _devicesGrid.Columns[2].Header = T("column.comment");
        }

        if (_commentsGrid.Columns.Count >= 3)
        {
            _commentsGrid.Columns[0].Header = T("column.address");
            _commentsGrid.Columns[1].Header = T("column.dataType");
            _commentsGrid.Columns[2].Header = T("column.comment");
        }

        if (_watchGrid.Columns.Count >= 3)
        {
            _watchGrid.Columns[0].Header = T("column.address");
            _watchGrid.Columns[1].Header = T("column.dataType");
            _watchGrid.Columns[2].Header = T("column.comment");
        }

        if (_trapsGrid.Columns.Count >= 6)
        {
            _trapsGrid.Columns[0].Header = T("column.address");
            _trapsGrid.Columns[1].Header = T("column.dataType");
            _trapsGrid.Columns[2].Header = T("column.comment");
            _trapsGrid.Columns[3].Header = T("column.condition");
            _trapsGrid.Columns[4].Header = T("column.threshold");
            _trapsGrid.Columns[5].Header = T("column.enabled");
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

        if (_isReadyStatus)
        {
            SetReadyStatus();
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

    private void LangButton_Click(object sender, RoutedEventArgs e)
    {
        var nextCode = T("language.next");
        _languageCode = LanguageCatalog.HasLanguage(nextCode)
            ? nextCode
            : LanguageCatalog.NextCode(_language.Code);
        ApplyLanguage();
        SetStatus(T("status.languageChanged"));
    }
}
