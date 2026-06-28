using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PlcIoCheckerQr.Core;

namespace PlcIoCheckerQr.Wpf;

public partial class MainWindow
{
    private void AddDevice_Click(object sender, RoutedEventArgs e)
    {
        var row = new DeviceRow();
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

    private void AddComment_Click(object sender, RoutedEventArgs e)
    {
        var row = new CommentRow();
        row.SetDeviceContext(Selected(_vendor), SelectedKeyenceDeviceMode());
        _comments.Add(row);
        SelectNewRow(_commentsGrid, row);
    }

    private void MoveCommentUp_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_commentsGrid, _comments, -1, T("noun.comment"));

    private void MoveCommentDown_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRows(_commentsGrid, _comments, 1, T("noun.comment"));

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

    private static void SelectNewRow(DataGrid grid, object row)
    {
        grid.SelectedItem = row;
        grid.ScrollIntoView(row);
        if (grid.Columns.Count > 0)
        {
            grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[0]);
        }
    }

    private void RemoveSelectedRows<T>(DataGrid grid, ObservableCollection<T> source)
    {
        var selectedRows = grid.SelectedItems.OfType<T>().ToList();
        if (selectedRows.Count == 0)
        {
            return;
        }

        var removals = selectedRows
            .Select(row => (row, index: source.IndexOf(row)))
            .OrderBy(pair => pair.index)
            .ToList();

        foreach (var (row, _) in removals)
        {
            source.Remove(row);
        }

        _undoStack.Push(() =>
        {
            foreach (var (row, index) in removals)
            {
                source.Insert(Math.Min(index, source.Count), row);
            }

            SelectRows(grid, removals.Select(pair => pair.row));
            SetStatus(Tf("status.undone", removals.Count));
        });

        SetStatus(Tf("status.deleted", selectedRows.Count));
    }
}
