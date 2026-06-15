using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using PlcIoCheckerQr.Core;

namespace PlcIoCheckerQr.Wpf;

public partial class MainWindow
{
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
        _devicesGrid.Columns.Add(new DataGridTextColumn
        {
            Binding = new Binding(nameof(DeviceRow.Address)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _devicesGrid.Columns.Add(DeviceDataTypeColumn<DeviceRow>());
        _devicesGrid.Columns.Add(new DataGridTextColumn
        {
            Binding = new Binding(nameof(DeviceRow.Comment)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1.4, DataGridLengthUnitType.Star),
        });

        _watchGrid.ItemsSource = _watches;
        _watchGrid.PreviewKeyDown += WatchGrid_PreviewKeyDown;
        _watchGrid.Columns.Add(new DataGridTextColumn
        {
            Binding = new Binding(nameof(WatchRow.Address)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _watchGrid.Columns.Add(DeviceDataTypeColumn<WatchRow>());

        _trapsGrid.ItemsSource = _traps;
        _trapsGrid.PreviewKeyDown += TrapsGrid_PreviewKeyDown;
        _trapsGrid.Columns.Add(new DataGridTextColumn
        {
            Binding = new Binding(nameof(TrapRow.Address)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _trapsGrid.Columns.Add(DeviceDataTypeColumn<TrapRow>());
        _trapsGrid.Columns.Add(new DataGridTemplateColumn
        {
            CellTemplate = TrapConditionCellTemplate(),
            CellEditingTemplate = TrapConditionEditingTemplate(),
            Width = new DataGridLength(220),
        });
        _trapsGrid.Columns.Add(new DataGridTemplateColumn
        {
            CellTemplate = TrapThresholdCellTemplate(),
            CellEditingTemplate = TrapThresholdEditingTemplate(),
            Width = new DataGridLength(140),
        });
        _trapsGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Binding = new Binding(nameof(TrapRow.Enabled)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(90),
        });
    }

    private static DataGridTemplateColumn DeviceDataTypeColumn<T>() where T : DataTypedAddressRow =>
        new()
        {
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
}
