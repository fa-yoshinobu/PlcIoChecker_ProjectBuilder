using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PlcIoCheckerQr.Core;
using PlcIoCheckerQr.Wpf.Windows;
using static PlcIoCheckerQr.Wpf.ClipboardImport;
using static PlcIoCheckerQr.Wpf.NumericParsing;

namespace PlcIoCheckerQr.Wpf;

public partial class MainWindow
{
    private const int ClipboardMaxAttempts = 6;
    private const int ClipboardRetryDelayMs = 35;

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
        if (TryHandleRowDeleteShortcut(e, _devicesGrid, _devices))
        {
            return;
        }

        if (TryHandleMoveShortcut(e, _devicesGrid, _devices, T("noun.device")))
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.Z && _undoStack.TryPop(out var undo))
        {
            undo();
            e.Handled = true;
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

    private bool TryHandleRowDeleteShortcut<T>(
        KeyEventArgs e,
        DataGrid grid,
        ObservableCollection<T> source)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Delete ||
            Keyboard.Modifiers != ModifierKeys.None ||
            e.OriginalSource is TextBoxBase or ComboBox ||
            !grid.SelectedItems.OfType<T>().Any(row => source.Contains(row)))
        {
            return false;
        }

        RemoveSelectedRows(grid, source);
        e.Handled = true;
        return true;
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

        CopyRowsToClipboard(
            rows,
            "status.copiedDeviceRows",
            row => [row.Address, row.DataType, NormalizeDeviceComment(row.Comment)]);
    }

    private void PasteDevicesFromClipboard()
    {
        if (!TryGetClipboardText(out var clipboardText))
        {
            return;
        }

        var preview = ParseClipboardPreview(
            clipboardText,
            IsDeviceClipboardHeader,
            ParseDeviceClipboardRow,
            row => [row.Address, row.DataType, row.Comment],
            valueColumnCount: 3);
        if (preview.Count == 0)
        {
            SetStatus(T("status.noDeviceRowsPasted"), isError: true);
            return;
        }

        var rows = ConfirmPastePreview<DeviceRow>(T("section.devices.title"), DeviceValueHeaders(), preview);
        if (rows is null || rows.Count == 0)
        {
            return;
        }

        _devicesGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        _devicesGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var startIndex = SelectedIndexOrAppend(_devicesGrid, _devices);
        ApplyRowsWithUndo(_devicesGrid, _devices, startIndex, rows);
        MergeRowComments(rows);
        SelectRows(_devicesGrid, rows);

        var count = TimeChartAddresses().Count;
        var skipped = preview.Count - rows.Count;
        var pasteMsg = Tf("status.pastedDeviceRows", rows.Count, count, ProjectFactory.MaxTimeChartTargets);
        SetStatus(skipped > 0 ? $"{pasteMsg}  {Tf("status.clipboardSkipped", skipped)}" : pasteMsg);
    }

    private DeviceRow? ParseDeviceClipboardRow(string[] fields)
    {
        var address = NormalizeAddressText(fields[0]);
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        if (fields.Length <= 1 || !IsDeviceDataTypeField(fields[1]))
        {
            throw new ArgumentException($"Clipboard row for {address} requires an explicit data type.");
        }

        var row = new DeviceRow();
        row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
        row.Address = address;
        row.DataType = NormalizeDeviceDataType(fields[1], address);
        row.Comment = DeviceCommentFromFields(fields, FirstValueIndexAfterOptionalDataType(fields, hasDataType: true));
        return row;
    }

    private string NormalizeDeviceDataType(string text, string address) =>
        ClipboardImport.NormalizeDeviceDataType(text, address, Selected(_vendor), SelectedKeyenceDeviceMode());

    private void MergeRowComments(IEnumerable<DataTypedAddressRow> rows)
    {
        foreach (var row in rows.Where(row =>
                     !string.IsNullOrWhiteSpace(row.Address) && !string.IsNullOrWhiteSpace(row.Comment)))
        {
            AddOrFillCommentRow(row.Address, NormalizeDeviceComment(row.Comment), row.DataType, overwriteComment: true);
            ApplyCommentToRows(row.Address, NormalizeDeviceComment(row.Comment));
        }
    }

    private void CommentsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleRowDeleteShortcut(e, _commentsGrid, _comments))
        {
            return;
        }

        if (TryHandleMoveShortcut(e, _commentsGrid, _comments, T("noun.comment")))
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.Z && _undoStack.TryPop(out var undo))
        {
            undo();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C)
        {
            CopySelectedCommentsToClipboard();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V)
        {
            PasteCommentsFromClipboard();
            e.Handled = true;
        }
    }

    private void CopySelectedCommentsToClipboard()
    {
        _commentsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        _commentsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var rows = SelectedRows(_commentsGrid, _comments);
        if (rows.Count == 0)
        {
            return;
        }

        CopyRowsToClipboard(
            rows,
            "status.copiedCommentRows",
            row => [row.Address, row.DataType, NormalizeDeviceComment(row.Comment)]);
    }

    private void PasteCommentsFromClipboard()
    {
        if (!TryGetClipboardText(out var clipboardText))
        {
            return;
        }

        var preview = ParseClipboardPreview(
            clipboardText,
            IsCommentClipboardHeader,
            ParseCommentClipboardRow,
            row => [row.Address, row.DataType, row.Comment],
            valueColumnCount: 3);
        if (preview.Count == 0)
        {
            SetStatus(T("status.noCommentRowsPasted"), isError: true);
            return;
        }

        var rows = ConfirmPastePreview<CommentRow>(T("section.comments.title"), DeviceValueHeaders(), preview);
        if (rows is null || rows.Count == 0)
        {
            return;
        }

        _commentsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        _commentsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var startIndex = SelectedIndexOrAppend(_commentsGrid, _comments);
        ApplyRowsWithUndo(_commentsGrid, _comments, startIndex, rows);
        SelectRows(_commentsGrid, rows);

        var skipped = preview.Count - rows.Count;
        var pasteMsg = Tf("status.pastedCommentRows", rows.Count);
        SetStatus(skipped > 0 ? $"{pasteMsg}  {Tf("status.clipboardSkipped", skipped)}" : pasteMsg);
    }

    private CommentRow? ParseCommentClipboardRow(string[] fields)
    {
        var address = NormalizeAddressText(fields[0]);
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        if (fields.Length <= 1 || !IsDeviceDataTypeField(fields[1]))
        {
            throw new ArgumentException($"Clipboard row for {address} requires an explicit data type.");
        }

        var row = new CommentRow();
        row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
        row.Address = address;
        row.DataType = NormalizeDeviceDataType(fields[1], address);
        row.Comment = DeviceCommentFromFields(fields, FirstValueIndexAfterOptionalDataType(fields, hasDataType: true));
        return row;
    }

    private void WatchGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleRowDeleteShortcut(e, _watchGrid, _watches))
        {
            return;
        }

        if (TryHandleMoveShortcut(e, _watchGrid, _watches, T("noun.watch")))
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.Z && _undoStack.TryPop(out var undo))
        {
            undo();
            e.Handled = true;
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

        CopyRowsToClipboard(
            rows,
            "status.copiedWatchRows",
            row => [row.Address, row.DataType, NormalizeDeviceComment(row.Comment)]);
    }

    private void PasteTimeChartRowsFromClipboard()
    {
        if (!TryGetClipboardText(out var clipboardText))
        {
            return;
        }

        var preview = ParseClipboardPreview(
            clipboardText,
            IsAddressClipboardHeader,
            ParseWatchClipboardRow,
            row => [row.Address, row.DataType, row.Comment],
            valueColumnCount: 3);
        if (preview.Count == 0)
        {
            SetStatus(T("status.noWatchRowsPasted"), isError: true);
            return;
        }

        var rows = ConfirmPastePreview<WatchRow>(T("section.watch.title"), DeviceValueHeaders(), preview);
        if (rows is null || rows.Count == 0)
        {
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

        ApplyRowsWithUndo(_watchGrid, _watches, startIndex, rows);
        MergeRowComments(rows);
        SelectRows(_watchGrid, rows);

        var skipped = preview.Count - rows.Count;
        var pasteMsg = Tf("status.pastedWatchRows", rows.Count, uniqueCount, ProjectFactory.MaxTimeChartTargets);
        SetStatus(skipped > 0 ? $"{pasteMsg}  {Tf("status.clipboardSkipped", skipped)}" : pasteMsg);
    }

    private WatchRow? ParseWatchClipboardRow(string[] fields)
    {
        var address = NormalizeAddressText(fields[0]);
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        if (fields.Length <= 1 || !IsDeviceDataTypeField(fields[1]))
        {
            throw new ArgumentException($"Clipboard row for {address} requires an explicit data type.");
        }

        var row = new WatchRow();
        row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
        row.Address = address;
        row.DataType = NormalizeDeviceDataType(fields[1], address);
        row.Comment = DeviceCommentFromFields(fields, FirstValueIndexAfterOptionalDataType(fields, hasDataType: true));
        return row;
    }

    private static int UniqueWatchAddressCount(IEnumerable<WatchRow> rows) =>
        rows.Where(row => !string.IsNullOrWhiteSpace(row.Address))
            .Select(row => row.Address.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private void TrapsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleRowDeleteShortcut(e, _trapsGrid, _traps))
        {
            return;
        }

        if (TryHandleMoveShortcut(e, _trapsGrid, _traps, T("noun.trap")))
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.Z && _undoStack.TryPop(out var undo))
        {
            undo();
            e.Handled = true;
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

        CopyRowsToClipboard(
            rows,
            "status.copiedTrapRows",
            row => [row.Address, row.DataType, NormalizeDeviceComment(row.Comment), row.Condition, row.Threshold, row.Enabled ? "TRUE" : "FALSE"]);
    }

    private void PasteTrapsFromClipboard()
    {
        if (!TryGetClipboardText(out var clipboardText))
        {
            return;
        }

        var preview = ParseClipboardPreview(
            clipboardText,
            IsTrapClipboardHeader,
            ParseTrapClipboardRow,
            row => [row.Address, row.DataType, row.Comment, row.Condition, row.Threshold, row.Enabled ? "TRUE" : "FALSE"],
            valueColumnCount: 6);
        if (preview.Count == 0)
        {
            SetStatus(T("status.noTrapRowsPasted"), isError: true);
            return;
        }

        var rows = ConfirmPastePreview<TrapRow>(T("section.traps.title"), TrapValueHeaders(), preview);
        if (rows is null || rows.Count == 0)
        {
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

        ApplyRowsWithUndo(_trapsGrid, _traps, startIndex, rows);
        MergeRowComments(rows);
        SelectRows(_trapsGrid, rows);

        var skipped = preview.Count - rows.Count;
        var pasteMsg = Tf("status.pastedTrapRows", rows.Count);
        SetStatus(skipped > 0 ? $"{pasteMsg}  {Tf("status.clipboardSkipped", skipped)}" : pasteMsg);
    }

    private TrapRow? ParseTrapClipboardRow(string[] fields)
    {
        var address = NormalizeAddressText(fields[0]);
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var row = new TrapRow();
        row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
        row.Address = address;
        if (fields.Length <= 1 || !IsDeviceDataTypeField(fields[1]))
        {
            throw new ArgumentException($"Clipboard row for {address} requires an explicit data type.");
        }

        row.DataType = NormalizeDeviceDataType(fields[1], address);
        var conditionIndex = FirstValueIndexAfterOptionalDataType(fields, hasDataType: true);
        if (fields.Length > conditionIndex &&
            !IsTrapConditionField(fields[conditionIndex]) &&
            fields.Length > conditionIndex + 1)
        {
            row.Comment = NormalizeDeviceComment(fields[conditionIndex]);
            conditionIndex++;
        }

        var thresholdIndex = conditionIndex + 1;
        var enabledIndex = conditionIndex + 2;
        if (fields.Length <= conditionIndex || string.IsNullOrWhiteSpace(fields[conditionIndex]))
        {
            throw new ArgumentException($"Clipboard row for {address} requires an explicit trap condition.");
        }

        row.Condition = NormalizeTrapCondition(fields[conditionIndex], address);
        if (fields.Length > thresholdIndex && !string.IsNullOrWhiteSpace(fields[thresholdIndex]))
        {
            row.Threshold = fields[thresholdIndex].Trim();
        }

        row.Enabled = fields.Length <= enabledIndex || string.IsNullOrWhiteSpace(fields[enabledIndex]) || ParseClipboardBoolean(fields[enabledIndex]);
        return row;
    }

    private string NormalizeTrapCondition(string text, string address) =>
        ClipboardImport.NormalizeTrapCondition(text, address, Selected(_vendor), SelectedKeyenceDeviceMode());

    private void DeviceExcelMenu_Click(object sender, RoutedEventArgs e) =>
        ShowExcelMenu((Button)sender, PasteDevicesFromClipboard, CopySelectedDevicesToClipboard, DeviceTemplateTsv);

    private void WatchExcelMenu_Click(object sender, RoutedEventArgs e) =>
        ShowExcelMenu((Button)sender, PasteTimeChartRowsFromClipboard, CopySelectedTimeChartRowsToClipboard, DeviceTemplateTsv);

    private void TrapExcelMenu_Click(object sender, RoutedEventArgs e) =>
        ShowExcelMenu((Button)sender, PasteTrapsFromClipboard, CopySelectedTrapsToClipboard, TrapTemplateTsv);

    private void CommentExcelMenu_Click(object sender, RoutedEventArgs e) =>
        ShowExcelMenu((Button)sender, PasteCommentsFromClipboard, CopySelectedCommentsToClipboard, DeviceTemplateTsv);

    private const string DeviceTemplateTsv = "Address\tData type\tComment";
    private const string TrapTemplateTsv = "Address\tData type\tComment\tCondition\tThreshold\tEnabled";

    private void ShowExcelMenu(Button owner, Action paste, Action copy, string templateTsv)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = owner,
            Placement = PlacementMode.Bottom,
        };
        menu.Items.Add(ExcelMenuItem(T("excel.paste"), "Ctrl+V", paste));
        menu.Items.Add(ExcelMenuItem(T("excel.copy"), "Ctrl+C", copy));
        menu.Items.Add(new Separator());
        menu.Items.Add(ExcelMenuItem(T("excel.copyTemplate"), gesture: null, () => CopyTemplateToClipboard(templateTsv)));
        menu.IsOpen = true;
    }

    private static MenuItem ExcelMenuItem(string header, string? gesture, Action action)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        item.Click += (_, _) => action();
        return item;
    }

    private void CopyTemplateToClipboard(string templateTsv)
    {
        if (TrySetClipboardText(templateTsv))
        {
            SetStatus(T("status.templateCopied"));
        }
    }

    private IReadOnlyList<string> DeviceValueHeaders() =>
        [T("column.address"), T("column.dataType"), T("column.comment")];

    private IReadOnlyList<string> TrapValueHeaders() =>
        [T("column.address"), T("column.dataType"), T("column.comment"), T("column.condition"), T("column.threshold"), T("column.enabled")];

    private static List<PastePreviewRow> ParseClipboardPreview<T>(
        string text,
        Func<IReadOnlyList<string>, bool> isHeader,
        Func<string[], T?> parseRow,
        Func<T, string[]> displayValues,
        int valueColumnCount) where T : class
    {
        var preview = new List<PastePreviewRow>();
        var isFirstRow = true;
        var lineNumber = 0;
        foreach (var fields in SplitClipboardRows(text))
        {
            lineNumber++;
            if (fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (isFirstRow && isHeader(fields))
            {
                isFirstRow = false;
                continue;
            }

            isFirstRow = false;
            try
            {
                var row = parseRow(fields);
                preview.Add(row is null
                    ? new PastePreviewRow
                    {
                        LineNumber = lineNumber,
                        Values = FitPreviewValues(fields, valueColumnCount),
                    }
                    : new PastePreviewRow
                    {
                        LineNumber = lineNumber,
                        Values = displayValues(row),
                        ParsedRow = row,
                    });
            }
            catch (ArgumentException ex)
            {
                preview.Add(new PastePreviewRow
                {
                    LineNumber = lineNumber,
                    Values = FitPreviewValues(fields, valueColumnCount),
                    Error = ex.Message,
                });
            }
        }

        return preview;
    }

    private static string[] FitPreviewValues(IReadOnlyList<string> fields, int count)
    {
        var values = new string[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = index < fields.Count ? fields[index] : "";
        }

        if (fields.Count > count && count > 0)
        {
            values[count - 1] = string.Join(",", fields.Skip(count - 1));
        }

        return values;
    }

    private IReadOnlyList<T>? ConfirmPastePreview<T>(
        string sectionTitle,
        IReadOnlyList<string> valueHeaders,
        IReadOnlyList<PastePreviewRow> preview) where T : class
    {
        var window = new PastePreviewWindow(_language, sectionTitle, valueHeaders, preview) { Owner = this };
        if (window.ShowDialog() != true)
        {
            return null;
        }

        return preview
            .Where(row => row.IsValid)
            .Select(row => (T)row.ParsedRow!)
            .ToList();
    }

    private void CopyRowsToClipboard<T>(
        IReadOnlyCollection<T> rows,
        string copiedStatusKey,
        Func<T, IReadOnlyList<string>> toFields)
    {
        try
        {
            if (TrySetClipboardText(FormatClipboardRows(rows.Select(toFields))))
            {
                SetStatus(Tf(copiedStatusKey, rows.Count));
            }
        }
        catch (ArgumentException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private bool TryGetClipboardText(out string text)
    {
        text = "";
        var clipboardText = "";
        if (!TryUseClipboard(() =>
            {
                if (Clipboard.ContainsText())
                {
                    clipboardText = Clipboard.GetText(TextDataFormat.UnicodeText);
                }
            },
            out var exception))
        {
            SetClipboardUnavailableStatus(exception);
            return false;
        }

        text = clipboardText;
        return text.Length > 0;
    }

    private bool TrySetClipboardText(string text)
    {
        if (TryUseClipboard(() => Clipboard.SetText(text, TextDataFormat.UnicodeText), out var exception))
        {
            return true;
        }

        SetClipboardUnavailableStatus(exception);
        return false;
    }

    private static bool TryUseClipboard(Action operation, out Exception? exception)
    {
        exception = null;
        for (var attempt = 1; attempt <= ClipboardMaxAttempts; attempt++)
        {
            try
            {
                operation();
                return true;
            }
            catch (ExternalException ex) when (attempt < ClipboardMaxAttempts)
            {
                exception = ex;
                Thread.Sleep(ClipboardRetryDelayMs * attempt);
            }
            catch (ExternalException ex)
            {
                exception = ex;
                return false;
            }
        }

        return false;
    }

    private void SetClipboardUnavailableStatus(Exception? exception)
    {
        var detail = exception?.Message ?? T("status.clipboardUnavailableDetail");
        SetStatus(Tf("status.clipboardUnavailable", detail), isError: true);
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
        var count = ParseRange(countTextBox, min: 1, max: maxCount);
        foreach (var address in ProjectFactory.BuildDeviceBlock(startTextBox.Text, count, vendor, keyenceDeviceMode, machineLabel))
        {
            var row = new DeviceRow();
            row.SetDeviceContext(vendor, keyenceDeviceMode);
            row.Address = address;
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

    private void ApplyRowsWithUndo<T>(
        DataGrid grid,
        ObservableCollection<T> target,
        int startIndex,
        IReadOnlyList<T> rows)
    {
        var replaced = new List<T>();
        for (var index = 0; index < rows.Count && startIndex + index < target.Count; index++)
        {
            replaced.Add(target[startIndex + index]);
        }

        ApplyRows(target, startIndex, rows);

        _undoStack.Push(() =>
        {
            for (var index = rows.Count - 1; index >= replaced.Count; index--)
            {
                var targetIndex = startIndex + index;
                if (targetIndex < target.Count && ReferenceEquals(target[targetIndex], rows[index]))
                {
                    target.RemoveAt(targetIndex);
                }
            }

            for (var index = 0; index < replaced.Count; index++)
            {
                var targetIndex = startIndex + index;
                if (targetIndex < target.Count && ReferenceEquals(target[targetIndex], rows[index]))
                {
                    target[targetIndex] = replaced[index];
                }
            }

            SelectRows(grid, replaced);
            SetStatus(Tf("status.undone", rows.Count));
        });
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
}
