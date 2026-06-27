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
        _devicesGrid.CellEditEnding += AddressGrid_CellEditEnding;
        _devicesGrid.CurrentCellChanged += Grid_CurrentCellChanged;
        _devicesGrid.LostKeyboardFocus += Grid_LostKeyboardFocus;
        _devicesGrid.Columns.Add(AddressColumn<DeviceRow>());
        _devicesGrid.Columns.Add(DeviceDataTypeColumn<DeviceRow>());
        _devicesGrid.Columns.Add(CommentColumn<DeviceRow>());

        _commentsGrid.ItemsSource = _comments;
        _commentsGrid.PreviewKeyDown += CommentsGrid_PreviewKeyDown;
        _commentsGrid.CellEditEnding += AddressGrid_CellEditEnding;
        _commentsGrid.CurrentCellChanged += Grid_CurrentCellChanged;
        _commentsGrid.LostKeyboardFocus += Grid_LostKeyboardFocus;
        _commentsGrid.Columns.Add(AddressColumn<CommentRow>());
        _commentsGrid.Columns.Add(DeviceDataTypeColumn<CommentRow>());
        _commentsGrid.Columns.Add(CommentColumn<CommentRow>());

        _watchGrid.ItemsSource = _watches;
        _watchGrid.PreviewKeyDown += WatchGrid_PreviewKeyDown;
        _watchGrid.CellEditEnding += AddressGrid_CellEditEnding;
        _watchGrid.CurrentCellChanged += Grid_CurrentCellChanged;
        _watchGrid.LostKeyboardFocus += Grid_LostKeyboardFocus;
        _watchGrid.Columns.Add(AddressColumn<WatchRow>());
        _watchGrid.Columns.Add(DeviceDataTypeColumn<WatchRow>());
        _watchGrid.Columns.Add(CommentColumn<WatchRow>());

        _trapsGrid.ItemsSource = _traps;
        _trapsGrid.PreviewKeyDown += TrapsGrid_PreviewKeyDown;
        _trapsGrid.CellEditEnding += AddressGrid_CellEditEnding;
        _trapsGrid.CurrentCellChanged += Grid_CurrentCellChanged;
        _trapsGrid.LostKeyboardFocus += Grid_LostKeyboardFocus;
        _trapsGrid.Columns.Add(AddressColumn<TrapRow>());
        _trapsGrid.Columns.Add(DeviceDataTypeColumn<TrapRow>());
        _trapsGrid.Columns.Add(CommentColumn<TrapRow>());
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

    private static DataGridTextColumn AddressColumn<T>() where T : DataTypedAddressRow =>
        new()
        {
            Binding = new Binding(nameof(DataTypedAddressRow.Address)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            ElementStyle = UnsupportedDeviceTextStyle<TextBlock>(TextBlock.ForegroundProperty, TextBlock.FontWeightProperty),
            EditingElementStyle = UnsupportedDeviceTextStyle<TextBox>(TextBox.ForegroundProperty, Control.FontWeightProperty),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        };

    private static DataGridTextColumn CommentColumn<T>() where T : DataTypedAddressRow =>
        new()
        {
            Binding = new Binding(nameof(DataTypedAddressRow.Comment)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            EditingElementStyle = CommentTextBoxStyle(),
            Width = new DataGridLength(1.5, DataGridLengthUnitType.Star),
        };

    private static Style CommentTextBoxStyle()
    {
        var style = new Style(typeof(TextBox), Application.Current.TryFindResource(typeof(TextBox)) as Style);
        style.Setters.Add(new Setter(TextBox.MaxLengthProperty, ProjectCommentRules.MaxCommentCharacters));
        return style;
    }

    private static Style UnsupportedDeviceTextStyle<T>(
        DependencyProperty foregroundProperty,
        DependencyProperty fontWeightProperty) where T : FrameworkElement
    {
        var style = new Style(typeof(T), Application.Current.TryFindResource(typeof(T)) as Style);
        if (typeof(T) == typeof(TextBox))
        {
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        }

        style.Triggers.Add(new DataTrigger
        {
            Binding = new Binding(nameof(DataTypedAddressRow.IsUnsupportedDevice)),
            Value = true,
            Setters =
            {
                new Setter(foregroundProperty, new DynamicResourceExtension("ErrorFg")),
                new Setter(fontWeightProperty, FontWeights.SemiBold),
            },
        });

        return style;
    }

    private static DataGridTemplateColumn DeviceDataTypeColumn<T>() where T : DataTypedAddressRow =>
        new()
        {
            CellTemplate = DeviceDataTypeCellTemplate<T>(),
            CellEditingTemplate = DeviceDataTypeEditingTemplate<T>(),
            SortMemberPath = nameof(DataTypedAddressRow.DataType),
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
