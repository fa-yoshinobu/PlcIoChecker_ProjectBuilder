using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PlcIoCheckerQr.Core;
using static PlcIoCheckerQr.Wpf.ClipboardImport;
using static PlcIoCheckerQr.Wpf.NumericParsing;

namespace PlcIoCheckerQr.Wpf;

public partial class MainWindow
{
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

        try
        {
            var (rows, skipped) = ParseDeviceClipboardRows(Clipboard.GetText());
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

            MergeRowComments(rows);

            _devicesGrid.SelectedItems.Clear();
            foreach (var row in rows)
            {
                _devicesGrid.SelectedItems.Add(row);
            }

            var count = TimeChartAddresses().Count;
            var pasteMsg = Tf("status.pastedDeviceRows", rows.Count, count, ProjectFactory.MaxTimeChartTargets);
            SetStatus(skipped > 0 ? $"{pasteMsg}  {Tf("status.clipboardSkipped", skipped)}" : pasteMsg);
        }
        catch (ArgumentException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private (IReadOnlyList<DeviceRow> Rows, int Skipped) ParseDeviceClipboardRows(string text)
    {
        var rows = new List<DeviceRow>();
        var skipped = 0;
        var isFirstRow = true;

        foreach (var fields in SplitClipboardRows(text))
        {
            if (fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (isFirstRow && IsDeviceClipboardHeader(fields))
            {
                isFirstRow = false;
                continue;
            }

            isFirstRow = false;
            var address = NormalizeAddressText(fields[0]);
            if (string.IsNullOrWhiteSpace(address))
            {
                skipped++;
                continue;
            }

            if (fields.Length <= 1 || !IsDeviceDataTypeField(fields[1]))
            {
                throw new ArgumentException($"Clipboard row for {address} requires an explicit data type.");
            }

            var dataType = NormalizeDeviceDataType(fields[1], address);
            var commentIndex = FirstValueIndexAfterOptionalDataType(fields, hasDataType: true);
            var row = new DeviceRow();
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = address;
            row.DataType = dataType;
            row.Comment = DeviceCommentFromFields(fields, commentIndex);
            rows.Add(row);
        }

        return (rows, skipped);
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

        Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(row =>
            $"{row.Address}\t{row.DataType}\t{NormalizeDeviceComment(row.Comment)}")));
        SetStatus(Tf("status.copiedCommentRows", rows.Count));
    }

    private void PasteCommentsFromClipboard()
    {
        if (!Clipboard.ContainsText())
        {
            return;
        }

        try
        {
            var rows = ParseCommentClipboardRows(Clipboard.GetText()).ToList();
            if (rows.Count == 0)
            {
                SetStatus(T("status.noCommentRowsPasted"), isError: true);
                return;
            }

            _commentsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
            _commentsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

            var startIndex = SelectedIndexOrAppend(_commentsGrid, _comments);
            ApplyRows(_comments, startIndex, rows);
            SelectRows(_commentsGrid, rows);
            SetStatus(Tf("status.pastedCommentRows", rows.Count));
        }
        catch (ArgumentException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private IEnumerable<CommentRow> ParseCommentClipboardRows(string text)
    {
        var isFirstRow = true;
        foreach (var fields in SplitClipboardRows(text))
        {
            if (fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (isFirstRow && IsCommentClipboardHeader(fields))
            {
                isFirstRow = false;
                continue;
            }

            isFirstRow = false;
            var address = NormalizeAddressText(fields[0]);
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            if (fields.Length <= 1 || !IsDeviceDataTypeField(fields[1]))
            {
                throw new ArgumentException($"Clipboard row for {address} requires an explicit data type.");
            }

            var row = new CommentRow();
            row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
            row.Address = address;
            row.DataType = NormalizeDeviceDataType(fields[1], address);
            var commentIndex = FirstValueIndexAfterOptionalDataType(fields, hasDataType: true);
            row.Comment = DeviceCommentFromFields(fields, commentIndex);
            yield return row;
        }
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

        Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(row =>
            $"{row.Address}\t{row.DataType}\t{NormalizeDeviceComment(row.Comment)}")));
        SetStatus(Tf("status.copiedWatchRows", rows.Count));
    }

    private void PasteTimeChartRowsFromClipboard()
    {
        if (!Clipboard.ContainsText())
        {
            return;
        }

        try
        {
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
            MergeRowComments(rows);
            SelectRows(_watchGrid, rows);
            SetStatus(Tf("status.pastedWatchRows", rows.Count, uniqueCount, ProjectFactory.MaxTimeChartTargets));
        }
        catch (ArgumentException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private IEnumerable<WatchRow> ParseWatchClipboardRows(string text)
    {
        var isFirstRow = true;
        foreach (var fields in SplitClipboardRows(text))
        {
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
            var address = NormalizeAddressText(fields[0]);
            if (!string.IsNullOrWhiteSpace(address))
            {
                var row = new WatchRow();
                row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
                row.Address = address;
                if (fields.Length <= 1 || !IsDeviceDataTypeField(fields[1]))
                {
                    throw new ArgumentException($"Clipboard row for {address} requires an explicit data type.");
                }

                row.DataType = NormalizeDeviceDataType(fields[1], address);
                var commentIndex = FirstValueIndexAfterOptionalDataType(fields, hasDataType: true);
                row.Comment = DeviceCommentFromFields(fields, commentIndex);
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

        Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(row =>
            $"{row.Address}\t{row.DataType}\t{NormalizeDeviceComment(row.Comment)}\t{row.Condition}\t{row.Threshold}\t{(row.Enabled ? "TRUE" : "FALSE")}")));
        SetStatus(Tf("status.copiedTrapRows", rows.Count));
    }

    private void PasteTrapsFromClipboard()
    {
        if (!Clipboard.ContainsText())
        {
            return;
        }

        try
        {
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
            MergeRowComments(rows);
            SelectRows(_trapsGrid, rows);
            SetStatus(Tf("status.pastedTrapRows", rows.Count));
        }
        catch (ArgumentException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private IEnumerable<TrapRow> ParseTrapClipboardRows(string text)
    {
        var isFirstRow = true;
        foreach (var fields in SplitClipboardRows(text))
        {
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
            var address = NormalizeAddressText(fields[0]);
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
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
            yield return row;
        }
    }

    private string NormalizeTrapCondition(string text, string address) =>
        ClipboardImport.NormalizeTrapCondition(text, address, Selected(_vendor), SelectedKeyenceDeviceMode());

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
